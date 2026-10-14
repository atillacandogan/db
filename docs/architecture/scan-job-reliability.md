# Tarama işlerinin güvenilirliği — Transactional Outbox ve Execution Lease

Tarih: 2026-10-06. Sürüm: `stage-15-reliability`.

## İşleyiş

1. API hedef, kapsam ve credential doğrulamasını yapar. `ScanService` yeni
   `Pending` taramayı ve `OutboxScanJobPublisher` mesaj snapshot'ını aynı scoped
   DbContext'e ekler. Tek `SaveChanges` ikisini birlikte commit eder. API isteği
   sırasında RabbitMQ bağlantısı açılmaz; broker kesintisi tarama isteğini kaybettirmez.
2. Worker'ın `ScanOutboxDispatcher` servisi gönderime hazır bir satırı
   `FOR UPDATE SKIP LOCKED` ile alır. Dispatch token/süre birden fazla dispatcher'ın
   aynı satırı aynı anda sahiplenmesini engeller.
3. Confirm-enabled RabbitMQ channel, persistent mesajı `mandatory=true` ile
   gönderir. `BasicPublishAsync` broker confirm'ini bekler. Başarıdan sonra
   `PublishedAt` kaydedilir. Hata/timeout varsa kayıt korunur ve tekrar denenir.
4. Consumer `Pending → Scanning` geçişini atomik yapar ve tekil lease token alır.
   Kuyruktaki mesaj yerine kaydedilmiş orijinal kontrol/credential snapshot'ını
   kullanır. Başka worker'a ait veya tamamlanmış tarama tekrar çalıştırılmaz.
5. Heartbeat bağımsız scoped DbContext ile lease'i yeniler. Sahiplik/bağlantı
   kaybolursa çalışmanın cancellation token'ı iptal edilir. Kullanıcı iptali de
   lease'i kaldırır; en geç bir heartbeat turunda çalışan kontrol iptal sinyali alır.
6. Sonuç yazımı tarama satırındaki token ve durumla **fence** edilir. Eski deneme,
   yeni worker'ın veya iptal edilen taramanın sonucunu ezemez. Durum, risk puanı,
   bulgu ve kontrol sonuçları tek transaction içinde commit edilir; başarısız
   evidence insert'i önceki durumu da geri alır. Commit sonrası mesaj ACK edilir.
7. Recovery servisi süresi dolan `Scanning` işlerini kilitleyip `Pending` yapar
   ve orijinal outbox satırını yeniden gönderime açar. Son çalışma denemesi de
   kesilmişse tarama `Failed` olur ve snapshot outbox üzerinden dead-letter
   kuyruğuna gönderilir. Eski worker'ın retry/heartbeat işlemleri token eşleşmeden
   durumu değiştiremez.

Outbox'ın ilk `RequestedAt`, `CreatedAt` ve `AvailableAt` zamanları da lease,
dispatch ve retry kararları gibi PostgreSQL `clock_timestamp()` üzerinden alınır.
Host ve veritabanı saatleri farklı olduğunda yeni mesaj yanlışlıkla geleceğe
ertelenmez. Bu salt okunur saat sorgusu kayıtları commit etmez; tarama ve outbox
hâlâ tek `SaveChanges` ile birlikte kaydedilir. Broker confirm'i
ile veritabanı commit'i tek bir dağıtık transaction değildir: confirm'den hemen
sonra dispatcher ölürse aynı mesaj tekrar gönderilebilir. Bu nedenle garanti
**at-least-once delivery + fenced/idempotent sonuç yazımıdır**, tüm ağ kontrolleri
için fiziksel exactly-once çalışma iddiası değildir. Bir kesinti sırasında bir
kontrol yeniden yapılabilir; kontrollerin güvenli/salt-okunur olması önemlidir.

## Ayarlar

`ScanReliability` bölümü Worker appsettings veya ortam değişkenleriyle ayarlanır.
Örneğin `ScanReliability__LeaseSeconds`.

| Ayar | Varsayılan | Anlam |
| --- | --- | --- |
| `HeartbeatSeconds` | 10 | Canlı worker'ın lease yenileme aralığı |
| `LeaseSeconds` | 60 | Yenilenmezse sahipliğin sona erme süresi |
| `RecoveryIntervalSeconds` | 15 | Süresi dolan iş denetimi |
| `MaxExecutionAttempts` | 3 | İlk çalışma dahil en fazla deneme |
| `RetryDelaySeconds` | 5 | Yeniden çalışma / publish hata beklemesi |
| `OutboxPollSeconds` | 1 | Boş outbox'ın kontrol aralığı |
| `DispatchLeaseSeconds` | 30 | Dispatcher sahipliğinin süresi |
| `PublishTimeoutSeconds` | 10 | Broker confirm'i için azami bekleme |

Lease heartbeat'in en az üç katı olmalıdır. Dispatch lease publish timeout'undan
en az beş saniyeden fazla uzun olmalıdır. Geçersiz ayarlar startup'ta reddedilir.
Worker durduktan sonra kurtarma tipik olarak lease süresi + recovery aralığı +
retry/poll beklemesi içinde başlayabilir; bu bir SLO garantisi değildir.

API ve Worker aynı `CredentialProtection` anahtarını kullanmalıdır. Parola
outbox JSON'una plaintext olarak yazılmaz; mevcut AES-GCM envelope korunur.
Snapshot terminal taramada hemen silinmez, scan silinince FK cascade ile silinir.
Bu nedenle veri saklama politikası ve anahtar rotasyonu replay ihtiyacını hesaba
katmalıdır. Loglarda payload/parola veya broker connection string'i yazılmaz;
güvenilirlik hataları sınıf adı ve sabit hata kodlarıyla raporlanır.

## Uyumluluk ve kapsam

- Outbox dispatcher ve recovery yalnızca Worker'da başlar; API yalnızca kayıt
  oluşturur. Worker kapalıyken yeni taramalar kalıcı `Pending` olarak bekleyebilir.
- Başlangıçta broker kapalıysa consumer'lar beş saniye arayla bağlantıyı tekrar
  dener; geçici bağlantı hatası tüm Worker'ı kapatmaz.
- Önceden kuyruğa konmuş eski mesaj, ilk sahiplenmede snapshot olarak kaydedilir;
  `selectedNseScriptIds` dahil orijinal seçim korunur. Eski, lease'siz `Scanning`
  kaydı aynı orijinal mesaj yeniden teslim edilirse sahiplenilebilir.
- Eski sürümde hiç gönderilememiş ve snapshot'ı bulunmayan bir işi otomatik
  yeniden oluşturmak mümkün değildir; kontrol/credential tahmini yapılmaz.
- Mevcut kural değerlendirme başarısızlığı/scan timeout'u terminal sonuç olarak
  kalır. Her `Failed` tarama otomatik üç kez çalıştırılmaz; sınırlı tekrar burada
  kesilmiş/sahipliğini kaybetmiş işlerin kurtarılması içindir.
- Discovery ve NSE katalog senkronizasyonu için outbox/lease bu değişikliğe
  dahil değildir. Consumer başlangıç reconnect koruması onlar için de geçerlidir.
- Mesaj tekrar teslimine karşı idempotency, API isteğine `Idempotency-Key`
  desteğiyle aynı şey değildir; bu sürüm yeni bir HTTP idempotency sözleşmesi eklemez.

## Test kanıtı

14 güvenilirlik entegrasyon testi geçici PostgreSQL 17.6 ve RabbitMQ 4.2 container'ları
kullanır. Her test ayrı veritabanı ve kuyruk adlarıyla izole edilir.

- Tarama/outbox birlikte commit; şifreli credential round-trip.
- Outbox NOT NULL hatasında tarama kaydının da rollback olması.
- Kontrollü transport hata enjeksiyonunda kalıcı pending istek ve sonraki publish.
- Host saatinden beş dakika gerideki kontrollü DB saatinde ilk dispatch ve broker
  hatasından sonra retry. Eski publisher bu testte ilk dispatch'i yapamaz; yeni
  publisher DB saatini kullandığı için test geçer. Makine/container saati değiştirilmez.
- Gerçek gelecekteki `AvailableAt` kaydının erken gönderilmemesi.
- Sekiz eşzamanlı teslimden yalnızca birinin lease alması.
- Süresi dolan lease'i iki recovery instance'ından yalnızca birinin kurtarması.
- Snapshot değiştirmeye çalışan teslimde orijinal seçim ve credential'ın korunması.
- Eski token'ın renewal, retry ve sonuç yazımının reddedilmesi.
- Evidence FK hatasında tam rollback ve geçerli lease'in korunması.
- Kullanıcı iptalinde lease'in kaldırılması; sonuç/recovery/publish engellemesi.
- Gerçek heartbeat yenilemesi ve sahiplik kaybında cancellation.
- Üçüncü kesintide `Failed` + gerçek RabbitMQ dead-letter mesajı.
- Gerçek broker confirm'inden sonra dispatcher kesilmesi/çift teslimde tek completion.
- Consumer kapanması ve broker redelivery sonrasında orijinal planla kurtarma.
- Önceden queued eski alanlı mesajın snapshot olarak yakalanması.

Lease/dispatch süresinin geçirilmesi yalnızca test container'larında doğrudan
test SQL'iyle yapılır; canlı gözlem state'i/saatleri değiştirilmez.

İlk devreye alma (2026-10-06) test paketi: Application **125 başarılı**, Infrastructure **142 başarılı,
1 atlandı**, başarısız **0**. Atlanan test host Nmap eksikliğindeki gerçek SQL
Server/Nmap testidir. Worker image'ında Nmap vardır; önceki frontend kaynakları
değişmediği için bu backend değişikliğinde Web test/build'i yeniden çalıştırılmadı.
Mevcut bağımlılık güvenlik uyarıları ayrı işlerdir; bu değişiklikte giderilmedi.

### Saat kaynağı düzeltmesi — 2026-10-07

Tam regresyondaki üç aralıklı hata ilk outbox dispatch'inde birleşti: broker retry
testi `false` alırken worker kapanması ve dedicated tenant testleri ilk dispatch
sonucunu kontrol etmeyip boş kuyrukta timeout oluyordu. İlk kayıt host `UtcNow`,
claim ise PostgreSQL saati kullanıyordu. Kontrollü -5 dakika DB saat testi eski
kodda aynı ilk-dispatch hatasını üretti; publisher'ın DB saatine geçirilmesiyle
geçti. Önceki gerçek koşudaki saat farkının büyüklüğü ölçülmüş değildir.

Worker/tenant testleri artık ilk dispatch'i açıkça doğrular; timeout/assertion
olsa bile consumer'lar `finally` ile durdurulur. Süreler uzatılmadı, dispatch due
filtresi veya tenant erişim denetimleri kaldırılmadı. Clock enjeksiyonu yalnızca
izole test ortamındaki scalar SQL okumalarını etkiler.

Doğrulama: ilgili güvenilirlik/tenant testleri **24/24** başarılı. Release solution
build **0 hata / 0 uyarı**; Application/API **191 başarılı**; Python ops **37 başarılı**.
Infrastructure iki ardışık tam koşuda da **174 başarılı / 0 başarısız / 1 atlandı**
(53 ve 73 saniye). Toplam benzersiz regresyon sonucu **402 başarılı / 0 başarısız /
1 atlandı**. Atlanan mevcut test host Nmap gerektirir. Web ve bağımlılık audit'i
bu backend düzeltmesinde tekrar çalıştırılmadı.

Schema/migration, kuyruk sözleşmesi, uygulama secret'ları ve canlı veri değişmedi.
EF `has-pending-model-changes` kontrolü son migration'dan beri model değişikliği
olmadığını doğruladı; yeni migration gerekmiyor.
Çalışan API/Worker süreçlerinin yeni publisher'ı kullanması için yeni derlemeyle
yeniden başlatılması gerekir; bu doğrulama canlı dağıtım yapmadı.

Kaynak rehber: [RabbitMQ publisher confirms](https://www.rabbitmq.com/docs/confirms),
[RabbitMQ reliability](https://www.rabbitmq.com/docs/reliability).

## Yerel dağıtım

- Additive migration: `20261006112320_AddScanOutboxAndExecutionLeases`.
- Eski alan veya tablo silinmez; eski API/Worker aynı anda çalıştırılmamalıdır.
  Eski worker yeni outbox/lease sahipliğini koruyamaz.
- Mevcut yerel veritabanında dağıtım öncesi aktif tarama bulunmadı.
- Migration öncesi PostgreSQL custom-format backup alındı, `pg_restore --list`
  ile okunabilirliği kontrol edildi (tam restore provası yapılmadı).
- Yedek: `/private/tmp/dbscanner-reliability-backup.vmkIji/dbscanner-before-reliability.dump`;
  dizin `0700`, dosya `0600`. Geçici dizin OS temizliğine tabi olduğundan uzun
  süreli yedek saklama alanı değildir; repository'ye kopyalanmadı.
- Devreye alma kontrolü: 2026-10-06 11:52 UTC. API `/api/status` sürümü
  `stage-15-reliability`, durum `ready`; `/health` sonucu `Healthy`.
- Yeni Worker: `scanner-worker-ahafjzpa`, image
  `scanner-worker:cbd0fe659251c6fbb15329c026d0d8f2c0207752`.
  Scan/discovery/sync consumer'ları başladı, başlangıç loglarında hata yok.
- Prometheus `target_info` hem API `atillacan-MacBook-Air-13499` hem Worker
  `57ceb42bac15-1` için `stage-15-reliability` raporladı.
- Canlı migration history'de yeni migration doğrulandı; outbox tablosu
  erişilebilir (kontrol anında 0 kayıt). Worker rebuild edildi; eski Worker'ın
  kapanışı tamamlandı, karma sürüm consumer bırakılmadı.
- Web `/dashboard` HTTP `200`. Mevcut uygulama/Keycloak volume'ları ve secret'lar
  korundu. Canlı ortamda ek kullanıcı veya tarama oluşturulmadı; hata enjeksiyonu
  yalnızca izole test veritabanlarında yapıldı.
- .NET solution build başarılı: 0 hata, mevcut paketlerden 15 güvenlik uyarısı.

Rollback için yalnızca lease recovery'yi kapatmak veya EF `Down` çalıştırmak
güvenli değildir. Aktif işleri durdurup/drain ederek API ve Worker birlikte geri
alınmalı; dispatch edilmemiş outbox kayıtları korunmalıdır.
