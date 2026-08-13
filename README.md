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

Windows PowerShell:

```powershell
.\scripts\setup-dev-secrets.ps1
dotnet run --project src/DbScanner.AppHost/DbScanner.AppHost.csproj
```

macOS/Linux:

```bash
bash scripts/setup-dev-secrets.sh
dotnet run --project src/DbScanner.AppHost/DbScanner.AppHost.csproj
```

Docker volume'ları veya uygulama veritabanı başka bir bilgisayardan taşındıysa
yeni secret üretmek yerine önceki parolalar ve
`scanner-credential-protection-key` değeri aynen aktarılmalıdır.

Microsoft SQL Server demo ortamı ve target seviyesinde credential kullanan
tek scan akışının ayrıntıları için
[Microsoft SQL Server desteği](docs/mssql.md) belgesine bakın.

Oracle Listener/TNS keşfi, read-only kontroller ve demo ortamı için
[Oracle Database desteği](docs/oracle.md) belgesine bakın.

Yerel Keycloak oturumu, roller ve uzak ortam yapılandırması için
[Keycloak kimlik doğrulama](docs/keycloak.md) belgesine bakın.

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
