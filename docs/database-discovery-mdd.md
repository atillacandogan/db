# Database Discovery – Technical Design & Detection Flow

> İnceleme tarihi: 13 Ağustos 2026
> İncelenen kaynak: mevcut çalışma ağacındaki frontend, API, application, worker, discovery infrastructure, entity/DTO ve repository kodu
> Kapsam dışı: normal target güvenlik taraması (`NmapScannerRuntime`, authenticated scan probe'ları). Bu belge yalnızca **Database Discovery** akışını anlatır.

## 1. Yönetici özeti

Database Discovery, Nmap kullanmayan, credentialless ve asenkron bir TCP discovery hattıdır. Kullanıcı UI'dan yerel ağ veya belirli hedef modu seçer; API bir `DiscoveryJob` kaydedip RabbitMQ'ya yalnızca job kimliğini yollar. Scanner Worker job'ı alır, scope'u yeniden planlar, her planlı host/port için `TcpClient.ConnectAsync` çalıştırır, açık portları sıralı detector zincirinden geçirir ve sonuçları PostgreSQL veritabanında saklar. UI aktif job varken iki saniyede bir `/api/discoveries` endpoint'ini poll eder.

Sistemde port seçimi ile engine kimliği ayrılmıştır. Tarama planındaki `ExpectedEngine` ve default port yalnız **ilk denenecek protocol probe'u** belirler. Açık portta MySQL, PostgreSQL, MSSQL ve Oracle protokolleri ayrı ve temiz TCP bağlantılarıyla sırayla denenir; engine yalnız geçerli protocol evidence bulunduğunda atanır. Bu nedenle en doğru tanım:

> **Port/configuration ile taranacak endpoint seçimi + protocol-centric engine classification.**

Engine'ler eşit doğrulama politikasına sahip değildir:

- MySQL: server-first protocol-v10 handshake ayrı bağlantıda pasif okunur. Geçerliyse `95 / Confirmed`; yoksa engine atanmaz.
- PostgreSQL: credentialless SSLRequest ayrı bağlantıda gönderilir. Tek-byte `S` veya `N` cevabı varsa `98 / Confirmed`; yoksa engine atanmaz.
- MSSQL: TDS pre-login ayrı bağlantıda gönderilir. Geçerli cevap varsa `98 / Confirmed`; yoksa engine atanmaz.
- Oracle: TNS `(CONNECT_DATA=(COMMAND=version))` ayrı bağlantıda gönderilir. Geçerli Listener cevabı varsa `98 / Confirmed`; yoksa engine atanmaz.
- Beklenen/default port açık fakat hiçbir database protokolü doğrulanamazsa `30 / PortHint`, engine `null` ve unsupported olur.
- Redis ve MongoDB: yalnız port numarasıyla bilgi amaçlı unsupported `30 / PortHint` sonucu üretir.
- Diğer yapılandırılmış unsupported açık port: `Unknown TCP service`, `25 / Unknown`.

Kaynaklar: `src/DbScanner.Infrastructure/Discovery/DiscoveryAgent.cs:56-102`, `TcpDiscoveryProbe.cs:39-166`, engine detector dosyaları ve `DiscoveryResultClassifier.cs:9-42`.

## 2. Uçtan uca gerçek akış

### 2.1 UI: Discovery Başlat

1. `DiscoveryPanel` başlangıçta local network modunu, `demo` environment'ını ve dört supported engine'i seçer (`discovery-panel.tsx:34-48`, `78-83`).
2. Local Network modunda UI `/api/discoveries/local-networks` ile uygun interface/CIDR listesini alır; taranabilir network'leri otomatik seçer (`discovery-panel.tsx:127-138`).
3. Advanced modunda UI IPv4, CIDR, IP aralığı veya hostname toplar. IP aralığını API'ye yollamadan önce ayrı IPv4 satırlarına genişletir (`discovery-panel.tsx:1112-1201`).
4. Advanced modunda ilk submit yalnız `/api/discoveries/validate-scope` çağrısını yapar; ikinci submit backend'in `Authorized` döndürdüğü hedeflerle job oluşturur (`discovery-panel.tsx:248-329`).
5. Local Network submit'i `/api/discoveries/local-network`; Advanced submit'i `/api/discoveries` endpoint'ine gider (`src/DbScanner.Web/src/lib/api/discoveries.ts:39-59`).

### 2.2 API ve job oluşturma

- Local Network: `POST /api/discoveries/local-network`, Operator policy (`Program.cs:554-575`).
- Advanced/legacy automatic: `POST /api/discoveries`, Operator policy (`Program.cs:610-633`).
- Scope doğrulama: `POST /api/discoveries/validate-scope`, Operator policy (`Program.cs:635-654`).
- Job listeleme/detay: `GET /api/discoveries` ve `GET /api/discoveries/{id}`, Viewer policy (`Program.cs:537-545`, `577-586`).

`DiscoveryService` adı/environment'ı normalize eder, engine filtresini yalnız MySQL, PostgreSQL, MSSQL ve Oracle ile sınırlar ve scope'tan en az bir endpoint planlanabildiğini job yaratmadan önce doğrular (`DiscoveryService.cs:78-175`, `446-485`).

`QueueJobAsync`:

1. `DiscoveryJob`'ı `Pending` oluşturur.
2. Önce repository ile kaydeder.
3. Sonra `RabbitMqDiscoveryJobPublisher` ile `DiscoveryRequestedMessage(jobId, requestedAt)` yayınlar.
4. API `202 Accepted` ve henüz sonuç içermeyen job DTO'sunu döndürür.

Kaynaklar: `DiscoveryService.cs:424-443`, `RabbitMqDiscoveryJobPublisher.cs:17-38`, `DiscoveryRequestedMessage.cs:3-5`.

### 2.3 Worker, probe, classification ve persistence

`DbScanner.ScannerWorker.Worker`, normal scan ve NSE sync consumer'larıyla birlikte discovery consumer'ı başlatır (`Worker.cs:12-29`). RabbitMQ consumer mesajı deserialize eder, scoped `IDiscoveryAgent` çözer, `ExecuteAsync(jobId)` çağırır ve başarıda ACK, hatada requeue etmeden NACK verir (`RabbitMqDiscoveryJobConsumer.cs:20-64`).

`DiscoveryAgent.ExecuteAsync` gerçek discovery işidir:

1. Atomik olarak yalnız `Pending` job'ı `Running` yapar.
2. Worker tarafında scope'u ikinci kez planlar (defense in depth).
3. Endpoint'leri `Parallel.ForEachAsync` ile tarar.
4. Kapalı/timeout portları sonuç üretmeden eler.
5. Açık portu detector'lara `Order` sırasıyla verir; ilk non-null fingerprint kazanır.
6. Fingerprint'i supported/unsupported olarak sınıflandırır.
7. `(normalizedHost, port, detectedEngine, environment)` bazında job içi duplicate'leri birleştirir; yüksek fingerprint level ve confidence kazanır.
8. Repository sonuçları kaydeder ve job'ı `Completed` yapar.
9. Exception olursa job `Failed` olur ve hata mesajı güvenli biçimde sınırlandırılır.

Kaynaklar: `DiscoveryAgent.cs:21-128`, `DiscoveryRepository.cs:40-110`.

### 2.4 UI'a dönüş

POST response nihai sonuç response'u değildir. UI, herhangi bir job `pending` veya `running` iken `/api/discoveries` listesini **2.000 ms** aralıkla yeniler (`discovery-panel.tsx:112-119`). Backend job/results entity'lerini DTO'ya map eder ve sonuçları `supportedDatabases` ile `unsupportedDatabases` dizilerine ayırır (`DiscoveryService.cs:517-583`). UI bu dizileri iki ayrı tabloda gösterir (`discovery-panel.tsx:1017-1100`).

## 3. Mermaid sequence diagram

```mermaid
sequenceDiagram
    actor User as Kullanıcı
    participant UI as DiscoveryPanel
    participant API as Program.cs Minimal API
    participant Service as DiscoveryService
    participant Scope as DiscoveryScopeValidator
    participant Repo as DiscoveryRepository
    participant MQ as RabbitMQ discovery queue
    participant Consumer as RabbitMqDiscoveryJobConsumer
    participant Agent as DiscoveryAgent
    participant Probe as TcpDiscoveryProbe
    participant Detector as IDatabaseFingerprintDetector zinciri
    participant Classifier as DiscoveryResultClassifier
    participant DB as PostgreSQL / EF Core

    User->>UI: Discovery Başlat
    alt Advanced ilk submit
        UI->>API: POST /api/discoveries/validate-scope
        API->>Service: ValidateScopeAsync
        Service->>Scope: ValidateAndPlanDetailedAsync
        Scope-->>UI: Authorized / rejected hedefler
    end
    UI->>API: POST /api/discoveries veya /local-network
    API->>Service: CreateAsync / CreateLocalNetworkAsync
    Service->>Scope: Endpoint planını önceden doğrula
    Service->>Repo: Add job (Pending) + SaveChanges
    Repo->>DB: INSERT discovery_jobs
    Service->>MQ: DiscoveryRequestedMessage(jobId)
    API-->>UI: 202 Accepted + Pending job
    MQ->>Consumer: Mesaj teslimi
    Consumer->>Agent: ExecuteAsync(jobId)
    Agent->>Repo: TryMarkRunningAsync
    Repo->>DB: Pending → Running, StartedAt
    Agent->>Scope: Scope'u worker'da yeniden planla
    loop Her planlı host/port (paralel, rate-limited)
        Agent->>Probe: ProbeAsync(endpoint)
        Probe->>Probe: ExpectedEngine önce; her protocol için temiz TCP bağlantısı
        alt Port kapalı / connect timeout
            Probe-->>Agent: IsOpen=false
        else Port açık
            Probe-->>Agent: ExpectedEngine hint + doğrulanmış ProtocolEvidence
            Agent->>Detector: Order sırasıyla Detect
            Agent->>Classifier: Classify(fingerprint, engineFilter)
        end
    end
    Agent->>Repo: CompleteAsync(deduplicatedResults)
    Repo->>DB: Sonuçları kaydet, job Completed
    loop Aktif job varken her 2 saniye
        UI->>API: GET /api/discoveries
        API->>Service: ListAsync
        Service->>Repo: Job + results + target inventory
        API-->>UI: supportedDatabases / unsupportedDatabases
    end
```

## 4. Scope ve endpoint planlama

### 4.1 Local Network

`LocalNetworkProvider` yalnız çalışan Ethernet/Wi-Fi benzeri interface'lerin RFC1918 IPv4 adreslerini alır. Docker, veth, Hyper-V, WSL, VMware, VirtualBox, VPN, tunnel, Tailscale, Zerotier ve loopback marker'lı interface'ler elenir. Process container içinde çalışıyorsa liste boş döner (`LocalNetworkProvider.cs:12-35`, `47-77`, `80-97`).

Seçilen CIDR'lar için:

- yalnız RFC1918 IPv4 network kabul edilir;
- tek network ve seçilen network'lerin toplam ham adres sayısı `MaxCidrHosts` değerini aşamaz;
- mevcut API/worker ayarında limit `512` adrestir;
- prefix `<= /30` ise network ve broadcast adresleri taranmaz;
- `/31` ve `/32` için adresler atlanmaz;
- örneğin `/24`, 256 adres sınır hesabına girer ve 254 host endpoint'e genişler.

Kaynaklar: `DiscoveryScopeValidator.cs:57-109`, `NetworkScope.cs:65-81`, `103-127`, `src/DbScanner.Api/appsettings.json:18-27`, `src/DbScanner.ScannerWorker/appsettings.json:7-24`.

Local network oluşturma sırasında API seçilen CIDR'ın gerçekten o anda algılanmış uygun interface listesinde olduğunu kontrol eder (`DiscoveryService.cs:78-124`). Worker ise saklanan CIDR'ı tekrar private/size kontrolünden geçirir, fakat interface'in hâlâ mevcut olduğunu tekrar sorgulamaz (`DiscoveryAgent.cs:38-54`).

### 4.2 Advanced / belirli hedef

Backend scope tokenizer virgül, noktalı virgül, whitespace ve satır sonunu ayırıcı kabul eder ve case-insensitive duplicate'leri kaldırır (`DiscoveryScopeValidator.cs:559-563`).

- IPv6 açıkça desteklenmez.
- IPv4 yalnız private unicast ise kabul edilir; public, loopback, link-local, multicast, `0.0.0.0` ve broadcast reddedilir.
- CIDR yalnız private IPv4 ise ve host limiti aşmıyorsa genişletilir.
- Hostname DNS ile IPv4'e çözülür; dönen adreslerin **herhangi biri** private/unsafe kontrolünü geçemezse hedef bütünüyle reddedilir.
- DNS timeout varsayılan/effective değer `2.000 ms`'dir.
- Combined expanded host sayısı `MaxCidrHosts` değerini aşamaz.
- UI IP range'i destekler; backend'in ayrı bir range sözdizimi yoktur. UI range'i tek tek IPv4 satırlarına çevirir.

Kaynaklar: `DiscoveryScopeValidator.cs:134-191`, `194-316`, `DiscoveryDnsResolver.cs:8-31`, `discovery-panel.tsx:1112-1201`.

Önemli uygulama ayrıntısı: mevcut `ValidateAndPlanDetailedAsync` manual/advanced yolunda `ValidateExplicitIp`, `ValidateExplicitCidr` ve `ValidateExplicitHostnameAsync` kullanılır. Bu yol environment-specific `scan_scope_allowlist` kaydı aramaz; güvenlik sınırı private IPv4/size/DNS kontrolleridir. Environment doğrulanır ve job/result'a yazılır. Dosyada allowlist kullanan `ValidateIp`, `ValidateCidr`, `ValidateHostnameAsync` yardımcıları bulunsa da manual dispatcher bunları çağırmaz (`DiscoveryScopeValidator.cs:134-227`, `230-307`, `386-448`).

### 4.3 Legacy “All authorized endpoints”

API request'inde `useAuthorizedScope=true` ise veya hem flag hem scope boşsa `__all_authorized__` saklanır (`Program.cs:617-624`, `DiscoveryScopeSelection.cs:3-26`). Bu mod mevcut modalde sunulmuyor; API uyumluluk yolu olarak duruyor.

Bu mod active allowlist kayıtlarını environment ve seçilen engine filtresine göre planlar. Portlar `AllowedPorts`; o yoksa legacy `Port`; o da yoksa engine standard portundan gelir (`DiscoveryScopeValidator.cs:19-55`, `451-519`, `DiscoveryAllowlistPolicy.cs:32-68`). Local/Advanced'den farklı olarak global `UnsupportedDatabasePorts` listesi otomatik eklenmez.

## 5. Taranan portlar ve tarama tekniği

### 5.1 Etkin port matrisi

Mevcut `appsettings.json` değerleri hem API hem Worker'da şöyledir:

| Aday | Taranan portlar | Kaynak |
| --- | --- | --- |
| MySQL | `3306`, `13306` | `Discovery:LocalNetworkPorts:mysql` |
| PostgreSQL | `5432`, `15432` | `Discovery:LocalNetworkPorts:postgresql` |
| MSSQL | `1433`, `11433` | `Discovery:LocalNetworkPorts:mssql` |
| Oracle | `1521`, `11521` | `Discovery:LocalNetworkPorts:oracle` |
| Unsupported bilgi portları | `6379`, `27017` | `Discovery:UnsupportedDatabasePorts` |

Kaynaklar: `src/DbScanner.ScannerWorker/appsettings.json:7-24`, `src/DbScanner.Api/appsettings.json:18-27`.

Kodda ayrıca standard port tablosu vardır: MySQL `3306`, PostgreSQL `5432`, MSSQL `1433`, Oracle `1521` (`DiscoveryAllowlistPolicy.cs:8-15`). `DiscoveryOptions.GetLocalNetworkPorts` configured listeye standard portu her durumda ekleyip distinct/sort eder (`DiscoveryOptions.cs:50-59`). Dolayısıyla config standard portu yazmasa bile taranır.

Engine seçimi supported port listesini değiştirir: yalnız seçilen engine'ler için configured+standard portlar planlanır. Fakat Local/Advanced yolları `6379` ve `27017` unsupported portlarını engine seçiminden bağımsız olarak **her hosta ekler** (`DiscoveryScopeValidator.cs:90-103`, `309-316`). UI'daki “Yalnızca seçilen engine portları kontrol edilir” metni (`discovery-panel.tsx:846-850`) bu iki ek portu yansıtmamaktadır.

### 5.2 TCP, timeout, concurrency ve rate limit

- Tarama `TcpClient.ConnectAsync`; ICMP ping veya ayrı host-discovery aşaması yoktur (`TcpDiscoveryProbe.cs:39-77`, `111-132`).
- TCP connect timeout: `1.000 ms`.
- Banner/protocol read timeout: `1.000 ms`.
- DNS timeout: `2.000 ms`.
- Worker job timeout: `600 s` effective config; sınıf default'u `300 s`.
- Maksimum paralellik: `8` endpoint.
- Global probe hızı: en fazla `20/s`; ayrıca minimum `50 ms` probe aralığı. Gerçek interval `max(ProbeDelay, 1000/MaxProbesPerSecond)` ile hesaplanır.
- Maksimum okunan banner: `512 byte`.

Kaynaklar: `DiscoveryOptions.cs:10-19`, `ScannerWorker/appsettings.json:7-24`, `DiscoveryAgent.cs:31-33`, `56-74`, `131-152`, `TcpDiscoveryProbe.cs:168-207`.

Unreachable host için host-wide short-circuit yoktur. Her planlı port ayrı probe akışıdır. İlk TCP bağlantısındaki `SocketException` veya connect timeout `IsOpen=false` üretir; agent endpoint'i sonuçsuz eler (`TcpDiscoveryProbe.cs:39-54`, `111-132`; `DiscoveryAgent.cs:73-78`). Kapalı port da aynı noktada elenir. Job timeout veya beklenmeyen exception job'ı `Failed` yapabilir.

## 6. Database classification ve fingerprinting

### 6.1 Probe sırası ve engine kararı

Her `DiscoveryEndpoint`, `Host`, `Port`, nullable `ExpectedEngine` ve optional `ConnectAddress` taşır (`DiscoveryEndpoint.cs:5-9`). Local/Advanced planlayıcı selected engine ile o engine'e atanmış portları çaprazlayarak `ExpectedEngine` yazar. Unsupported port endpoint'lerinde bu değer `null` olur (`DiscoveryScopeValidator.cs:90-103`, `309-316`).

`TcpDiscoveryProbe` protocol deneme sırasını şöyle kurar:

1. `ExpectedEngine` varsa önce o engine;
2. yoksa 3306/5432/1433/1521 için default-port engine'i;
3. ardından henüz denenmeyen MySQL, PostgreSQL, MSSQL ve Oracle protokolleri.

Her protocol denemesi yeni bir `TcpClient` bağlantısı açar. Bir protocol başarısız olduğunda aynı bağlantı başka protocol için kullanılmaz (`TcpDiscoveryProbe.cs:39-113`, `209-224`). `ExpectedEngine` engine sonucunu belirlemez; yalnız probe sırasını optimize eder.

Detector'lar MySQL `10`, PostgreSQL `20`, MSSQL `30`, Oracle `40`, MongoDB `50`, Redis `60`, Unknown `int.MaxValue` sırasındadır. Doğrulanmış `ProtocolEvidence` hangi engine'e aitse yalnız o detector engine üretir. Evidence yoksa expected/default port detector'ı engine `null` olan `PortHint` üretir (`DiscoveryAgent.cs:57`, `80-88`; engine detector'ları `11-33`).

### 6.2 MySQL

MySQL probe temiz bağlantıda payload göndermez; server-first handshake'i pasif okur. Validator packet sequence byte'ının `0`, protocol byte'ının `10`, little-endian declared payload length'in en az `20` ve server-version alanının null-terminated olmasını ister; ayrıca en az ilk 32 byte ile paket uzunluğu tutarlılığını kontrol eder (`TcpDiscoveryProbe.cs:81-85`, `134-142`, `226-239`). Evidence `mysql-handshake-v10` ise detector `MySQL`, engine `MySql`, `95 / Confirmed` üretir. Evidence yoksa ilgili port yalnız `30 / PortHint`, engine `null` olur (`MySqlDiscoveryDetector.cs:11-33`). Server version persist edilmez.

### 6.3 PostgreSQL

PostgreSQL probe temiz bağlantıda sekiz byte SSLRequest (`length=8`, request code `80877103 / 0x04D2162F`) gönderir. Server tek byte `S` (SSL destekliyor) veya `N` (SSL desteklemiyor) döndürürse `postgresql-ssl-response` evidence oluşur (`TcpDiscoveryProbe.cs:21-22`, `86-92`, `237-238`). Evidence varsa `PostgreSQL`, engine `PostgreSql`, `98 / Confirmed`; yoksa `30 / PortHint`, engine `null` olur (`PostgreSqlDiscoveryDetector.cs:11-33`). Startup/login paketi gönderilmez ve server version çıkarılmaz.

### 6.4 MSSQL

MSSQL sırası geldiğinde temiz bağlantıda programatik olarak oluşturulan TDS pre-login paketi gönderilir. Builder `VERSION`, `ENCRYPTION`, `INSTOPT`, `THREADID` ve `MARS` option directory'sinin offset/length alanlarını ve TDS header toplam uzunluğunu gerçek payload'dan hesaplar (`TdsPreLoginProtocol.cs`). Response okuyucu önce sekiz-byte TDS header'ı, sonra header'da ilan edilen uzunluğun tamamını `MaxBannerBytes` sınırı içinde toplar. Timeout, reset veya yarım paket durumunda yeni ve temiz TCP bağlantısıyla yalnız bir kez retry edilir; tam fakat geçersiz cevap retry edilmez (`TcpDiscoveryProbe.cs`). Response şu yapısal koşulları sağlarsa `mssql-tds-prelogin` evidence üretilir:

- packet type `0x04` ve end-of-message status biti geçerlidir;
- header'daki big-endian packet length gerçek response uzunluğuna eşittir;
- ilk PRELOGIN option `VERSION` ve uzunluğu altı byte'tır;
- zorunlu `ENCRYPTION` option'ı bir byte ve değeri geçerli TDS enum aralığındadır;
- option directory `0xFF` terminator ile biter;
- bilinen token'ların uzunluk/değerleri, tüm token offset'leri, paket sınırları ve data-range çakışmaları doğrulanır;
- gelecekteki bilinmeyen token'lar paket yapısı geçerliyse güvenli biçimde atlanır.

Evidence varsa `Microsoft SQL Server`, engine `MsSql`, `98 / Confirmed`; hiçbir database probe'u doğrulanmazsa ilgili port `30 / PortHint`, engine `null` olur. Login paketi, username veya password gönderilmez.

Kaynaklar: `TdsPreLoginProtocol.cs`; `TcpDiscoveryProbe.cs`; `MsSqlDiscoveryDetector.cs:11-33`.

### 6.5 Oracle

Oracle sırası geldiğinde temiz bağlantıda TNS CONNECT paketi içinde ASCII `(CONNECT_DATA=(COMMAND=version))` gönderilir (`TcpDiscoveryProbe.cs:100-106`, `250-267`). Response şu minimal koşulları sağlarsa `oracle-tns-listener` evidence üretilir:

- response uzunluğu en az 8;
- TNS packet type byte'ı `2`, `4`, `5`, `6` veya `11`;
- packet header'daki big-endian uzunluk en az 8.

Evidence varsa `Oracle Database Listener`, engine `Oracle`, `98 / Confirmed`; hiçbir database probe'u doğrulanmazsa ilgili port `30 / PortHint`, engine `null` olur. Database login veya SQL çalıştırılmaz.

Kaynaklar: `TcpDiscoveryProbe.cs:100-106`, `144-166`, `245-267`; `OracleDiscoveryDetector.cs:11-33`.

### 6.6 Redis, MongoDB ve unknown

- Port `6379` açık ve supported database evidence yoksa `Redis`, engine `null`, `30 / PortHint`.
- Port `27017` açık ve supported database evidence yoksa `MongoDB`, engine `null`, `30 / PortHint`.
- Önceki detector'lardan hiçbiri eşleşmezse açık port `Unknown TCP service`, engine `null`, `25 / Unknown`.

Redis'e `PING`, MongoDB'ye hello/isMaster veya başka protocol probe gönderilmez. İsimler yalnız porttan gelir (`RedisDiscoveryDetector.cs:10-13`, `MongoDbDiscoveryDetector.cs:10-13`, `UnknownTcpDiscoveryDetector.cs:10-13`). Ayrıca sistem tüm 1-65535 portları taramadığı için “unknown” yalnız endpoint planına config/allowlist yoluyla girmiş bir port açık olduğunda oluşabilir.

## 7. Engine bazında detection tablosu

| Engine/servis | Default | Mevcut ek port | Candidate koşulu | Protocol/banner kontrolü | Başarı | Kanıt yoksa |
| --- | ---: | ---: | --- | --- | --- | --- |
| MySQL | 3306 | 13306 | `ExpectedEngine`/default port yalnız ilk probe sırası | Temiz bağlantıda server-first protocol-v10 handshake | `95 Confirmed`, supported | `30 PortHint`, engine null; unsupported |
| PostgreSQL | 5432 | 15432 | `ExpectedEngine`/default port yalnız ilk probe sırası | Temiz bağlantıda SSLRequest; tek-byte `S`/`N` | `98 Confirmed`, supported | `30 PortHint`, engine null; unsupported |
| MSSQL | 1433 | 11433 | `ExpectedEngine`/default port yalnız ilk probe sırası | Temiz bağlantıda programatik TDS pre-login + tam paket/token parser | `98 Confirmed`, supported | `30 PortHint`, engine null; unsupported |
| Oracle | 1521 | 11521 | `ExpectedEngine`/default port yalnız ilk probe sırası | Temiz bağlantıda TNS version CONNECT + header/type | `98 Confirmed`, supported | `30 PortHint`, engine null; unsupported |
| Redis | 6379 | — | port 6379 display hint | Redis protocol probe yok | — | `30 PortHint`, engine null; unsupported |
| MongoDB | 27017 | — | port 27017 display hint | MongoDB protocol probe yok | — | `30 PortHint`, engine null; unsupported |
| Unknown TCP | config/allowlist'e bağlı | — | Önceki detector eşleşmedi | Yok | — | `25 Unknown`, engine null; unsupported |

## 8. Port ve protocol sonucu çelişirse

### 8.1 Beklenen/default port açık, beklenen protokol doğrulanmadı

Probe beklenen engine'de durmaz; kalan üç database protokolünü yeni TCP bağlantılarıyla dener. Başka engine doğrulanırsa gerçek engine `Confirmed` olur. Hiçbiri doğrulanmazsa sonuç `30 / PortHint`, engine `null`, unsupported olur ve target olarak eklenemez. Bu kural dört supported engine için aynıdır.

### 8.3 Başka/custom porttaki gerçek database

Arbitrary open portlara protocol auto-detection yapılmaz. Database ancak port endpoint planına ilgili engine ile atanmışsa probe edilir:

- Mevcut config sayesinde MySQL `13306`, PostgreSQL `15432`, MSSQL `11433`, Oracle `11521` desteklenir.
- Aspire demo ortamındaki `demo-mysql-on-postgres-port-target`, MySQL 8.4'ü container içinde `5432` portunda çalıştırır (host endpoint `15433`). `5432` PostgreSQL port planında zaten bulunduğu için ayrı config gerekmez; bu hedef cross-port fingerprint davranışını gerçek servisle test eder.
- Yeni custom port `Discovery:LocalNetworkPorts:<engine>` config'ine eklenirse Local/Advanced'de; allowlist `AllowedPorts` içine eklenirse legacy authorized modda planlanabilir.
- UI kullanıcıya port girme alanı sunmaz.
- PostgreSQL'in örneğin `6000` portunda çalışması mevcut config ile bulunamaz. Port herhangi bir tarama listesine eklendiğinde bağımsız SSLRequest probe'u PostgreSQL'i doğrulayabilir.
- Custom port hangi engine adı altında planlanmış olursa olsun açık portta dört database protokolü denenir; yalnız evidence üreten engine supported olur.

### 8.4 Gerçek protocol farklı, planlanan engine farklı

Probe artık beklenen engine ile sınırlı değildir. Örneğin `ExpectedEngine=PostgreSql` olan 5432 endpoint'inde PostgreSQL SSLRequest başarısız olup MySQL handshake geçerse sonuç `MySQL / Confirmed` olur. Ters yönde, `ExpectedEngine=MySql` endpoint'inde MySQL greeting gelmez fakat PostgreSQL SSLRequest `S/N` dönerse sonuç `PostgreSQL / Confirmed` olur. Cross-engine port config çakışmaları gereksiz duplicate endpoint/probe üretebilir, fakat evidence olmadan yanlış supported engine üretmez.

## 9. Confidence ve fingerprint level

Confidence bir ağırlık toplama algoritması değildir. Her detector sonucu literal sabit değerle oluşturur; genel bir `+puan`, clamp veya normalization yoktur.

| Confidence | Level | Üreten durum |
| ---: | --- | --- |
| 98 | Confirmed | PostgreSQL SSLRequest, MSSQL TDS pre-login veya Oracle TNS evidence |
| 95 | Confirmed | MySQL protocol-v10 handshake evidence |
| 30 | PortHint | Beklenen/default DB portu açık fakat hiçbir supported protocol doğrulanmadı; ayrıca Redis/MongoDB port hint'i |
| 25 | Unknown | Diğer planlanmış açık TCP servis |

Mevcut detector'ların üretebildiği maksimum confidence **98**'dir. `DiscoveryFingerprintLevel` enum'unda legacy `PortOnly` ve `Probable` vardır, ancak mevcut detector'lardan hiçbiri bu level'ları üretmez (`DiscoveryFingerprintLevel.cs:3-10`).

Sorudaki değerlerin kesin anlamı:

- `30% · PortHint`: port açıktır ve port/config bir servis ihtimaline işaret eder; hiçbir supported database protocol evidence üretmemiştir.
- `95% · Confirmed`: MySQL protocol-v10 handshake validator'ı geçmiştir.
- `98% · Confirmed`: PostgreSQL SSLRequest, MSSQL TDS veya Oracle TNS validator'ı geçmiştir.

UI bunları doğrudan `result.confidence` ve `result.fingerprintLevel` ile gösterir (`discovery-panel.tsx:1039-1041`).

## 10. Supported / Unsupported kararı

Classifier'ın whitelist'i yalnız `MySql`, `PostgreSql`, `MsSql`, `Oracle`'dır. Fingerprint engine'i:

1. null değilse,
2. whitelist'teyse,
3. job'ın selected engine filter'ındaysa,
4. fingerprint level `Confirmed` ise

sonuç `Supported` olur. `PortHint`, `PortOnly`, `Probable` ve `Unknown` hiçbir durumda supported değildir (`DiscoveryResultClassifier.cs:9-42`).

Diğer sonuçlar `Unsupported` olur:

- Fingerprint Confirmed değilse reason: `... was not confirmed by a database protocol fingerprint.`
- Confirmed engine supported whitelist'te olduğu hâlde filter dışında kaldıysa reason: `... was excluded by the supported engine filter.`
- Diğer engine null/whitelist dışı durumlarda reason: `... is not supported yet.`

Kaynak: `DiscoveryResultClassifier.cs:29-42`.

Backend DTO iki liste üretir:

- `supportedDatabases`: status `supported`, `alreadyDiscovered` veya `addedAsTarget`;
- `unsupportedDatabases`: status `unsupported`.

Kaynak: `DiscoveryService.cs:517-542`.

Unsupported UI alanları:

| UI alanı | Kaynak |
| --- | --- |
| HOST | `DiscoveryResultDto.host` ← `DiscoveredDatabase.Host` |
| PORT | `DiscoveryResultDto.port` ← `DiscoveredDatabase.Port` |
| SERVICE | `DiscoveryResultDto.detectedService` ← detector `Service` |
| SOURCE | Backend result property değil; frontend `job.scope` metninden türetir |
| REASON | `DiscoveryResultDto.reason` ← classifier/repository reason |

`SOURCE` mapping'i: scope `Local network:` ile başlarsa `Yerel Ağ (LocalNetwork)`; `All authorized endpoints` ise `Eski Otomatik Kapsam`; aksi hâlde `Belirli Hedef (Advanced)` (`discovery-panel.tsx:1243-1255`).

## 11. “Target Olarak Ekle” kriteri ve duplicate davranışı

### 11.1 UI ve API kriterleri

UI butonu `canAddAsTarget && !isAlreadyTarget` olduğunda etkinleştirir (`discovery-panel.tsx:455-461`, `1031-1071`). Backend `canAddAsTarget` değerini şu şekilde üretir:

- `DetectedEngine != null`;
- `FingerprintLevel == Confirmed`;
- result status `Unsupported` değil;
- aynı endpoint zaten target değil.

Kaynak: `DiscoveryService.cs:545-582`.

Backend add akışında confidence yüzdesi eşiği yoktur, fakat explicit `FingerprintLevel == Confirmed` şartı vardır. `PortHint` ve legacy `PortOnly/Probable` sonuçlar engine taşısa bile reddedilir. Tekli istekte exception; bulk istekte o item `failed` döner (`DiscoveryService.cs:198-247`, `495-498`).

Target yaratılmadan hemen önce discovery endpoint'i internal scan allowlist'e `(environment, host, engine, port)` ile create/merge edilir; sonra normal `TargetService.CreateAsync` çağrılır (`DiscoveryService.cs:263-287`, `334-360`). `TargetService` host non-empty, port `1..65535` ve approved scan scope kontrollerini ayrıca yapar (`TargetService.cs:24-70`). Discovery bu çağrıya credential geçmez; target credential'sız oluşturulur.

### 11.2 Already target

Mevcut target karşılaştırması:

```text
NormalizeHost(target.Host) == NormalizeHost(result.Host)
AND target.Port == result.Port
AND target.Engine == result.DetectedEngine
```

Engine karşılaştırmaya dahildir; environment dahil değildir (`DiscoveryService.cs:497-509`). Host normalizasyonu IP'yi canonical string'e, hostname'i trim/trailing-dot removal + lowercase biçimine çevirir (`TargetEndpointNormalizer.cs:5-13`). DB'de de target için `(NormalizedHost, Port, Engine)` unique index vardır (`DbScannerDbContext.cs:35-49`).

Eşleşme varsa DTO `isAlreadyTarget=true`, `existingTargetId`, `targetState=AlreadyAdded` ve status `addedAsTarget` döndürür. UI checkbox/add butonunu kapatıp “Targetı Gör” linki gösterir (`DiscoveryService.cs:550-582`, `discovery-panel.tsx:1034-1072`).

### 11.3 Already Discovered

Yeni job sonucu kaydedilirken repository daha eski bir discovery sonucunu şu anahtarla arar:

```text
NormalizedHost + Port + DetectedEngine + EnvironmentTag
```

Bulursa yeni row eklemek yerine eski row'un `DiscoveryJobId` değerini yeni job'a taşır, fingerprint/service alanlarını günceller ve:

- `TargetId` varsa `AddedAsTarget`;
- yeni sonuç unsupported/engine null ise `Unsupported`;
- aksi hâlde `AlreadyDiscovered`

yapar (`DiscoveryRepository.cs:53-96`). Bu nedenle “Already Discovered” target duplicate'i değil, önceki job'da keşfedilmiş aynı engine endpoint'inin yenilenmesidir. DB unique index'i de aynı dört alan üzerindedir (`DbScannerDbContext.cs:244-266`).

## 12. Discovery Job veri modeli ve lifecycle

### 12.1 Entity

`DiscoveryJob` alanları (`DiscoveryJob.cs:5-18`):

| Alan | Anlamı |
| --- | --- |
| `Id` | Job GUID |
| `Name` | Kullanıcı adı, max 160 |
| `ScopeInput` | Raw/marker scope; max 4096 |
| `EnvironmentTag` | `demo`, `test`, `staging`, `prod` |
| `SupportedEngineFilter` | Virgülle serialize engine adları |
| `Status` | Pending/Running/Completed/Failed |
| `ErrorMessage` | Nullable, max 512 |
| `CreatedAt` | Oluşturma zamanı |
| `StartedAt` | Worker Running zamanı |
| `FinishedAt` | Completed/Failed zamanı |
| `Results` | `DiscoveredDatabase` collection |

EF mapping: `DbScannerDbContext.cs:229-242`.

### 12.2 Lifecycle

```text
Pending
  └─ TryMarkRunningAsync başarılı → Running
       ├─ CompleteAsync → Completed
       └─ exception/job timeout → Failed
```

`Cancelled` status veya cancel endpoint'i yoktur (`DiscoveryStatus.cs:3-9`). Pending/Running job silinemez (`DiscoveryService.cs:45-60`). RabbitMQ duplicate/redelivery durumunda `TryMarkRunningAsync` yalnız Pending'i geçirir; diğer status'taki job agent tarafından yok sayılır (`DiscoveryAgent.cs:21-27`).

## 13. Discovery Result veri modeli ve UI mapping

### 13.1 Entity

`DiscoveredDatabase` alanları (`DiscoveredDatabase.cs:5-21`):

| Alan | Anlamı |
| --- | --- |
| `Id` | Result GUID |
| `DiscoveryJobId` / `DiscoveryJob` | Parent ilişki |
| `Host` | UI'da gösterilen hostname/IP |
| `NormalizedHost` | Duplicate identity |
| `EnvironmentTag` | Persist edilen environment |
| `Port` | Açık port |
| `DetectedService` | Detector display service adı |
| `DetectedEngine` | Nullable supported engine enum |
| `Confidence` | Detector literal skoru |
| `FingerprintLevel` | Unknown/PortHint/PortOnly/Probable/Confirmed |
| `Status` | Supported/Unsupported/AddedAsTarget/AlreadyDiscovered |
| `Reason` | Classification/refresh açıklaması |
| `TargetId` | Eklenen target'a nullable FK |
| `CreatedAt` | Result zamanı |

Raw banner veya `ProtocolEvidence` persist edilmez; bunlar yalnız in-memory `DiscoveryProbeResult` içindedir (`DiscoveryProbeResult.cs:5-11`). EF mapping ve cascade/set-null ilişkileri `DbScannerDbContext.cs:244-270` arasındadır.

### 13.2 DTO ve UI

API result DTO'su: `DiscoveryResultDto.cs:3-18`. UI TypeScript karşılığı: `discovery.schema.ts:17-33`.

| UI sütunu | DTO/property | Not |
| --- | --- | --- |
| Host | `host` | Entity `Host` |
| Port | `port` | Entity `Port` |
| Engine/Servis | `detectedService` | Display adı; canonical engine ayrı `detectedEngine` |
| GÜVEN | `confidence` + `fingerprintLevel` | Örn. `30% · PortHint`, `98% · Confirmed` |
| Environment | `environment` | Map sırasında parent job environment'ı |
| KAYNAK | frontend `getDiscoverySource(job)` | Result entity/DTO alanı değildir |
| DURUM | `status`, `isAlreadyTarget`, `targetState` | UI badge/aksiyon durumu |
| NEDEN | `reason` | Unsupported tablosu |

## 14. Classification decision tree

```text
Planlı endpoint oluşturuldu mu?
  ├─ Hayır → Job oluşturulmaz veya hedef validation'da atlanır
  └─ Evet (Host, Port, ExpectedEngine?)
      ↓
İlk protocol sırasındaki TCP connect başarılı mı? (1 s)
  ├─ Hayır → Port kapalı/erişilemez; sonuç yok
  └─ Evet → Port açık
      ↓
Probe sırası = ExpectedEngine/default-port engine + kalan engine'ler
      ↓
Her probe için YENİ TCP bağlantısı
  ├─ MySQL → server-first protocol-v10 handshake oku
  ├─ PostgreSQL → SSLRequest gönder; tek-byte S/N doğrula
  ├─ MSSQL → TDS pre-login gönder; tam paket ve option directory doğrula
  └─ Oracle → TNS version CONNECT gönder; response type/header doğrula
      ↓
Herhangi bir protocol evidence var mı?
  ├─ Evet → Evidence'a ait engine + 95/98 Confirmed
  └─ Hayır
      ├─ expected/default/6379/27017 port hint'i → engine null, 30 PortHint
      └─ diğer planlı açık port → engine null, 25 Unknown
      ↓
Engine non-null VE Confirmed VE selected filter'da mı?
  ├─ Evet → Supported ve target adayı
  └─ Hayır → Unsupported, target eklenemez
```

## 15. False-positive / false-negative analizi

### 15.1 False-positive riskleri

- Açık default port tek başına supported engine üretmez; önceki MySQL/PostgreSQL port-only false-positive yolu kapatılmıştır.
- PostgreSQL SSLRequest validator'ı tek-byte `S/N`; Oracle validator'ı minimal header/type kontrolüdür. MSSQL artık tam TDS packet length, PRELOGIN option directory, token shape/value ve range kontrolleri yapar. Yine de bunlar authentication veya cryptographic identity değildir; protokolü eksiksiz taklit eden bir servis teorik olarak false-positive üretebilir.
- MySQL validator'ı packet sequence, declared length ve protocol-v10 byte'ını kontrol eder; full capability/auth-plugin paketi parse edilmez.
- Redis/MongoDB adı yalnız porttan gelir; `6379`/`27017` üzerindeki başka servis yanlış adlandırılır.
- Aynı host/port birden fazla engine için config edilmişse duplicate probe planı oluşabilir; engine sonucunu yine protocol evidence belirler.

### 15.2 False-negative veya düşük-confidence riskleri

- Config/allowlist'te olmayan custom port hiç probe edilmez. Örneğin PostgreSQL `6000` mevcut config ile görünmez.
- PostgreSQL SSLRequest cevabı proxy/firewall tarafından kesilirse gerçek servis `PortHint/Unknown` kalır ve target olarak eklenemez.
- Firewall/proxy yalnız TCP connect'e izin verip protocol cevabını engellerse hiçbir engine Confirmed olmaz.
- MySQL handshake read timeout'u aşarsa engine atanmaz; sonuç `PortHint` veya `Unknown` kalır.
- MSSQL cevabı timeout/reset/yarım paket olursa yeni bağlantıyla bir kez retry edilir; ikinci deneme de tamamlanmazsa evidence oluşmaz. Oracle cevabı ilk 8 byte'a ulaşmadan kapanır/timeout olursa evidence oluşmaz.
- Job'ın 600 s global timeout'u büyük/yavaş scope'u `Failed` yapabilir.
- Hostname public veya mixed private+public IPv4 çözerse tamamı reddedilir; private adresi ayrıca taranmaz.

### 15.3 Host/port davranışının sonucu

Ayrı host availability cache'i olmadığı için unreachable host üzerindeki her port timeout'a kadar denenir. Buna karşılık tek portun kapalı olması aynı hosttaki diğer portları etkilemez.

## 16. Güvenlik değerlendirmesi

Discovery request, command, RabbitMQ message, endpoint, probe result ve persisted result modellerinde username/password/credential alanı yoktur. Discovery sırasında:

- authentication denenmez;
- username/password gönderilmez;
- brute force veya exploit çalıştırılmaz;
- SQL/query veya mutating operation çalıştırılmaz;
- MySQL için yalnız TCP connect + pasif server-first handshake read yapılır;
- PostgreSQL için credentialless SSLRequest gönderilir; startup/login yapılmaz;
- MSSQL için credentialless TDS pre-login paketi gönderilir;
- Oracle için credentialless TNS listener version command paketi gönderilir.

Kaynaklar: `CreateDiscoveryRequest.cs:3-8`, `CreateDiscoveryCommand.cs:5-10`, `DiscoveryRequestedMessage.cs:3-5`, `DiscoveryEndpoint.cs:5-9`, `TcpDiscoveryProbe.cs:39-166`, `250-267`.

Repository'deki `MsSqlAuthenticatedProbe`, `OracleAuthenticatedProbe`, `MySqlEmptyPasswordProbe` ve normal Nmap scan hattı **Database Discovery tarafından çağrılmaz**. Bunlar target oluşturulduktan sonraki normal scan özellikleridir. Discovery DI zinciri doğrudan `IDiscoveryProbe → TcpDiscoveryProbe` olarak kayıtlıdır (`DependencyInjection.cs:123-136`).

## 17. Kritik kod referansları

| Katman | Dosya | Kritik üyeler/satırlar |
| --- | --- | --- |
| Frontend | `src/DbScanner.Web/src/components/targets/discovery-panel.tsx` | `submitDiscovery` 248-330; polling 112-119; result UI 968-1103; source mapping 1243-1255 |
| Frontend API | `src/DbScanner.Web/src/lib/api/discoveries.ts` | create/list/validate/add 27-70 |
| Frontend model | `src/DbScanner.Web/src/schemas/discovery.schema.ts` | result/job types 17-70 |
| API | `src/DbScanner.Api/Program.cs` | discovery endpoints 537-736; engine parse 834-854 |
| Application | `src/DbScanner.Application/Services/DiscoveryService.cs` | create/plan 78-195; add target 198-360; queue 424-443; DTO mapping 517-583 |
| Worker orchestration | `src/DbScanner.Infrastructure/Discovery/DiscoveryAgent.cs` | `ExecuteAsync` 21-128 |
| Scope | `src/DbScanner.Infrastructure/Discovery/DiscoveryScopeValidator.cs` | local 57-109; advanced 134-316; allowlist planning 451-519 |
| CIDR | `src/DbScanner.Infrastructure/Discovery/NetworkScope.cs` | parse/expand/private checks 65-169 |
| TCP probe | `src/DbScanner.Infrastructure/Discovery/TcpDiscoveryProbe.cs` | connect/probe 31-75; read 101-140; validators 142-169 |
| Fingerprint | `src/DbScanner.Infrastructure/Discovery/*DiscoveryDetector.cs` | engine-specific `Detect` metotları |
| Classification | `src/DbScanner.Infrastructure/Discovery/DiscoveryResultClassifier.cs` | supported/unsupported 9-39 |
| Messaging | `src/DbScanner.Infrastructure/Messaging/RabbitMqDiscoveryJobPublisher.cs`; `RabbitMqDiscoveryJobConsumer.cs` | publish 17-38; consume 20-64 |
| Entity | `src/DbScanner.Domain/Entities/DiscoveryJob.cs`; `DiscoveredDatabase.cs` | tüm model alanları |
| Persistence | `src/DbScanner.Infrastructure/Repositories/DiscoveryRepository.cs` | lifecycle/dedup 40-110 |
| EF mapping | `src/DbScanner.Infrastructure/Persistence/DbScannerDbContext.cs` | discovery tables/indexes 229-270 |

## 18. Kritik Soruların Cevapları

1. **Açık port bulunduğunda database türü neye göre belirleniyor?**
   Port yalnız endpoint'i ve ilk probe sırasını belirler. Engine, ayrı temiz bağlantılarda alınan MySQL handshake, PostgreSQL SSLResponse, MSSQL TDS veya Oracle TNS evidence'ına göre belirlenir (`TcpDiscoveryProbe.cs:39-113`).

2. **Sadece porta bakarak mı karar veriliyor?**
   Hayır. Port evidence olmadan supported engine üretmez. Redis/MongoDB yalnız bilgi amaçlı `PortHint` olarak adlandırılır ve target olamaz.

3. **Gerçek protocol handshake yapılıyor mu?**
   Evet, credentialless seviyede dört engine için yapılır: MySQL server-first handshake, PostgreSQL SSLRequest, MSSQL TDS pre-login ve Oracle TNS version exchange. Her biri ayrı bağlantı kullanır (`TcpDiscoveryProbe.cs:39-166`).

4. **MySQL nasıl doğrulanıyor?**
   Server-first pakette sequence `0`, protocol version `10` ve geçerli declared payload length aranır; geçerse `95 Confirmed`, geçmezse engine atanmaz (`TcpDiscoveryProbe.cs:226-235`, `MySqlDiscoveryDetector.cs:11-33`).

5. **PostgreSQL nasıl doğrulanıyor?**
   Sekiz-byte SSLRequest gönderilir; tek-byte `S` veya `N` cevabı `98 Confirmed` kanıtıdır. Cevap yoksa engine atanmaz (`TcpDiscoveryProbe.cs:21-22`, `86-92`, `237-238`).

6. **MSSQL nasıl doğrulanıyor?**
   Offset ve toplam uzunluğu builder tarafından hesaplanan credentialless TDS pre-login gönderilir. Response'un tam TDS uzunluğu, `0x04` packet type'ı, PRELOGIN token directory'si, zorunlu `VERSION`/`ENCRYPTION` alanları ve tüm token sınırları doğrulanırsa `98 Confirmed` olur. Timeout/reset/yarım pakette temiz bağlantıyla bir retry yapılır (`TdsPreLoginProtocol.cs`; `TcpDiscoveryProbe.cs`).

7. **Oracle nasıl doğrulanıyor?**
   TNS version CONNECT gönderilir; kabul edilen packet type ve header length kontrolü geçerse `98 Confirmed`, aksi hâlde diğer engine probe'larına geçilir (`TcpDiscoveryProbe.cs:100-106`, `245-267`).

8. **`PortHint` ne anlama geliyor?**
   TCP endpoint açıktır ve port/config bir servis ihtimaline işaret eder, fakat hiçbir supported database protocol evidence üretmemiştir. Engine null'dır, sonuç unsupported'dır ve target olamaz. `PortOnly` yalnız eski persisted sonuçlarla uyumluluk için enum'da durur.

9. **`Confirmed` ne anlama geliyor?**
   Detector'ın sınırlı engine-specific signature/response koşulu geçmiştir. Full authentication veya kesin cryptographic identity anlamına gelmez.

10. **Confidence yüzdesi nasıl hesaplanıyor?**
    Hesaplanmıyor/toplanmıyor; detector'larda literal `98`, `95`, `30` veya `25` atanıyor.

11. **Custom port üzerindeki database tespit edilebilir mi?**
    Yalnız port config/allowlist planına ekliyse. Planlandıktan sonra hangi engine listesi üzerinden geldiğine bakılmadan dört protocol probe edilir. Mevcut ek portlar 13306/15432/11433/11521'dir; UI'dan port girilemez.

12. **Bilinmeyen açık portlara ne oluyor?**
    Tüm portlar taranmaz. Planlanmış fakat bilinen detector'a uymayan açık endpoint `Unknown TCP service`, `25 / Unknown`, unsupported olur (`UnknownTcpDiscoveryDetector.cs:10-13`).

13. **Supported / unsupported kararı nasıl veriliyor?**
    Engine non-null, dört-engine whitelist'inde, job filtresinde ve fingerprint `Confirmed` ise supported; diğerleri unsupported (`DiscoveryResultClassifier.cs:9-42`).

14. **Bir discovery sonucunun target olarak eklenme kriteri nedir?**
    Engine null olmamalı, fingerprint `Confirmed` olmalı, result unsupported olmamalı ve aynı host/port/engine target bulunmamalı. Ardından internal allowlist ve `TargetService` validasyonu geçmelidir (`DiscoveryService.cs:218-360`, `495-498`, `545-584`).

15. **Aynı database daha önce target ise nasıl anlaşılıyor?**
    Normalized host + port + engine karşılaştırılır; environment karşılaştırılmaz (`DiscoveryService.cs:497-509`).

16. **Discovery sırasında herhangi bir credential kullanılıyor mu?**
    Hayır. Credential/login/query yoktur. MySQL pasif handshake; PostgreSQL SSLRequest; MSSQL TDS pre-login ve Oracle TNS version probe'ları credentialless çalışır.

## 19. Özet

Discovery sistemi özetle:

1. UI'dan seçilen local private network veya advanced private hedefleri backend'de doğrular.
2. Seçilen engine'lerin configured+standard TCP portlarını; Local/Advanced'de ayrıca 6379 ve 27017'yi planlar.
3. Job'ı `Pending` kaydedip RabbitMQ üzerinden Worker'a yollar.
4. Worker scope'u yeniden doğrular; endpoint'leri 8-way parallel ve 20 probe/s rate limit ile TCP connect eder.
5. `ExpectedEngine`/default portu yalnız probe sırası için kullanır; her supported protocolü ayrı TCP bağlantısında doğrular.
6. Yalnız protocol evidence bulunan `Confirmed` engine'i supported yapar; evidence yoksa `PortHint/Unknown` unsupported kalır.
7. Sonuçları discovery job altında saklar, önceki discovery ve mevcut target kimliklerini normalize host/port/engine üzerinden ilişkilendirir.
8. UI sonuçları polling ile alır; supported ve henüz target olmayan sonuçlar normal target inventory'ye credential olmadan eklenebilir.
