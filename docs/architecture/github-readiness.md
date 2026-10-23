# GitHub yayın hazırlığı — 2026-10-07

Güncel kontrol ve güvenlik yamaları için [24 Ekim 2026 yayın kontrolüne](github-readiness-2026-10-24.md) bakın.
Bu belgedeki paket sürümleri ve audit sonuçları tarihsel kayıttır.

Bu değişiklik repository'yi yayına hazırlar; commit/push, canlı realm/parola
rotasyonu, veritabanı temizliği veya Git geçmişi rewrite yapılmaz.

## Özel dosyalar ve secret'lar

- `.tmp-staj/`, `deliverables/`, `tmp/`, `output/` ve observability `*.local.json`
  Git ve Docker build context'i dışında bırakıldı. Dosyalar silinmedi.
- Dotenv, `secrets.json`, private `.key/.pem` ve `.dump/.backup` dosyaları da dışlandı.
  Örnek rollout manifest'i, init SQL ve kaynak kod/migration dosyaları korunur.
- Keycloak realm başlangıç yönetici parolası `${DBSCANNER_INITIAL_ADMIN_PASSWORD}`
  yer tutucusuna taşındı. AppHost bunu `dbscanner-initial-admin-password` secret
  parametresinden aktarır; shell ve PowerShell setup scriptleri aynı 12 anahtarı
  provision eder. İlk girişte parola değişikliği zorunludur.
- Demo PostgreSQL/MySQL yönetici parolaları da rastgele provision edilir. Bilerek
  savunmasız anonim/boş parola demo senaryoları test amacıyla korunmuştur; bu demo
  ortamı internete açılacak üretim kurulumu değildir.
- API ayarlarındaki sabit veritabanı parolası ve runtime DI fallback parolası
  kaldırıldı. Bağlantı verilmezse açık hata oluşur. Aspire bağlantıyı aktarır;
  standalone deployment kendi secret/environment bağlantısını vermelidir.
- Design-time model factory parola içermez. DB migration/update için kendi
  `DBSCANNER_MIGRATION_CONNECTION` bağlantınızı güvenli ortamda sağlayın.
- İlk kurulum README'sine `npm --prefix src/DbScanner.Web ci` eklendi; temiz klonda
  Web bağımlılıkları kurulmadan AppHost başlatılmaz.
- Yerel observability Compose portları varsayılan olarak loopback'e bağlanır;
  anonim Grafana görüntüleme LAN'a açılmaz. Collector'a ayrı Linux container'ından
  erişileceğinde `DBSCANNER_OTLP_BIND_ADDRESS` uygun host bridge adresi olarak açıkça
  ayarlanmalıdır; DNS/gateway ve firewall kuralları ayrıca gerekir. `0.0.0.0` seçmek
  tekrar ağ erişimi açar; üretim collector'ında ayrıca TLS/auth kullanılmalıdır.

Mevcut User Secrets değerleri değiştirilmedi; yalnızca dört eksik anahtar oluşturuldu.
Keycloak startup import mevcut realm'i tekrar yazmaz; yeni secret var olan kullanıcı
parolasını resetlemez. Önceki sabit başlangıç veya veritabanı parolalarını gerçek
hesaplarda kullanıyorsanız onları ayrıca değiştirin. Eski Git commit'lerinde eski
development değerleri bulunabilir; dosyadan kaldırmak geçmişten silmek değildir.
Bu çalışma tüm geçmişin her türlü secret'tan arındığı garantisini vermez.

## Paket değişiklikleri

- Next.js ve `eslint-config-next`: 16.2.12 → 16.4.0; npm lockfile yeniden üretildi.
  Üretim bağımlılıklarının transitif yamaları da alındı.
- API: `Microsoft.OpenApi` 2.7.5 güvenlik override. ASP.NET OpenAPI'nin transitif
  seçimi hâlâ 2.0.0 olduğu için parent upgrade tek başına yeterli değildi.
- EF Tools: 11 preview → 10.0.4; EF Design ve yerel dotnet-ef sürümüyle hizalandı.
- AppHost: `MessagePack` 2.5.307 güvenlik patch override. Aspire/imaj sürümü
  yükseltilmedi; mevcut PostgreSQL volume layout'u değiştirilmedi.
- `Testcontainers.MsSql`: 4.13.0 → 4.14.0; transitif `SSH.NET` 2026.0.0 oldu.
- Yeni React Hooks lint kuralıyla yakalanan discovery ağ seçimi düzeltildi:
  varsayılanlar sorgu verisinden türetilir; kullanıcının açık seçimi (boş seçim
  dahil) state'te korunur. Kural kapatılmadı.

Resmî kaynaklar: [Next.js Windows duyurusu](https://github.com/vercel/next.js/security/advisories/GHSA-p293-qw3h-jr36),
[OpenAPI duyurusu](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc),
[MessagePack patch sürümü](https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v2.5.307),
[Keycloak environment import](https://www.keycloak.org/server/importExport).

## Kalan bulgu

Tüm npm bağımlılıkları için audit hâlâ 5 `high` bildiriyor. Bunlar beş bağımsız
runtime açık değil, aynı yamalanmamış `braces` → `micromatch` → `fast-glob` →
`@next/eslint-plugin-next` → `eslint-config-next` geliştirme bağımlılığı zinciri.
Üretim audit'i bu zinciri içermez. Yayınlanmış uyumlu yama bulunmadığından ESLint
çıkarılmadı, kural kapatılmadı ve kör `npm audit fix --force` downgrade uygulanmadı.
Lint/build araçlarına güvenilmeyen glob girdileri verilmemeli; üst bağımlılık
yamalandığında takip edilmelidir. “Tüm paketler sıfır açık” diye raporlanmaz.
[Yamasız braces duyurusu](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm).

## Yayın öncesi kontrol

```bash
python3 -m unittest discover -s tests/ops -v
dotnet build DbScanner.sln
dotnet test tests/DbScanner.Application.Tests/DbScanner.Application.Tests.csproj
dotnet test tests/DbScanner.Infrastructure.Tests/DbScanner.Infrastructure.Tests.csproj
dotnet list DbScanner.sln package --vulnerable --include-transitive
```

Web klasöründe:

```bash
npm ci
npm run lint
npm run build
npm audit --omit=dev
npm audit
```

Kendi commit'inizi oluştururken önce `git status --short` ve `git diff --cached`
ile içeriği inceleyin. Yeni `src/`, `tests/`, migration, `scripts/`, `docs/` ve
gerekli `ops/` dosyalarını ekleyin; `.gitignore` bu işlemden önce commit kapsamına
alınmalıdır. `.env`/özel belgeyi `git add -f` ile zorla eklemeyin. Eklenecek bir
license işletme/kullanım kararı olduğundan bu görevde seçilmedi.

## Son doğrulama

- Solution build: 0 hata, 0 uyarı.
- Application/API: 191 başarılı, 0 başarısız; patched OpenAPI ile gerçek scan
  sözleşmelerinden HTTP OpenAPI belge üretimi ayrıca geçti.
- Infrastructure: saat kaynağı düzeltmesinden sonra iki ardışık tam koşuda da
  174 başarılı, 0 başarısız, 1 atlandı (53 ve 73 saniye). Atlanan mevcut host Nmap
  gerektiren testtir. Önceki üç aralıklı outbox/worker/tenant hatası ilk dispatch'e
  bağlandı: host `UtcNow` ile DB clock birlikte kullanılıyordu. Kontrollü saat
  farkı testi eski kodda başarısız oldu, DB saatini kullanan publisher ile geçti;
  gerçek erteleme filtresinin korunduğunu doğrulayan ikinci test de eklendi.
  Gerçek başarısız koşudaki saat farkının büyüklüğü ölçülmüş değildir.
  Ayrıntılar: [güvenilirlik raporu](scan-job-reliability.md#saat-kaynağı-düzeltmesi--2026-10-07).
- Python ops/yayın güvenliği: 37 başarılı. Toplam 402 başarılı, 1 atlanan test.
- Temiz npm lock kurulum testi (`npm ci --ignore-scripts`) başarılı; ardından
  Web lint, TypeScript ve production build başarılı. Windows/Linux native paketleri
  lock'ta mevcut; Windows runtime bu macOS ortamında çalıştırılmadı.
- NuGet transitif audit: 9 projede 0 bildirilen açık. npm production audit: 0;
  tüm npm audit: yukarıda açıklanan 5 dev-only high bulgu.
- Git'in tracked/untracked yayın adayları arasında özel belge/görsel/secret yedeği
  kalmadı; `.gitignore` guard testleri ve scoped whitespace kontrolü geçti.
- Compose config doğrulandı; beş host portu varsayılan 127.0.0.1'e bağlanıyor.
  Çalışan observability servisleri bu görevde yeniden başlatılmadı; mevcut instance
  eski port binding'i yeniden oluşturulana kadar kullanabilir.
- Canlı Keycloak/import yeniden çalıştırılmadı; migration veya gerçek kullanıcı
  parola değişimi yapılmadı. User Secrets'ta dört eksik anahtar eklendi, önceki
  sekiz değer korundu. Özel belgeler silinmedi; Git index/commit/push değiştirilmedi.

Son saat kaynağı düzeltmesinde backend build/test ve Python sonuçları yenilendi;
Web/paket audit sonuçları önceki yayın hazırlığı koşusuna aittir. Bu düzeltme
canlı API/Worker dağıtımı yapmadı; yeni derlemeyle yeniden başlatılmalıdır.
