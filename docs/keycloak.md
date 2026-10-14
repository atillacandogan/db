# Keycloak kimlik doğrulama

DBScanner geliştirme ortamı, `quay.io/keycloak/keycloak:26.7.0` image'ını Aspire
içinde çalıştırır. Realm tanımı ilk açılışta
`docker/keycloak/import/dbscanner-realm.json` dosyasından içe aktarılır ve Keycloak
verisi `dbscanner-keycloak-postgres-data` Docker volume'ünde saklanır.

Yerel adresler:

- DBScanner: `http://localhost:3000`
- Keycloak: `http://localhost:18080`
- Keycloak yönetim konsolu: `http://localhost:18080/admin`

İlk uygulama girişi için kullanıcı adı `dbscanner-admin` olur. Geçici parola
kurulum scripti tarafından rastgele üretilir; AppHost User Secrets içindeki
`Parameters:dbscanner-initial-admin-password` anahtarında saklanır. Import dosyası
`${DBSCANNER_INITIAL_ADMIN_PASSWORD}` ortam değişkeni yer tutucusunu kullanır.
Keycloak ilk girişte bu parolanın değiştirilmesini zorunlu tutar. Keycloak yönetim
konsolunun `admin` kullanıcısı ayrı bir hesaptır; parolası
`Parameters:keycloak-admin-password` anahtarından alınır. Parolaları yalnızca kendi
yerel User Secrets yöneticinizden okuyun; terminal çıktısını/loglarını paylaşmayın.

Mevcut realm startup import'unda yeniden yazılmaz. Bu nedenle scripti tekrar
çalıştırmak veya yeni import dosyasını indirmek var olan kullanıcı parolasını
değiştirmez. Önceki sabit başlangıç parolasını hâlâ kullanıyorsanız Keycloak'tan
değiştirin; eski Git commit'leri kaynak koddan kaldırılan değeri içerebilir.

## Roller

- `dbscanner-viewer`: target, scan, finding, rule ve NSE kayıtlarını görüntüler.
- `dbscanner-operator`: viewer haklarına ek olarak target oluşturur/günceller,
  scan başlatır ve çalışan scan'i iptal eder.
- `dbscanner-admin`: operator haklarına ek olarak katalog kayıtlarını yönetir ve
  target/scan kayıtlarını silebilir.

Roller Keycloak realm rolü olarak access token içindeki `roles` claim'ine eklenir.
Token ayrıca `dbscanner-api` audience değerini taşır. API issuer, imza, süre,
audience ve rol doğrulamasını standart JWT bearer middleware'i ile yapar.

## Tarayıcı akışı

Web istemcisi Authorization Code + PKCE (`S256`) kullanır. Access token yalnızca
Keycloak adaptörünün belleğinde tutulur; localStorage veya sessionStorage'a
yazılmaz. API çağrısından önce süresi yaklaşan token yenilenir ve
`Authorization: Bearer ...` başlığıyla gönderilir.

## Uzak ortam

Mevcut realm import'u yalnızca yerel geliştirme adreslerine izin verir. Uzak
ortamda `start-dev` kullanılmamalı; HTTPS arkasında kalıcı bir Keycloak kurulumu
yapılmalı ve aşağıdaki değerler gerçek alan adlarıyla değiştirilmelidir:

- Keycloak client redirect URI ve web origin değerleri
- `Authentication__Authority`
- `NEXT_PUBLIC_KEYCLOAK_URL`
- API CORS allowed origins

Kurumsal hesapla giriş istenirse Keycloak realm'ine şirketin OpenID Connect veya
SAML identity provider'ı eklenebilir. Kullanıcı rolleri yine DBScanner realm
rolleri üzerinden yönetilir.

## Uygulama içi kullanıcı yönetimi

`/admin/users` sayfası yalnızca `dbscanner-admin` rolüne açıktır. Web istemcisi
Keycloak Admin API'ye doğrudan bağlanmaz. DBScanner API, User Secrets içindeki
`Parameters:keycloak-admin-service-client-secret` değeriyle
`dbscanner-admin-service` service account'unu kullanır. Bu hesapta yalnızca
`query-users`, `view-users`, `manage-users` ve `view-realm` realm-management
rolleri bulunur. `view-realm`, kullanıcı oluşturulurken atanacak DBScanner realm rolünün
kimliğini okuyabilmek için gereklidir; realm ayarlarını değiştirme yetkisi vermez.

Yeni geliştirme ortamında secret'ı üretmek için normal kurulum scriptini tekrar
çalıştırın. Realm daha önce kalıcı Keycloak veritabanına import edildiyse startup
import'u mevcut realm'i değiştirmez; service-account client'ını yönetim
konsolundan aynı rollerle ekleyin veya yalnızca disposable geliştirme ortamında
Keycloak verisini yeniden oluşturun.

POC ortamında yönetici kullanıcıyı geçici parolayla oluşturur. Parola Keycloak'a
`temporary=true` olarak gönderilir; kullanıcı ilk girişinde parolayı değiştirmek
zorundadır. İsteğe bağlı MFA seçildiğinde `CONFIGURE_TOTP` işlemi de ilk girişe
eklenir. Bu akış e-posta adresi toplamaz, SMTP kullanmaz ve e-posta doğrulama
işlemi başlatmaz.

Üretim için e-posta gönderme altyapısı korunur. Parola yenileme ve gelecekteki
davet akışları Keycloak `execute-actions-email` mekanizmasını kullanabilir; bunun
için uzak ortamda Keycloak realm SMTP ayarları ayrıca yapılandırılmalıdır.
