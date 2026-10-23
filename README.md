# DBScanner

DBScanner, PostgreSQL, MySQL, Microsoft SQL Server ve Oracle Database hedeflerinde izinli kapsam
üzerinde servis keşfi ve güvenli yapılandırma kontrolleri çalıştıran bir
.NET/Aspire uygulamasıdır.

## Geliştirme

Gereksinimler:

- .NET 10 SDK
- Node.js ve npm
- Docker Desktop veya uyumlu bir Docker daemon
- Gerçek servis keşfi için `nmap`

Projeyi yeni bir bilgisayarda ilk kez çalıştırmadan önce AppHost'un yerel
geliştirme secret'larını oluşturun. Script yalnızca eksik değerleri üretir;
mevcut secret'ları değiştirmez ve değerleri repository'ye yazmaz.
Yeni kurulumda uygulama yöneticisinin geçici parolası da rastgele üretilir; herkes
için ortak bir başlangıç parolası yoktur. Mevcut kurulumda scripti tekrar çalıştırmak
yalnızca eksik yeni anahtarları ekler; mevcut Keycloak hesaplarını değiştirmez.

Windows PowerShell:

```powershell
.\scripts\setup-dev-secrets.ps1
npm --prefix src/DbScanner.Web ci
dotnet run --project src/DbScanner.AppHost/DbScanner.AppHost.csproj
```

macOS/Linux:

```bash
bash scripts/setup-dev-secrets.sh
npm --prefix src/DbScanner.Web ci
dotnet run --project src/DbScanner.AppHost/DbScanner.AppHost.csproj
```

### Web kaynağı `Finished` durumuna geçerse

Aspire logunda `node_modules/.bin/next: Permission denied` görülüyorsa Web
uygulaması tamamlanmış değildir; Next.js süreci hiç başlayamadığı için npm hata
ile kapanmış ve Aspire kaynağı `Finished` olarak göstermiştir. Bu durum çoğunlukla
başka bir işletim sisteminde oluşturulan veya arşiv/mesajlaşma uygulamasıyla
taşınan `node_modules` klasöründeki çalıştırma izinlerinin kaybolmasından
kaynaklanır.

`node_modules` klasörünü kopyalamayın. Bağımlılıkları hedef makinede kilit
dosyasından yeniden kurun ve ardından AppHost'u yeniden başlatın:

```bash
cd src/DbScanner.Web
npm ci
cd ../..
dotnet run --project src/DbScanner.AppHost/DbScanner.AppHost.csproj
```

Docker volume'ları veya uygulama veritabanı başka bir bilgisayardan taşındıysa
yeni secret üretmek yerine önceki parolalar ve
`scanner-credential-protection-key` değeri aynen aktarılmalıdır.

Discovery'nin porttan bağımsız protocol fingerprint davranışını test etmek için
`demo-mysql-on-postgres-port-target` container'ı MySQL 8.4'ü container içinde
`5432` portunda çalıştırır. Aspire host endpoint'i `15433`'tür. Docker/Aspire
ağını Advanced discovery ile taradığınızda `5432` açık olmasına rağmen
sonuç `MySQL / Confirmed` olmalıdır.

Microsoft SQL Server demo ortamı ve target seviyesinde credential kullanan
tek scan akışının ayrıntıları için
[Microsoft SQL Server desteği](docs/mssql.md) belgesine bakın.

Oracle Listener/TNS keşfi, read-only kontroller ve demo ortamı için
[Oracle Database desteği](docs/oracle.md) belgesine bakın.

Yerel Keycloak oturumu, roller ve uzak ortam yapılandırması için
[Keycloak kimlik doğrulama](docs/keycloak.md) belgesine bakın.

AppHost, API/Worker veritabanı bağlantısını kaynak referansı üzerinden verir. API'yi
tek başına çalıştıracaksanız `ConnectionStrings__DefaultConnection` veya
`ConnectionStrings__dbscanner` değerini kendi ortamınızda/User Secrets'ta sağlayın;
kaynak kodda varsayılan veritabanı parolası yoktur. Veritabanına uygulanan EF
migration komutlarında `DBSCANNER_MIGRATION_CONNECTION` gerekir; modeli üretme ve
snapshot kontrolü gibi bağlantısız komutlar parola gerektirmez.

Temel doğrulama:

```powershell
dotnet build DbScanner.sln -c Release
dotnet test tests/DbScanner.Application.Tests/DbScanner.Application.Tests.csproj -c Release
dotnet test tests/DbScanner.Infrastructure.Tests/DbScanner.Infrastructure.Tests.csproj -c Release
Set-Location src/DbScanner.Web
npm run lint
npm run build
```

Docker bulunmadığında Microsoft SQL Server ve Oracle Database Testcontainers testleri açık bir
`Skipped` gerekçesiyle raporlanır; başarılı çalışmış sayılmaz.

## Aşama 15: Strategy çalışma yolu

Tarama artık yalnızca `StrategyScannerRuntime` üzerinden çalışır. Nmap/NSE,
MySQL native, SQL Server native ve Oracle native yürütücüleri ortak resolver ve
orchestrator üzerinden seçilir. Eski runtime zinciri sadece karşılaştırma
testlerinde tutulur; `Scanner:UseStrategyOrchestrator=false` desteklenmez.

API çıktıları ve yeni RabbitMQ mesajları genel `scanCheckId` / `scanChecks` /
`selectedCheckIds` alanlarını kullanır. API, Worker ve Web birlikte güncellenmelidir;
eski consumer'a yeni mesaj gönderimi güvenli değildir. Kuyrukta kalan eski
mesajların ve eski veritabanı kayıtlarının okunması hâlâ desteklenir. Veritabanı
kolonları bu aşamada silinmez. Yeni yürütücüler Strategy ile kaydedilebilir;
XML/XPath kural sınırının tamamen JSON gibi başka formatlara taşınması ayrı iştir.

POC kapsamı, eski/yeni karşılaştırması ve doğrulama sonuçları:
[Aşama 15 raporu](docs/architecture/stage-15-report.md).

## Tarama işi güvenilirliği

`stage-15-reliability` sürümünde tarama ve outbox mesajı aynı transaction'da
kaydedilir. Worker broker confirm'iyle gönderir, lease/heartbeat ile işi sahiplenir
ve kesilen işleri orijinal kontrol seçimiyle kurtarır. Sonuç yazımı sahiplik token'ıyla
korunur; tamamlanmış tarama tekrar teslimde yeniden çalıştırılmaz. API ve Worker
birlikte güncellenmelidir. Migration mevcut tablolara yalnızca kolon/tablo ekler.

Varsayılanlar: heartbeat 10 saniye, lease 60 saniye, recovery 15 saniye, ilk
çalışma dahil 3 deneme. Ayarlar, test kanıtı ve kalan sınırlar için
[güvenilirlik raporu](docs/architecture/scan-job-reliability.md).

## Ekip ve hedef yetkileri

`stage-15-resource-access` sürümünde admin/operator/viewer rolleri korunur; operator
ve viewer hedeflere yalnızca ekip/proje grant'i ile erişir. Web'deki **Ekip ve Hedef
Yetkileri** ekranından proje, ekip, üyelik ve görüntüleme/tarama/düzenleme/export
izinleri yönetilir. Mevcut hedefler varsayılan projededir; otomatik geniş erişimli
ekip oluşturulmaz. API, tarama geçmişi ve raporlar aynı hedef filtresini kullanır.

Çok kurumlu mod kurum başına ayrı API/Worker, PostgreSQL veritabanı/login ve RabbitMQ
vhost'u gerektirir; yalnızca TenantId kolonu ekleyen bir yapı değildir. Yerel AppHost
tek kurum modunda kalır. Kurulum, yetki tablosu, testler ve üretim sınırları:
[Ekip/hedef yetkisi ve kurum izolasyonu](docs/architecture/resource-access-and-tenancy.md).

## GitHub'a yayınlama

Özel staj belgeleri, imzalar, geçici PDF/görsel çıktıları, yerel rollout manifest'i,
dotenv dosyaları, private key'ler ve veritabanı yedekleri Git ve Docker build
kapsamı dışında tutulur. Kaynak kod/migration/test dosyalarını birlikte commit edin;
yalnız mevcut commit'leri push etmek çalışma dizinindeki yenilikleri göndermez.

Güncel güvenlik yamaları, doğrulama sonuçları ve üretim sınırları:
[24 Ekim 2026 GitHub hazırlık raporu](docs/architecture/github-readiness-2026-10-24.md).
