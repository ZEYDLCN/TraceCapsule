# Uçtan uca test raporu — 21 Eylül 2026

## Sonraki doğrulama: test üretimi ve otomatik yayınlama

JSON karşılaştırması, HTTP replay ortamı ve test üretimi eklendikten sonra bulunan üç hata
düzeltildi: üretilen testte charset ayrıştırması, HTTP kodu aynı kalan düzeltmede çelişen
assertion ve eşdeğer JSON string kaçışlarının farklı sayılması.

Son Release çalıştırması: **144/144 başarılı** (75 unit, 24 integration, 45 replay; atlanan yok).
Kalıcı test, generator'ın ürettiği C# dosyalarını ayrı bir projede derliyor ve gerçek yerel
HTTP bağlantılarıyla çalıştırıyor. UTF-8, tırnaklı ISO-8859-1 charset, aynı/değişen HTTP kodu,
Unicode kaçışları, değişken alanları hariç tutma ve yanlış yanıt geri geldiğinde testin
başarısız olması doğrulandı.

`0.1.0-beta.2` sürüm geçersiz kılmasıyla testler tekrar geçti; altı paket ve sembolleri
oluşturuldu, paket sürümleri ve iç bağımlılıkları doğrulandı. `actionlint 1.7.12` iki workflow'u
hatasız doğruladı. Geçerli/geçersiz etiketler ve yanlış paket sürümünün reddedilmesi kontrol
edildi. Son çıktılar `TestResults/automation-fixes/` ve `TestResults/release-preflight/` altında.

GitHub OIDC yayını uzaktan çalıştırılmadı; [yayınlama rehberindeki](publishing.md) tek seferlik
hesap bağlantısı ve kullanıcının commit/tag push işlemi gerekiyor. Bu doğrulamada yayın yapılmadı.

## İlk uçtan uca test turu

Windows 10 üzerinde .NET SDK 9.0.300, .NET 8.0.16 runtime ve Release yapılandırmasıyla çalıştırıldı. Commit veya push yapılmadı.

| Test grubu | İlk durum | Son durum | Başarısız / atlanan |
| --- | ---: | ---: | ---: |
| UnitTests | 58 | 58 | 0 / 0 |
| IntegrationTests | 6 | 24 | 0 / 0 |
| ReplayTests | 13 | 27 | 0 / 0 |
| Toplam | 77 | 109 | 0 / 0 |

32 yeni test vakası eklendi. Aşağıdaki gerçek paket testi bu 109 teste ek olarak çalıştırıldı.

## Çalıştırılan senaryolar

- Gerçek Kestrel HTTP sunucusunda kayıt → ZIP kapsülünü okuma → replay → karşılaştırma.
- CLI'yi ayrı süreç olarak çalıştırarak inspect, HTML, export, replay, compare ve merge; boşluk içeren dosya yolları.
- 30 eşzamanlı HTTP isteğinde trace/session/gövde ayrımı ve hassas alanların maskelenmesi.
- Başarılı isteğin örnekleme dışında bırakılması; 400/404/500/503 durumları; elle yakalama; gecikme eşiği; attribute zorunluluğu; maskelemenin açıkça kapatılması.
- Boş, Unicode, dizi, iç içe JSON, binary ve limitten büyük gövdeler; istemciye dönen gövdenin korunması.
- Dış HTTP istek/yanıt başlıkları ve gövdeleri ile büyük kuyruk mesajlarında maskeleme.
- Kayıt klasörüne yazılamadığında uygulama yanıtının çalışmaya devam etmesi.
- Charset içeren content type; query string ve fault-injection başlığının replay sırasında iletilmesi.
- Yanlış CLI argümanları, bulunamayan/bozuk kapsül, metadata'sız ZIP, bulunamayan trace/session ve karşılaştırma farkında hata çıkış kodu.
- Tekrar merge işlemi ve giriş servisinin yanıtının korunması.
- Mevcut üç servisli demo: başarılı transfer ve kasıtlı ödeme timeout'u; tek servis kapsülündeki zaman/UUID değerlerinin yeniden üretilmesi.
- Mevcut kuyruk emülatörü, HTTP dependency replay/fault injection, YAML/JSON policy, HTML escaping ve ZIP round-trip testleri.

## Testlerle doğrulanan ve düzeltilen hatalar

1. `application/json; charset=utf-8` gibi content type değerleri replay sırasında `FormatException` oluşturuyordu. Media type artık parametreleriyle ayrıştırılıyor.
2. JSON gövdesi maskelenmeden önce kesildiğinden büyük gövdelerde parola açık kalıyordu. Maskeleme kesmeden önce uygulanıyor.
3. Middleware dış HTTP ve kuyruk kayıtlarını host'un redaction policy'sinden geçirmeden saklıyordu. Bu kayıtların başlıkları ve gövdeleri de artık policy ve boyut sınırından geçiyor. Kuyruk JSON'u bu aşamaya kadar tam tutuluyor.
4. Canlı replay yanıtındaki hassas JSON alanları açık saklanıyordu. Replay yanıtına varsayılan maskeleme politikası uygulanıyor.
5. Merge, giriş servisinin isteğine başka servisin yanıtını ekliyordu. Giriş servisinin yanıtı korunuyor.
6. Tekrar merge, önceki birleşik çıktıyı da girdi olarak alıp kayıtları çoğaltabiliyordu. Yeni birleşik çıktılar metadata ile işaretlenerek sonraki CLI merge'lerinde atlanıyor.
7. Metadata'sız ZIP dosyası geçerli kapsül sayılıyordu. Okuyucu artık dosyayı reddediyor.

CI dal filtresine mevcut `master` dalı da eklendi. GitHub Actions uzak ortamda çalıştırılmadı.

## Paket doğrulaması

`dotnet pack` ile altı `.nupkg` ve altı sembol paketi üretildi. Sample projelerinin paketlenmemesiyle ilgili beklenen iki uyarı dışında hata olmadı.

CLI yalnızca yerel paket kaynağından `tool-test/e2e-validation` klasörüne kuruldu. Çalışan SimpleApi'ye gerçek POST isteği gönderildi; üretilen kapsül kurulan CLI ile inspect edildi, HTML raporu üretildi, export edildi, yeniden API'ye replay edildi ve compare sonucu MATCH oldu. İstemci yanıtı değişmedi; HTML raporunda test parolası bulunmadı. Başlatılan API süreci test sonunda kapatıldı.

## Kanıtlar ve yeniden çalıştırma

```powershell
dotnet test TraceCapsule.sln --configuration Release --logger trx --results-directory TestResults/final --collect:"XPlat Code Coverage"
dotnet pack TraceCapsule.sln --configuration Release --no-build --output ./nupkg
```

- Son TRX ve Cobertura çıktıları: `TestResults/final/`.
- Bu oturumda kullanılan paket smoke betiği: `TestResults/packaged-smoke.ps1`.
- Gerçek örnek kapsüller, HTML ve API günlükleri: `TestResults/packaged-smoke/`.
- Bu çıktı klasörleri `.gitignore` kapsamındadır; yeni C# testleri repoda kalıcıdır.
- Cobertura dosyalarında dosya/satır anahtarıyla birleştirilen ölçüm: **1581/2132 satır, %74,16**. Bu değer örnek uygulamaları da içerir; ayrı CLI süreçlerinin yürüttüğü satırlar collector tarafından ölçülmediğinden CLI uçtan uca kapsamını tam göstermez.

## Sınırlar ve kalan noktalar

- Gerçek RabbitMQ broker bağlantısı doğrulanmadı. Docker daemon bağlantısı kurulamadı; kuyruk emülatörü ve kayıt adaptörü testleri çalıştı.
- Linux, uzun süreli yük, bellek tüketimi ve performans ölçümü yapılmadı. 30 eşzamanlı istek bir izolasyon testi olup kapasite ölçümü değildir.
- CLI replay canlı hedefin giriş isteğini tekrarlar. Hedefte dependency/determinism replay yapılandırması ayrıca gerekir; kapsül tek başına bütün servisleri otomatik kurmaz.
- Kod incelemesinde dağıtık `CapsuleMerger`'ın `Determinism` koleksiyonunu taşımadığı görüldü. Servis bazında determinism verisi ve replay sırasını koruyan bir tasarım ayrıca gereklidir; birleşik kapsülden tam dağıtık deterministik replay doğrulanmış değildir.
- JSON alanı/başlık maskelemesi bütün olası veri kanallarını kapsamaz. Geçersiz JSON, serbest metin, query string, exception ve span tag içeriklerinde genel bir secret temizleme garantisi yoktur. CLI replay varsayılan politikayı kullanır; host'un özel policy dosyasını devralmaz.
- Eski sürümle üretilmiş birleşik dosyalarda yeni `IsMerged` işareti yoktur; tekrar merge öncesinde bunları kaynak klasöründen ayırmak gerekir.
