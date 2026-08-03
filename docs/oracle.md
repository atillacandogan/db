# Oracle Database desteği

Oracle için canonical engine değeri `oracle`, görünen isim `Oracle Database` ve
varsayılan Listener portu `1521`'dir. Oracle targetları diğer engine'lerle aynı
target, script seçimi, `Scan Başlat`, scan history, finding ve rapor akışını
kullanır. Oracle'a özel ikinci bir scan endpoint'i veya authenticated scan
butonu yoktur.

## Tarama davranışı

Her taramada Nmap `-sV` servis fingerprint analizi ve güvenli
`oracle-tns-version` NSE scripti çalışır. Bu aşama Oracle Listener/TNS sürümü ve
servis metadata'sını parola denemeden toplar. Kullanıcı enumerasyonu, SID brute
force ve parola brute force scriptleri seed edilmez.

Target üzerinde credential yoksa `oracle-auth-*` kontrolleri
`Skipped / credentials_not_provided` olarak kaydedilir. Bu durum finding veya
zafiyet değildir. Credential varsa şifreli RabbitMQ scan mesajındaki mevcut
ortak credential taşıma mekanizması kullanılır ve discovery tamamlandıktan sonra
read-only ODP.NET kontrolleri otomatik çalışır.

Yanlış parola `AuthenticationFailed`; bağlantı sorunları `ConnectionFailed`;
dictionary view yetkisi eksikliği `InsufficientPrivileges` olarak kaydedilir.
Bu durumlar discovery sonuçlarını silmez ve scan'i bütünüyle çökertmez.

`Oracle.ManagedDataAccess.Core` resmi Oracle sürücüsü kullanılır.
`OracleConnectionStringBuilder` connection string'i üretir. Parola API
response, log, finding evidence ve frontend state içinde tutulmaz. Target
credential formundaki `Database` alanı Oracle için service name'dir (demo
ortamında `FREEPDB1`).

## Authenticated kontroller

- `oracle-auth-version`: sürüm ve edition
- `oracle-auth-session-privileges`: session role ve privilege envanteri
- `oracle-auth-dba-users`: DBA rolü verilen sistem dışı kullanıcılar
- `oracle-auth-risky-users`: açık varsayılan, örnek veya riskli hesaplar
- `oracle-auth-unlocked-unused-accounts`: açık ve kullanılmayan Oracle hesapları
- `oracle-auth-password-profiles`: zayıf password profile limitleri
- `oracle-auth-audit-configuration`: unified audit policy görünürlüğü
- `oracle-auth-risky-system-privileges`: yüksek etkili system privilege grantleri
- `oracle-auth-public-privileges`: PUBLIC rolüne riskli grantler
- `oracle-auth-database-metadata`: database metadata envanteri

## Demo container

Aspire sabit `gvenzl/oracle-free:23.26.1-slim` image'ını kullanır. Oracle
Database Free lisans koşulları deployment öncesinde ayrıca değerlendirilmelidir.
Container `/opt/oracle/oradata` için persistent volume, hostta `11521` portu ve
`SELECT 1 FROM DUAL` ODP.NET readiness kontrolü kullanır.

`docker/oracle-init/001-initialize-dbscanner-demo.sql`,
`DBSCANNER_SCANNER.SAMPLE_ASSETS` tablosunu idempotent şekilde oluşturur ve
demo satırlarını `MERGE` ile yükler.

Secret'ları repository dışında tanımlayın:

```powershell
$project = "src/DbScanner.AppHost/DbScanner.AppHost.csproj"
dotnet user-secrets --project $project set "Parameters:demo-oracle-password" "<SYSTEM_PASSWORD>"
dotnet user-secrets --project $project set "Parameters:demo-oracle-scanner-password" "<SCANNER_PASSWORD>"
```

Demo target credential'ında service `FREEPDB1`, kullanıcı
`dbscanner_scanner` ve scanner secret'ı kullanılır.

## Test

`OracleSecurityTests` canonical mapping, safe connection string ve birleşik
scan davranışını; `OracleContainerIntegrationTests` gerçek sabit Oracle
Database Free container üzerinde başarılı ve reddedilen ODP.NET bağlantılarını
doğrular. Docker yoksa container testi açık bir skip gerekçesiyle atlanır.
