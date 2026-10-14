# Ekip, proje, hedef yetkisi ve kurum izolasyonu

Sürüm: `stage-15-resource-access`.

## Kullanım

Web menüsünde **Teams and Target Permissions** (`/admin/resource-access`):

1. Proje ve ekip oluşturun.
2. Ekibin **Detaylar** sayfasını açıp Keycloak kullanıcı aramasından gerçek kullanıcıyı seçerek ekibe ekleyin.
   Sonuçlarda ad, kullanıcı adı ve değişmez Keycloak ID / OIDC `sub` görünür;
   tam UUID ile arama da desteklenir. Kullanıcı adı/e-posta yetki anahtarı değildir.
   Kullanıcının ayrıca mevcut
   `dbscanner-viewer`, `dbscanner-operator` veya `dbscanner-admin` rolü olmalıdır.
3. Projenin **Detaylar** sayfasında mevcut hedefi bağlayın veya bağlı hedefi başka projeye taşıyın. Mevcut/yeni hedefler başlangıçta varsayılan projededir.
4. Ekip detayındaki **Ekip izinleri** bölümünden bütün proje veya yalnızca bir hedef için izin verin; mevcut iznin işlemlerini düzenleyin veya izni kaldırın.

Ana ekran yalnızca proje/ekip oluşturma ve ayrı kayıt listelerini içerir. Ekip seçimi
gerektiren ortak alt formlar yoktur: üyeler ve izinler seçili ekibe, hedefler seçili
projeye aittir. Önizleme ve değişiklik geçmişi ekip detayında açılır bölümlerdedir.
İzin düzenlerken kapsam sabittir; kapsam değişikliği için eski izin kaldırılıp yeni
izin eklenir. Arşivlenmiş ekibe yeni izin/üye eklenemez; mevcut izin kaldırılabilir.

### Proje ayrıntıları, düzenleme ve silme

Kayıtlı projelerde **Detaylar** bağlantısı `/admin/resource-access/projects/{id}`
sayfasını açar. Projenin adı, bağlı hedefleri ve ekip izinleri ayrı bölümlerde
gösterilir. Hedef taşımada proje seçmek tek başına işlem yapmaz; **Taşı** veya
**Bu projeye bağla** düğmesiyle açık işlem gerekir. Taşıma hedefe özel eski izinleri
kaldırır, tarama geçmişini korur; aktif tarama varsa API reddeder. İzin yönetimi
bağlantıları ilgili ekibin detayına gider. Yönetici proje adını değiştirebilir; bu işlem hedefleri veya tarama
geçmişini değiştirmez. Başarılı değişiklikler denetim geçmişine kaydedilir.

Silme için proje adıyla açık onay gerekir. Varsayılan proje silinemez. Diğer
projelerde de önce bağlı hedefler başka bir projeye taşınmalı ve proje/hedef
izinleri kaldırılmalıdır. API bu kuralları ayrıca uygular; yalnızca boş ve izinsiz
bir proje silinebilir. Hedefler, taramalar veya izinler otomatik silinmez.

Yalnızca kurum yöneticisine açık proje API'si:

- `GET /api/access/projects/{id}`: proje ayrıntıları ve bağlı kayıtlar.
- `PUT /api/access/projects/{id}`: `{ "name": "Yeni proje adı" }`; kırpılmış ad
  1–160 karakter olmalıdır.
- `DELETE /api/access/projects/{id}`: başarılı silmede `204`; varsayılan proje,
  bağlı hedef veya izin varsa `409`. Olmayan proje için silme tekrarı `204` döner.
- `GET /api/access/audit?entityType=project&entityId={id}`: yalnızca bu projenin
  oluşturma, ad değiştirme ve silme kayıtları. İzin değişiklikleri kendi izin
  kaydı altında genel/ekip denetim geçmişinde gösterilir.

Doğrulama (2026-10-12): Application/API 211/211, Web 43/43 başarılı; iki
PostgreSQL kilit/foreign-key yarış testi izole test container'ında geçti. Release
solution build, Web lint ve production build başarılı. Gerçek bileşenler ve CSS
ile test verisi kullanılan 1280 px masaüstü/390 px mobil görsel önizlemede sayfa
yatay taşması görülmedi. Bu kontrol canlı veride düzenleme/silme veya canlı
oturumla uçtan uca mutasyon testi değildir. Migration eklenmedi; canlı API
yeniden başlatılmadı. Güncel backend derlemesi çalıştırılmadan yeni işlemler
canlı instance'a geçmez.

`GET /api/access/users?search=...&page=1` yalnızca admin için Keycloak dizininden
10'lu sayfalar döndürür. E-posta, parola veya MFA bilgisi bu yanıta dahil edilmez.
Servis hesapları kullanıcı olarak listelenmez. `PUT /api/access/teams/{id}/members`
gönderilen ID'yi Keycloak'tan yeniden doğrular: bulunamayan/silinmiş kullanıcı
404, devre dışı kullanıcı 409, kimlik servisi erişilemiyorsa 503 döner ve üyelik
yazılmaz. Ekip üyeliği uygulama rolü atamaz. Mevcut üyelikler otomatik silinmez.

Kimlik adaptörü şu anda realm genelinde çalıştığından `DedicatedTenant=true`
kurulumlarında bu yeni arama ve üyelik ekleme 403 ile kapalıdır; kurum kapsamlı
kimlik dizini uygulanmadan başka kurumların kullanıcıları açığa çıkarılmaz.
Mevcut üyelik/izin görüntüleme ve iptal etme akışı korunur.

### Grid ve kullanıcı seçimi doğrulaması (2026-10-07)

- Application/API: 162 başarılı, 0 başarısız; yeni kimlik endpoint testleri 16/16.
- Keycloak adaptörü: 6/6 başarılı; servis hesaplarını dışlama ve gerçek ID koruma dahil.
- Web ESLint, TypeScript ve production build başarılı.
- Oturum açılmış canlı sayfada 1440 px grid hizalaması ve 390 px tek sütun
  yerleşimi kontrol edildi; her iki genişlikte sayfada yatay taşma yok.
- Veritabanı modeli veya migration değiştirilmedi; canlı test üyeliği/izin eklenmedi.
- Canlı API eski derlemeyle çalıştığı için yeni arama adresi henüz 404 döndü;
  canlı Keycloak araması kontrolü için API'nin yeniden başlaması gerekiyor.

| İzin | Bit | Viewer | Operator | Admin |
| --- | --- | --- | --- | --- |
| Read | 1 | Ekip izni gerekir | Ekip izni gerekir | Kurum içindeki tüm hedefler |
| Scan / cancel | 2 | Yapamaz | Ekip izni gerekir | Yapabilir |
| Manage / hedef düzenleme | 4 | Yapamaz | Ekip izni gerekir | Yapabilir |
| Export | 8 | Ekip izni gerekir | Ekip izni gerekir | Yapabilir |

Read diğer izinlerin ön koşuludur. Grant API sayısal bit birleşimi alır:
Read+Scan = 3; Read+Export = 9; bütün izinler = 15. Birden fazla ekip/grant
**eklemeli** çalışır: hedefe özel bir grant, proje genelindeki izni azaltmaz.
Silme, yeni hedef oluşturma, keşif/ağ envanteri, katalog ve kullanıcı yönetimi
kurum yöneticisiyle sınırlıdır. Operator rolü korunur ancak ekip izni yoksa hedef
göremez/tarayamaz. Otomatik geniş erişimli bir "herkes" ekibi oluşturulmaz.

### Proje/ekip kapsamlı ekran doğrulaması (2026-10-12)

- Web: 56/56 test başarılı; ESLint ve TypeScript dahil production build başarılı.
- Yeni regresyonlar: ana ekranda yalnızca iki kart; ekip sabit izin oluşturma/
  düzenleme/kaldırma; silinen proje, taşınan hedef ve değişen izin için eski seçim
  engeli; arşivlenmiş ekipte yeni yetki engeli; açık düğmeyle hedef taşıma/bağlama;
  etkilenen cache'lerin yenilenmesi ve API hatalarının korunması.
- Gerçek TSX/CSS, test verisiyle 1280 px masaüstü ve 390 px mobil tarayıcıda
  incelendi; ana ekran, ekip izin formu ve proje hedef alanında sayfa yatay taşması
  yok. Bu görsel kontrol oturum açılmış canlı mutasyon testi değildir.
- API, veritabanı ve migration değiştirilmedi. Canlı üyelik, izin ve hedeflere
  dokunulmadı; AppHost/container'lar yeniden başlatılmadı.

Üyelik/izin kaldırılması yeni API sorgularında token süresinin dolmasını beklemeden
etkili olur. Önceden okunmuş/indirilmiş veriyi geri almak mümkün değildir. Başlamış
bir tarama kullanıcı izni kaldırıldı diye otomatik iptal edilmez; yetkili yönetici
iptal eder. Worker'ın yetkisi kullanıcı token'ından değil, kendi kurumu için
onaylanmış kalıcı işten gelir.

Proje taşıma hedefe özel eski grant'leri kaldırır; geçmiş taramalar/bulgular hedefle
birlikte yeni projede görünür. Aktif iş varken taşıma reddedilir. Tarama oluşturma
ve proje taşıma aynı hedef satırını kilitler; kontrol ile kayıt arasında taşıma
yarışı önlenir. Kurumlar arası hedef taşıma bu API'nin parçası değildir.

## İstekten rapora politika

- JWT önce mevcut issuer/audience doğrulamasından geçer. Middleware OIDC `sub`,
  roller ve varsa kurum claim'ini doğrular. `X-Tenant-Id` gibi başlıklar otorite değildir.
- Scoped `ResourceAccessContext` varsayılan olarak erişimi reddeder. Sistem erişimi
  yalnızca Worker kayıtlarında/design-time factory ve açık test düzeneklerinde verilir.
- EF filtreleri Target, Scan, Finding, ScanScriptResult ve outbox için ilişki üzerinden
  uygulanır. Liste, doğrudan ID, nested sonuçlar, dashboard ve rapor aynı sınıra tabidir.
- Normal kullanıcı `SaveChanges` ile yetkisiz hedef ekleyemez/değiştiremez, grant veya
  ekip üyeliğini değiştirerek kendine yetki veremez. Bulk scan sorguları da filtrelidir.
- Bulgu export sorgusu Read+Export kullanır; izin olmayan hedefler çıktıya dahil edilmez.
  `/api/scans/{id}/export` izin olmayan/olmayan kayda 404 döner. Web PDF/Excel/JSON
  export'u bu endpoint'ten taze veri alır, detay cache'ini izin kontrolü yerine kullanmaz.
  Okuma izni olan kişinin ekrandaki veriyi elle kopyalamasını teknik olarak önlemez.
- Worker outbox/lease bakım işlemleri açık sistem bağlamı gerektirir. Kullanıcı HTTP
  isteği bu sistem bağlamını oluşturamaz.

## Çok kurumlu dağıtım modeli: kurum başına ayrı veri düzlemi

Bu değişiklik **ortak tabloda TenantId + RLS** modeli kullanmaz. API, Worker,
veritabanı bağlantısı, şifreleme anahtarı ve broker vhost'u kurum başına ayrılır.
Her API/Worker instance yalnızca tek kurum için çalışır. Aynı sunucuda birden fazla
instance olabilir; yönlendirme/TLS/dağıtım operasyonu ayrıca kurulmalıdır.

API ve Worker'a aynı ayarları verin (secret değerlerini repoya koymayın):

```json
{
  "TenantIsolation": {
    "TenantId": "customer-a",
    "DedicatedTenant": true,
    "ClaimName": "dbscanner_tenant"
  },
  "Persistence": { "ApplyMigrations": false }
}
```

Üretim ön koşulları:

1. Kuruma özel PostgreSQL veritabanı ve yalnızca bu veritabanının tablolarına yetkili
   login provision edin. Login superuser/BYPASSRLS olmamalıdır. PUBLIC/role inheritance
   yetkilerini ve başka veritabanlarına erişimi operasyon seviyesinde denetleyin;
   uygulama bütün cluster ACL'lerini provision etmez veya kanıtlamaz.
2. Migration'ı dağıtım hesabıyla offline uygulayın. Demo hedeflerini devre dışı bırakın,
   `DemoTarget` scan scope kayıtlarını kapatın; yalnızca kurumun onaylı scope'unu
   platform yönetimi provision etsin. Dedicated startup aktif demo scope'u reddeder.
3. RabbitMQ'da `tenant.customer-a` vhost'u ve sadece bu vhost'a izinli login provision
   edin. Broker connection string'i bu vhost'a bağlanmalıdır. Root vhost/guest
   dedicated modda reddedilir. Altı kuyruk türü otomatik `tenant.customer-a.` ön eki alır.
4. JWT'de imzalı `dbscanner_tenant=customer-a` claim'i sağlayın. Realm/client özel sabit
   claim veya platform tarafından yönetilen üyelik kullanın; kullanıcının değiştirebildiği
   profil alanı bu claim'in kaynağı olmamalıdır. Issuer/audience, HTTPS metadata ve token
   güven politikaları üretime uygun yapılandırılmalıdır. Eksik/farklı/çoklu claim, admin
   dahil tüm veri API'lerinde reddedilir. Keycloak üzerinde mapper provision edilmesi
   bu yerel POC değişikliğinde otomatik yapılmadı.
5. Kuruma özel credential-protection anahtarı, worker egress/network policy, yedek
   saklama alanı ve log/metrics erişimi ayırın. TLS ve secret yönetimini sağlayın.
   API anahtarları/vhost login'leri müşteriye verilmemelidir.

`deployment_tenant` veritabanını bir kuruma bağlar. Farklı kurum aynı veritabanını
kullanmaya çalışırsa startup ve **migration öncesi** kontrol reddeder. Marker silmek/
yeniden adlandırmak güvenli tenant taşıma yöntemi değildir. Yeni kurum için yeni
veritabanı gerekir. Marker ilk provision sırasında yazılır; doğru bağlantının
seçilmesi ve migration hesabı platformun güven sınırıdır.

Yeni scan/discovery/NSE-sync request/response mesajları kurum kimliği taşır.
Yanlış kurum mesajı ACK ile çalıştırılmaz; consumer reject ile dead-letter eder.
Outbox transport da farklı kurum snapshot'ını publish etmez. Kuyruk prefix'i tek
başına broker güvenliği değildir: ayrı vhost/login ve kısıtlı ACL gerekir. Eski
kurumsuz mesajlar yalnızca yerel tek-kurum modunda okunur, dedicated modda reddedilir.

## Bilerek kapalı/kalan işler

- Demo AppHost tek kurum içindir; dedicated mod ayarlanırsa başlamaz. Üretim tenant
  provision'u için ayrı API/Worker dağıtımı gerekir; bu görev canlı SaaS altyapısı açmadı.
- Shared-realm Keycloak admin servisi bütün kullanıcıları görebildiği için dedicated
  modda `/api/admin/users` yüzeyi kapalıdır. Kullanıcı provision'u harici kimlik
  sağlayıcısındadır; Web aynı kısıtı açıklayıp ekip ekranına yönlendirir. Tenant kapsamlı
  kullanıcı provision/rol API'si ayrıca tasarlanmalıdır.
- Keşif ve ağ scope'u henüz proje bazlı değildir; normal operator/viewer için kapalıdır.
  Dedicated tenant yöneticisi scope genişletemez veya platformun yerel ağını keşfedemez.
- EF filtreleri ve write guard, PostgreSQL proje/ekip RLS'si değildir. DB credential'ı
  olan biri kurum içi tüm tablolara erişebilir; kurumlar arası bariyer ayrı DB/login'dir.
- Kota/rate limit, tenant onboarding/offboarding otomasyonu, RLS'li shared DB ve
  harici object storage bu değişiklikte eklenmedi. Proje/ekip değişiklik geçmişi
  2026-10-07'de eklendi; ayrıntılar [yetki yönetimi ilk paketi](resource-access-insights.md)
  raporunda. İleride eklenen yüzeyler aynı politikayı korumalıdır.
- Bilinen paket güvenlik uyarıları bu işte güncellenmedi; bu sonuç "üretime tamamen
  hazır SaaS" sertifikası değildir.

## Migration ve testler

Migration: `20261006121312_AddProjectAndTeamTargetAccess`. Mevcut target/scan/finding
silinmez; ProjectId mevcut hedeflere varsayılan proje olarak eklenir. EF'in otomatik
ürettiği demo-target delete/reinsert işlemleri çıkarıldı; kullanıcı değişiklikleri
ve cascade ilişkili tarama geçmişi korunur. API, Worker ve Web birlikte güncellenmelidir.

Testler `ResourceAccessMiddlewareTests`, `ResourceAccessTests` ve
`ResourceAccessIntegrationTests` içinde: default-deny, project/target grant, rol üst
sınırı, üyelik iptali, model-cache bağımsızlığı, export ayrımı, negatif save guard,
başlık/claim spoofing, admin cross-tenant reddi, PostgreSQL composite FK, iki ayrı DB'de
aynı ID izolasyonu, DB binding, superuser startup reddi, broker yanlış-kurum DLQ,
migration veri koruması ve private-vhost + normal DB login ile gerçek scan completion.
Önceki outbox/heartbeat/lease/replay testleri de regresyon kapsamındadır.

Tasarım rehberi: [OWASP Multi-Tenant Security](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html).

### Doğrulama sonucu (2026-10-06)

- Application/API: **146 başarılı**, 0 başarısız.
- Infrastructure: **161 başarılı**, 0 başarısız, 1 atlandı. Son ek SQL kilit testiyle
  `ResourceAccessIntegrationTests` ayrıca tekrar çalıştırıldı: 8/8 başarılı.
- Toplam **307 başarılı**, 0 başarısız. Atlanan test host Nmap gerektiren mevcut
  SQL Server/Nmap testidir. Worker'ın 605 NSE dosyasıyla katalog sync'i başarılı.
- Web lint ve production build başarılı; yeni `/admin/resource-access` route'u
  build çıktısında mevcut. Solution build: 0 hata, mevcut bağımlılıklardan 15 uyarı.
- EF `has-pending-model-changes`: model/snapshot farkı yok.
- İlk tam regresyonda InMemory testin ilişkisiz legacy-result fixture'ı filtreye
  takıldı; açık sistem bağlamının tenant içi bakım sorguları filtre dışında tutularak
  düzeltildi. Aynı çalıştırmada consumer başlangıcında bir timeout yaşandı; tekrar
  tam paket tek altyapı test süreciyle çalıştırıldığında bütün testler geçti.
- Canlı yerel migration uygulandı; 6 hedef, 0 scan, 0 finding korundu; kurum binding
  `local`. Canlı test kullanıcısı/grant/ekip veya yeni tarama oluşturulmadı.
- Migration öncesi yedek:
  `/private/tmp/dbscanner-access-backup.irna7u/dbscanner-before-resource-access.dump`.
  Dizin 0700, dosya 0600; pg_restore listelemesi başarılı, tam restore provası yapılmadı.
  Geçici dizin OS temizliğine tabi olduğundan uzun süreli yedek alanı değildir.
- Web route HTTP 200; tarayıcı Keycloak login'e yönlendi. Oturum sonrası canlı görsel
  kontrol kullanıcı girişini bekliyor; UI kontrolü tamamlandı diye raporlanmadı.
- Son yerel instance: API `stage-15-resource-access`, Worker `scanner-worker-hvwewdkd`.
  Önceki Worker'lar durduruldu; tek aktif scan consumer bırakıldı. Mevcut Keycloak/
  uygulama veritabanı volume'ları ve secret'lar korundu; container/volume temizliği yapılmadı.
