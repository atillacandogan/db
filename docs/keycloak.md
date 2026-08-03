# Keycloak kimlik doğrulama

DBScanner geliştirme ortamı, `quay.io/keycloak/keycloak:26.7.0` image'ını Aspire
içinde çalıştırır. Realm tanımı ilk açılışta
`docker/keycloak/import/dbscanner-realm.json` dosyasından içe aktarılır ve Keycloak
verisi `dbscanner-keycloak-data` Docker volume'ünde saklanır.

Yerel adresler:

- DBScanner: `http://localhost:3000`
- Keycloak: `http://localhost:18080`
- Keycloak yönetim konsolu: `http://localhost:18080/admin`

İlk uygulama girişi için kullanıcı adı `dbscanner-admin`, geçici parola
`ChangeMe_DbScanner_2026!` değeridir. Keycloak ilk girişte bu parolanın
değiştirilmesini zorunlu tutar. Keycloak yönetim kullanıcısının parolası kaynak
koda yazılmaz; AppHost User Secrets içindeki
`Parameters:keycloak-admin-password` anahtarından alınır.

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
