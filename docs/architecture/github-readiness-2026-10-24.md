# GitHub yayın kontrolü — 2026-10-24

## Karar ve kapsam

Kaynak kodun GitHub'da paylaşılması için yapılan kontrol, uygulamanın internete
açık üretim dağıtımını onaylamaz. Demo AppHost bilerek savunmasız MySQL hedefleri
ve geliştirme amaçlı HTTP kimlik sunucusu içerir. Üretim dağıtımında ayrı API/Worker,
TLS, düşük yetkili veritabanı hesabı, korunan şifreleme anahtarı ve kısıtlı ağ
erişimi gerekir. Kurum izolasyonunun dağıtım koşulları
[ilgili belgede](resource-access-and-tenancy.md) açıklanır.

Kontrol başlangıcında yerel `main`, GitHub `origin/main` ile aynı commit'teydi
(`d044616`). Depo herkese açıktır. Yeni yerel sunum üreticisi
`scripts/generate-presentation.js`, makineye özgü font/bağımlılık yolları ve
doğrulanmamış ürün iddiaları içerdiğinden Git ve Docker kapsamı dışında bırakıldı.
Dosya yerel diskte korunur.

## Uygulanan güvenlik düzeltmeleri

- Next.js ve `eslint-config-next`: `16.4.0` → `16.4.3`. İlk audit'te Next.js
  kritik önem dereceli bildirimlerle raporlandı. Aynı sürüm serisinin yaması
  uygulandı; lockfile yenilendi.
- Transitif Zod: `4.6.5` → `4.6.7`; yüksek önem dereceli audit bulgusu giderildi.
- Infrastructure ve AppHost'a `System.Security.Cryptography.Pkcs` `9.0.21`
  güvenlik override'ı eklendi. Eski transitif `9.0.13` sürümünün moderate/high
  bildirimleri temizlendi.
- `ScannerOptions` ve Worker'ın normal/geliştirme ayarlarında
  `UseSampleOutputWhenUnavailable=false` varsayılanı uygulandı. Nmap bulunmazsa
  gerçek tarama başarısızlığı örnek XML ile başarıya çevrilmez. Açıkça örnek çıktı
  seçen izole testler korunur. Eksik executable ile gerçek runtime çağrısının
  varsayılan olarak hata verdiğini doğrulayan regresyon testi eklendi.

Resmî bildirimler:
[Next.js](https://github.com/vercel/next.js/security/advisories/GHSA-2464-fhr4-w9xw),
[.NET PKCS](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx).

## Doğrulama

| Kontrol | Sonuç |
| --- | --- |
| .NET Release solution build | 0 hata, 0 uyarı |
| Application/API testleri | 214 başarılı, 0 başarısız |
| Infrastructure testleri | 148 başarılı, 0 başarısız, 30 atlanan |
| Web testleri | 60 başarılı, 0 başarısız |
| Python ops/yayın koruma testleri | 37 başarılı, 0 başarısız |
| Web ESLint | Başarılı |
| Web production build | Başarılı (Next.js 16.4.3) |
| NuGet transitif audit | 9 projede bildirilen açık yok |
| npm production audit | 0 bildirilen açık |
| npm tam audit | 0 bildirilen açık |
| Observability Compose config | Başarılı |

Toplam 459 test başarılı, 30 test atlandı; başarısız test yok.

Docker daemon çalışmadığından 30 container/Nmap entegrasyon testi atlandı.
PostgreSQL/RabbitMQ gerçek outbox, tenant izolasyonu, SQL Server ve Oracle
container senaryoları bu koşuda çalışmış sayılmaz. Host üzerinde Nmap de yoktur.
Container image derlemesi ve canlı servis sağlığı doğrulanmadı. Uygulamanın
üretimde çalıştırılmasından önce bu entegrasyon testleri Docker ve Nmap olan
ortamda tamamlanmalıdır.

Güncel takip edilen 528 dosyada ve dört mevcut Git commit'inin snapshot'larında
private key, GitHub token, AWS access key ve OpenAI key biçimleri arandı; eşleşme
bulunmadı. Bu sınırlı imza araması tüm olası secret türlerinin denetimi değildir.
Takip edilen dosya adlarında veya geçmiş dosya listesinde özel belge, dotenv,
private key veya veritabanı yedeği adayı bulunmadı. Mevcut ignore kuralları özel
çıktıları ve secret dosyalarını dışarıda tutar; koruma testleri başarılıdır.

Önceki geliştirme credential'larının Git geçmişinde bulunabileceği uyarısı
[7 Ekim raporunda](github-readiness.md) korunmaktadır. Kullanılan eski değerler
varsa rotasyon ayrıca gerekir; bu kontrol hesap/parola veya Git geçmişi değiştirmedi.
Lisans seçimi ve canlı dağıtım bu yayının kapsamında değildir.
