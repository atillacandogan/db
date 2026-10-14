# Aşama 15 — POC Strategy geçişi ve eski/yeni karşılaştırma

Tarih: 2026-10-06
Durum: runtime ve dışa gönderilen sözleşmelerin temizliği uygulandı; yıkıcı şema temizliği yapılmadı.

## Karar ve kapsam

Kullanıcı, yedi günlük süreyi beklememeyi ve Aşama 15'te yeni yapıyı eski yapıyla
karşılaştırıp test etmeyi açıkça istedi. Bu bir **yerel POC istisnasıdır**;
tamamlanmış üretim gözlemi değildir. Stage 14'ün imzalı state'i, anahtarı,
manifest'i veya saat bilgisi değiştirilmedi. Hazır kanıt paketi üretilmedi.
Artık geçerli olmayan saatlik `stage-14-g-zlemi` otomasyonu silindi.

Veri kaybını önlemek için eski kayıtların ve kuyruktaki eski mesajların okunması
korundu. Veritabanı kolonları/FK'ları silinmedi; bu iş ayrı bir backup, veri
denetimi ve migration gerektirir. Dolayısıyla **eski yapının bütün izleri
silindi** iddiası yoktur.

## Eski ve yeni yapı

| Konu | Önce | Aşama 15 |
| --- | --- | --- |
| Çalışma yolu | Strategy veya seçenekle eski Oracle → SQL Server → Nmap zinciri | Yalnızca Strategy runtime → orchestrator → resolver → executor |
| Eski runtime sınıfları | Üretim projesinde, DI üzerinden erişilebilir | Yalnızca test fixture'larında karşılaştırma referansı |
| Rollback ayarı | `UseStrategyOrchestrator=false` ile eski zincir | `false` açık hata verir; sessiz eski yola dönüş yok |
| Nmap/NSE | Eski temel runtime | Kayıtlı Strategy olarak korunur; NSE desteği kaldırılmaz |
| Yeni RabbitMQ mesajı | Eski ve yeni seçim alanları birlikte | Yalnızca `selectedCheckIds`; eski alan gönderilmez |
| API kural yanıtı | `nseScripts` ve `scanChecks` | Yalnızca `scanChecks` |
| API kontrol sonucu | `nseScriptId` ve `scanCheckId` | Yalnızca `scanCheckId` |
| Web istekleri/raporları | Eski alanlar ve fallback'ler | Genel kontrol alanları |
| Eski giriş/kuyruk | Eski veya çift alan | Okuma desteği korunur; çelişen seçim reddedilir |
| Veritabanı | Eski/yeni kimlikler birlikte | Korundu; yazma uyumluluğu devam eder |
| Çelişen kayıt kimlikleri | Yeni kimlik sessizce tercih edilebilirdi | Hata ve conflict telemetrisi; yanlış kontrol eşlemesi engellenir |

SQL Server Nmap normalizasyonu üretimdeki ayrı
`MsSqlNmapResultNormalizer` sınıfına taşındı; eski runtime fixture'ına üretim
bağımlılığı bırakılmadı.

## Davranış karşılaştırması

`ScanExecutorStrategyParityTests` dondurulmuş eski runtime zincirini, yeni
orchestrator'ı ve gerçek üretim `StrategyScannerRuntime` adaptörünü aynı
deterministik girdilerle çalıştırır.

- PostgreSQL Nmap, MySQL uyumluluk planı, SQL Server ve Oracle birleşik planları.
- Kimlik bilgisi olmayan SQL Server/Oracle native kontrolleri.
- Kontrol kimliği, isim, normalize çıktı, exit code ve sonuç durumu eşitliği.
- Aynı GenericOutput/regex ve StructuredXml/XPath kurallarında aynı bulgu kimliği,
  kanıt, score contribution, önem seviyesi, toplam risk puanı.
- Parolanın normalize/structured çıktıya sızmaması.
- Yanlış engine seçimi, executor kayıtları, katalog yönlendirmesi ve XML artifact
  birleştirme davranışı.

Gerçek Docker SQL Server ve Oracle veritabanlarında mevcut doğrudan probe ile
yeni native Strategy ayrıca karşılaştırıldı. Salt-okunur kontrol çıktıları ve
durumları eşleşti. Testler geçici Testcontainers ortamlarını kullandı;
uygulamanın gerçek veritabanında veri veya şema değişikliği yapılmadı.

`Stage15ContractCutoverTests` geçmiş/yeni kayıtların aynı genel API kimliğine
dönüşmesini, kaldırılan alanın JSON'da bulunmamasını, çelişen kimliklerin
reddedilmesini, açık boş seçim ile varsayılan seçimin ayrılmasını ve korunmuş
credential taşımasını doğrular. Eski üretici → yeni consumer testi korunur.
Ayrı bir negatif uyumluluk testi yeni mesajı eski consumer'ın okuyamadığını
doğrular; birlikte dağıtım uyarısı bu testle de belgelenir.

## Doğrulama

| Kontrol | Sonuç |
| --- | --- |
| Değişiklik öncesi runtime/parity referans grubu | 26 başarılı |
| Application testlerinin son çalışması | 124 başarılı, 0 başarısız |
| Infrastructure testlerinin son çalışması | 130 başarılı, 0 başarısız, 1 atlandı |
| Docker SQL Server ve Oracle native karşılaştırması | Başarılı; atlanmadı |
| Gerçek SQL Server Nmap entegrasyon testi | Atlandı: host üzerinde Nmap yok |
| Web ESLint | Başarılı |
| Web production build / TypeScript | Başarılı |
| .NET solution build | Başarılı; 0 hata, mevcut paketlerden 15 uyarı |

Son tam .NET test koşuları toplam **254 başarılı, 0 başarısız, 1 atlandı**.
Deterministik Nmap testleri başarılı olsa da gerçek Nmap entegrasyonu çalışmış
sayılmaz. Build'in işaretlediği mevcut Microsoft.OpenApi, SSH.NET ve MessagePack
güvenlik uyarıları bu aşamada paket yükselterek çözülmedi.

Komutlar:

```bash
dotnet test tests/DbScanner.Application.Tests/DbScanner.Application.Tests.csproj --no-restore --verbosity minimal
dotnet test tests/DbScanner.Infrastructure.Tests/DbScanner.Infrastructure.Tests.csproj --no-restore --verbosity minimal
dotnet build DbScanner.sln --no-restore --verbosity minimal -m:1 -nr:false
cd src/DbScanner.Web
npm run lint
npm run build
```

## Dağıtım ve geri dönüş

API, Worker ve Web sözleşme kesimini birlikte yayınlamak gerekir. Yeni üretici
eski worker'a seçili kontrol listesini güvenle aktaramaz; eski consumer yeni
alanı görmeyip varsayılan plana dönebilir. Eski worker ile karma sürüm dağıtımı
desteklenmez. API ve Worker kaynak ayarları `stage-15` olarak işaretlendi;
çalışan sürümler aşağıdaki devreye alma kontrolünde ayrıca doğrulandı.

İlk kod/test turunda servisler yeniden başlatılmadı. Kullanıcının ardından
verdiği "güncelle o zaman" komutuyla AppHost mevcut secret ve volume'larla
başlatıldı; eski API/Worker süreçleri çalışır durumda değildi. Eski
runtime flag'i artık rollback değildir; geri dönüş önceki API/Worker/Web
sürümünü birlikte dağıtarak yapılır. Şema değişmediğinden bu dönüş için veri
restore'u bu aşamanın zorunlu bir adımı değildir.

### Yerel devreye alma — 2026-10-06 11:09 UTC

- AppHost: mevcut `http` profili, dashboard `http://localhost:15019`.
- API `/api/status`: `stage-15`, `local`, `ready`; `/health`: `Healthy`.
- Worker: yeni image `scanner-worker:d956bf4fdd233a6474f6a6df7e1cd81d44cb27d7`,
  container `scanner-worker-zutertka`; scan/discovery/sync consumer'ları başladı.
- Prometheus `target_info`: API ve Worker ayrı canlı instance'larla `stage-15`.
- Aynı instance'ların compatibility readiness gauge değeri: API `1`, Worker `1`.
- Web ana sayfa: `http://localhost:3000`, HTTP `200`; güncel kaynakla yeni Next.js süreci.
- Erişim Yönetimi `/admin/users`: HTTP `200`; sayfa derlenip servis edildi
  (oturumlu kullanıcı işlemleri bu smoke kontrolüne dahil değildir).
- Keycloak realm keşif endpoint'i: HTTP `200`; mevcut realm import sırasında
  korunarak yeniden kullanıldı.
- Oturumsuz korumalı `/api/rules` isteği: HTTP `401`, erişim koruması açık.
- Worker içindeki Nmap: `7.94SVN`; 605 NSE scripti senkron, 0 başarısız.
- SQL Server init görevi: exit `0`. Bu tek seferlik görevin `Exited` olması normal.
- Volume silinmedi, secret yeniden üretilmedi, yıkıcı şema temizliği yapılmadı.

Bu kontroller başlangıç/sağlık ve sürüm eşleşmesi doğrulamasıdır; bu devreye alma
turunda yeni kullanıcı veya yeni tarama oluşturulmadı. İmzalı Stage 14 gözlem
penceresi yeniden başlatılmadı ve üretim gözlemi tamamlandı iddiası yoktur.

## Kalan sınırlar

1. Eski HTTP giriş alanları, kuyruk okuma alias'ı ve persist edilen çift kimlikler
   ayrı bir geçişte kaldırılabilir. Önce kuyruk boşaltma ve backup/row-count/FK
   denetimi gerekir.
2. `IScannerRuntime`, bazı katalog/model isimleri ve XML kural adaptörü hâlâ
   eski terimleri içerir. NSE katalog isimlerinin tamamı gereksiz değildir:
   Nmap/NSE yürütücüsünün gerçek domain'idir.
3. Yeni JSON-only / External API yürütücüsü için generic rule payload sınırı
   genişletilmelidir. Strategy kaydı yapılabilmesi, mevcut XML/XPath kurallarının
   başka formata otomatik uyum sağlaması anlamına gelmez.
4. Üretimde yedi günlük kanıt şartı bu POC istisnasıyla sağlanmış sayılmaz.
