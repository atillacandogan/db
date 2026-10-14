# Yetki yönetimi — ilk geliştirme paketi

Tarih: 2026-10-07. Kapsam: etkin yetki önizlemesi, değişiklik geçmişi ve ekip detayları.
Geçici izin, izin şablonu, toplu üyelik, e-posta akışı ve yeni SaaS tenant provision'u
bu paketin kapsamında değildir.

## Kullanım

`/admin/resource-access` sayfasında ekip adına tıklanınca detay sayfası açılır.
Üyelikler gerçek Keycloak kullanıcı ID'siyle tutulur; kullanıcı araması isim ve ID
seçimine yardımcı olur. Detay sayfasında ekip adı değiştirilebilir, üyelik/izin
kaldırılabilir ve ekip arşivlenebilir. Kalıcı ekip silme eklenmedi.

Yetki önizlemesinde gerçek kullanıcı ve hedef seçilir. Okuma, tarama, hedef yönetimi
ve dışa aktarma için izin/red nedeni ile ilgili ekip/izin kaynakları gösterilir.
Üye satırındaki “Yetkilerini incele” bağlantısı aynı kullanıcıyı önizlemeye taşır.
İşlem geçmişi ekip ve işlem türüne göre filtrelenir; sayfa başına 20 kayıt gösterilir.

## Yetki nasıl hesaplanıyor?

1. Kullanıcı Keycloak'tan tekrar okunur; tarayıcının gönderdiği rol kabul edilmez.
2. Hedefin projesi ve kullanıcı üyelikleriyle eşleşen proje/hedef izinleri bulunur.
3. Arşivli ekipler hesap dışında bırakılır; önizlemede kaynak olarak açıklanır.
4. Okuma içeren etkin izinler birleştirilir ve rol üst sınırıyla kesiştirilir.
   Viewer yalnızca okuma/dışa aktarma, operator dört işlemi yapabilir. Yönetici kendi
   kurumunda ekip iznine ihtiyaç duymaz. Devre dışı hesap önizlemede erişim alamaz.

Ortak `TargetAccessPolicy`, middleware rol üst sınırı ve önizlemede kullanılır.
Asıl erişim denetimi API/EF sorgu filtrelerinde devam eder; önizleme izin veren bir
token veya ayrı yetkilendirme mekanizması değildir.

Önizleme Keycloak'ın güncel rol/hesap durumunu kullanır. Halihazırda verilmiş JWT'nin
rolü token yenilenene kadar farklı olabilir; bu paket oturum/token iptali eklemez.
Ekip üyeliği/izin/arşiv değişikliği ise yeni veri sorgularında veritabanından kontrol
edilir. Arşivleme kuyruğa alınmış veya başlamış taramaları otomatik iptal etmez.

Dedicated tenant modunda tenant kapsamlı kimlik adaptörü bulunmadığı için shared
realm kullanıcı araması ve önizleme fail-closed olarak 403 döner. Ekip/izin/geçmiş
verisi mevcut ayrı tenant veritabanı ve admin sınırlarında kalır. Yeni uç noktalar
normal operator/viewer'a açık değildir; tenant claim kontrolü admin için de sürer.

## Arşivleme ve geçmişin tutarlılığı

Arşivleme `AccessTeams.IsArchived` alanını değiştirir. Üyelikler ve izinler korunur,
aktif ekip sorguları bunları erişim hesabına katmaz. Yeniden etkinleştirme mevcut
izinleri tekrar geçerli kılar; arayüz bu etkiyi açıklayan ikinci onay adımı gösterir.
Arşivli ekibe yeni üye veya izin ekleme 409 ile reddedilir. Ekip adı ve arşiv alanı
eşzamanlı yazmalara karşı EF concurrency token'dır; çatışma 409 döner.

`resource_access_audits`: doğrulanmış aktör subject/username, UTC zaman, işlem,
nesne/ekip kimliği, izin verilen alanlardan önce/sonra JSON snapshot'ı saklar.
Parola, secret, token, kullanıcı e-postası ve hedef credential'ı kaydedilmez.
Proje/ekip oluşturma, ad değişikliği, arşiv/restore, üyelik ekleme/kaldırma,
izin oluşturma/güncelleme/kaldırma ve hedef proje taşıma kaydedilir.

Değişiklik ve audit aynı transaction'da kaydedilir. Audit insert başarısızsa asıl
değişiklik de commit edilmez. Hedef taşınırken silinen hedefe özel izinler ve proje
değişimi mevcut kilitli transaction'a dahil edilir. Aynı değeri tekrar kaydetmek,
zaten var olan üyeyi eklemek veya olmayan kaydı kaldırmak ikinci audit oluşturmaz.
Başarısız denemelerin güvenlik log'u ve geçmiş kayıtların backfill'i eklenmedi.

EF write guard audit update/delete'i sistem bağlamında dahi reddeder. PostgreSQL
trigger'ları UPDATE/DELETE/TRUNCATE'i reddeder. Bu, DB sahibi/superuser'ın DDL ile
korumayı kaldırmasına karşı değiştirilemezlik garantisi değildir. Üretimde kısıtlı
uygulama DB rolü, ayrı dağıtım hesabı ve harici audit saklama ayrıca gerekir.

## Migration ve yerel veriler

Migration: `20261007122750_AddResourceAccessAuditAndTeamArchive`. Yalnızca arşiv alanı,
audit tablosu, indeksler ve append-only trigger'ları ekler. Demo seed delete/reinsert,
hedef veya tarama temizliği içermez. Yerel migration uygulandı: 6 hedef ve mevcut
tek ekip korundu; 0 scan, 0 üyelik ve 0 izin sayıları değişmedi. Ekip `deneme1` etkin
kaldı. Canlı üyelik/izin veya test kullanıcısı oluşturulmadı.

Migration öncesi yedek:
`/private/tmp/dbscanner-access-insights-backup.I33RAM/dbscanner-before-access-insights.dump`.
Özel izinli geçici dizinde saklandı; `pg_restore --list` başarılı (113 TOC girdisi).
Tam restore provası yapılmadı. OS geçici dosyaları temizleyebilir; bu kalıcı üretim
yedeği değildir. Migration Down yerelde çalıştırılmadı; audit geçmişini siler.

## Doğrulama

- Altyapı regresyonu: 167 başarılı, 0 başarısız, 1 atlandı. Atlanan mevcut test host
  Nmap gerektiriyor. Gerçek PostgreSQL üzerinde arşiv erişim iptali/restore, audit
  insert hatasında transaction rollback ve SQL audit update/delete/truncate reddi
  doğrulandı. Önceki tenant/outbox/lease testleri de geçti.
- Application/API: 190 başarılı, 0 başarısız. Rol sınırı, arşiv/restore, çoklu ekip
  izinleri, admin-only yüzeyler, Keycloak arızası/tenant fail-closed, snapshot'lar,
  üyelik kaldırma, audit filtreleme/sayfalama ve idempotent değişiklikler test edildi.
- Toplam 357 başarılı test, 0 başarısız, 1 atlanan test.
- Web lint ve production build başarılı; yeni ekip detay route'u build çıktısında.
- Solution build başarılı: 0 hata, mevcut paketlerden 15 güvenlik uyarısı.
  Microsoft.OpenApi, SSH.NET ve MessagePack sürümleri bu pakette güncellenmedi.
- EF `has-pending-model-changes`: model ve migration snapshot'ı eşleşiyor.
- Canlı API'nin yeni uç noktaları yüklemesi için Aspire `api` kaynağının yeniden
  başlaması gerekiyor. Oturumlu son görsel kontrol tamamlanmadı: Mac kilitli ve
  uygulama içi tarayıcıda açık sekme bulunmuyor. Test başarısı canlı tarayıcı
  doğrulaması yapılmış olarak sunulmadı.
