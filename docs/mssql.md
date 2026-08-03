# Microsoft SQL Server desteği

API ve veri modeli için tek canonical engine değeri `mssql`, kullanıcı
arayüzündeki adı **Microsoft SQL Server**, varsayılan bağlantı portu `1433`'tür.
SQL Server Browser keşfi ayrıca UDP `1434` üzerinden yapılır.

## Tarama modları

### Anonymous discovery

Kimlik bilgisi olmadan mevcut Nmap hattı çalışır:

- 1433/TCP port durumu ve servis sürüm tespiti
- `ms-sql-s`, `mssql`, `Microsoft SQL Server`, `SQL Server` ve TDS fingerprint
  normalizasyonu
- ürün ve sürüm metadata değerlendirmesi
- güvenli `ms-sql-info` sonucu
- UDP 1434 SQL Server Browser yanıt kontrolü

Browser probe yalnızca tek bir keşif paketi yollar. Brute force, exploit veya
kimlik doğrulama denemesi yapmaz. Kimlik bilgisi isteyen kontroller,
credential verilmediyse `Skipped/credentials_not_provided` döner.

### Authenticated security checks

Target oluşturma veya düzenleme akışında database, username, password,
`Encrypt`, `TrustServerCertificate` ve 1–30 saniyelik connection timeout
kaydedilebilir. Target satırındaki ortak **Scan Başlat** işlemi önce keşfi
çalıştırır; target üzerinde credential varsa authenticated kontrolleri aynı
scan içinde otomatik sürdürür. Bağlantı `Microsoft.Data.SqlClient` ve
`SqlConnectionStringBuilder` ile kurulur. Tüm sorgular salt okunur metadata
sorgularıdır.

Kontroller:

- ürün sürümü, product level, edition, geçerli login ve database
- scanner login'in sysadmin üyeliği
- login/database-user eşleşmeleri ve orphan user'lar
- sysadmin sayısı
- guest erişimi ve `TRUSTWORTHY`
- `xp_cmdshell`, OLE Automation, CLR ve Ad Hoc Distributed Queries
- cross-database ownership chaining
- mixed authentication mode ve riskli login adları
- SQL Server Audit görünürlüğü
- login password policy ve expiration metadata
- public server/database role izinleri
- database owner ve geniş server/database izinleri
- TDE ve erişilebiliyorsa son full backup encryption metadata

Sonuçlar `Passed`, `Failed`, `Skipped`, `NotSupported`,
`InsufficientPrivileges`, `AuthenticationFailed` veya `ConnectionFailed`
olarak saklanır. İzin
yetersizliği finding değildir. Bağlantı ve sorgu hataları host, timeout, TLS,
authentication, database, privilege ve provider kategorilerine ayrılır.

## Credential güvenliği

- Credential target tablosunda yalnızca AES-256-GCM ile şifreli zarf olarak
  saklanır; scan sonucuna, finding evidence'a veya API response'a yazılmaz.
- API target credential zarfını açar ve RabbitMQ mesajına koymadan önce yeni
  nonce ile tekrar AES-256-GCM kullanarak şifreler.
  API ve worker aynı 32-byte base64 `CredentialProtection__Key` değerini
  kullanmalıdır.
- UI password değerini React state'ine koymaz ve gönderimden sonra input'u
  temizler.
- Loglar target ID ve güvenli hata kategorisi içerir; connection string,
  password veya SQL parametresi içermez.
- Üretimde `Encrypt=true`, doğrulanabilir sunucu sertifikası ve
  `TrustServerCertificate=false` kullanın.

Mesaj kuyruğunda bekleyen credential zarfı kısa ömürlü olsa da kuyruk erişimi
ayrıca sınırlandırılmalı, TLS etkinleştirilmeli ve credential protection key
periyodik olarak kontrollü biçimde döndürülmelidir.

## En düşük yetkili scanner login

Aşağıdaki örnek sysadmin veya data reader yetkisi vermez. Login parolasını
secret store'dan sağlayın; örneği olduğu gibi üretime kopyalamayın.

```sql
USE [master];
CREATE LOGIN [dbscanner_scanner]
  WITH PASSWORD = N'<SECRET_STORE_VALUE>',
       CHECK_POLICY = ON,
       CHECK_EXPIRATION = ON;

GRANT VIEW SERVER STATE TO [dbscanner_scanner];
GRANT VIEW ANY DEFINITION TO [dbscanner_scanner];
GRANT VIEW ANY DATABASE TO [dbscanner_scanner];
GRANT CONNECT ANY DATABASE TO [dbscanner_scanner];

USE [ApplicationDatabase];
CREATE USER [dbscanner_scanner] FOR LOGIN [dbscanner_scanner];
GRANT CONNECT TO [dbscanner_scanner];
GRANT VIEW DEFINITION TO [dbscanner_scanner];
GRANT VIEW DATABASE STATE TO [dbscanner_scanner];
```

Bu izinlerle bile bazı edition/sürüm özellikleri veya `msdb` backup metadata
erişilemeyebilir; ilgili kontrol `InsufficientPrivileges` ya da `Skipped`
döner. Backup encryption kontrolü gerçekten gerekiyorsa yalnızca gerekli
`msdb` view/table erişimini kurum politikasına göre ayrıca verin. `sysadmin`,
`CONTROL SERVER`, `db_owner` veya uygulama verilerine `SELECT` vermeyin.

## Demo ortamı

AppHost şu sabit image'ı kullanır:

`mcr.microsoft.com/mssql/server:2022-CU22-GDR1-ubuntu-22.04`

`2022-latest` kullanılmaz. Veriler named volume içinde
`/var/opt/mssql` altında saklanır. `demo-mssql-init` kaynağı SQL Server gerçek
bir `SELECT 1` sorgusu kabul ettikten sonra
`docker/mssql-init/001-initialize-dbscanner-demo.sql` dosyasını çalıştırır.
Script idempotenttir; database, login, user, permission, tablo ve örnek veriyi
yeniden güvenle uzlaştırır.

AppHost secret'larını repository'ye yazmadan tanımlayın:

```powershell
$project = "src/DbScanner.AppHost/DbScanner.AppHost.csproj"
dotnet user-secrets --project $project set "Parameters:demo-mssql-password" "<SA_PASSWORD>"
dotnet user-secrets --project $project set "Parameters:demo-mssql-app-password" "<APP_PASSWORD>"
dotnet user-secrets --project $project set "Parameters:demo-mssql-scanner-password" "<SCANNER_PASSWORD>"
$key = [Convert]::ToBase64String(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets --project $project set "Parameters:scanner-credential-protection-key" $key
dotnet run --project $project
```

CI/environment configuration için aynı anahtarlar çift alt çizgiyle
verilebilir; örneğin `Parameters__demo-mssql-password`. Docker/Kubernetes
secret kullanan dağıtımlarda secret dosyasını deployment katmanında ilgili
configuration anahtarına veya `CredentialProtection__KeyFile`'a projekte edin.
Gerçek secret'ı image'a ya da compose dosyasına gömmeyin.

Güvenli demo varsayılandır. Kontrollü risk senaryosunda:

```powershell
dotnet user-secrets --project $project set "Parameters:demo-mssql-risky-settings" "true"
```

Bu ayar yalnızca demoda platformun desteklediği CLR, Ad Hoc Distributed
Queries, TRUSTWORTHY, DB chaining ve guest risklerini açar. SQL Server 2022
Linux image'ında system extended stored procedure'ler (`xp_cmdshell` dahil)
desteklenmediğinden init bunları değiştirmeye çalışmaz; probe görünür
configuration metadata'sını yine raporlar. Probe hiçbir riskli özelliği
çalıştırmaz.

## Testler

```powershell
dotnet test tests/DbScanner.Infrastructure.Tests/DbScanner.Infrastructure.Tests.csproj -c Release
```

`MsSqlContainerIntegrationTests`, sabit SQL Server image'ını Testcontainers ile
başlatır; TCP erişimini, query health'ini, geçerli/geçersiz credential
davranışını, version fingerprint'ini, sysadmin sonucunu, xp_cmdshell,
TRUSTWORTHY, guest, yetersiz izin ve secret sızıntısı davranışlarını sınar.
Linux container'da değiştirilemeyen xp_cmdshell için kapalı durum gerçek
probe'da, finding üretimi ise application E2E rule testinde doğrulanır.
Nmap da kuruluysa gerçek servis sınıflandırma testi ayrıca çalışır.

Docker daemon veya Nmap yoksa ilgili test `Skipped` ve açık gerekçeyle görünür.
CI'da integration testlerini gerçekten çalıştırmak için Linux container
destekli Docker daemon, yeterli disk/RAM ve MCR erişimi sağlayın.

## Production kontrol listesi

- SQL Server'ın desteklenen CU/GDR sürümünü kurum patch politikasına göre
  sabitleyin ve digest doğrulamasını deployment katmanında uygulayın.
- DBScanner erişimini network allowlist ve firewall ile sınırlandırın; UDP 1434
  gerekmiyorsa kapatın.
- Ayrı, düşük yetkili scanner login kullanın; uygulama veya SA credential'ını
  kullanmayın.
- TLS sertifikasını doğrulayın ve `TrustServerCertificate=false` kullanın.
- Secret'ları vault/Docker/Kubernetes secret ile sağlayın; kaynak kod, log,
  telemetry ve crash dump'lardan uzak tutun.
- RabbitMQ için TLS, ayrı vhost/user, kısa retention ve en düşük yetki
  uygulayın.
- Audit, TDE ve backup encryption sonuçlarını edition, izin ve veri
  sınıflandırması bağlamında değerlendirin.
- Riskli ayarları değiştiren demo init seçeneğini production'da kullanmayın.
