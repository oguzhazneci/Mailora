/* =====================================================================
   Mailora — ZENGİN örnek veri seti (13.4, 20.3)
   - Veritabanındaki BÜTÜN mesajları siler, yerine yaklaşık 163 mesaj yükler (yaklaşık son 30 gün)
   - Tarihler çalıştırma anına göredir: script hangi gün çalışırsa veriler o güne göre güncel olur
   - Tek / çok alıcılı, Bilgi (Cc) ve Gizli (Bcc) alıcılı, Mailora dışı adresli mesajlar
   - "Re:" yanıt zincirleri (ikinci seviye dahil), "Fwd:" iletmeler (ekleriyle)
   - Her kullanıcıda: taslak, yıldızlı, arşiv, çöp ve okunmamış örnekleri
   - Zamanlanmış gönderimler (gelecek), çöpte zamanı geçmiş bir zamanlanmış mesaj,
     süresi dolmak üzere olan çöp öğeleri ve dış teslimatı başarısız bir mesaj (20.3)
   - Eklerin dosyaları ayrı bir ZIP'te; adları buradaki StoredName değerleriyle aynı
   - Kullanıcılar: oguz01, umut01, ued01, baris01, murat01 (ahmet01 bilerek boş kalır)
   - Gövdeler <!--mailora-seed--> işaretlidir: 99-dashboard-seed-cleanup.sql bu mesajları bulur

   ÇALIŞTIRMA SIRASI (README / 13.4):
     1) Uygulamayı DURDUR (arka plan servisleri çalışmasın: ek süpürücü, çöp temizleyici, zamanlanmış gönderici)
     2) Bu script'in tamamını çalıştır, en alttaki kontrol sorgularına bak
     3) Doğruysa sadece  COMMIT;   değilse  ROLLBACK;   (aynı pencerede)
     4) COMMIT'ten SONRA ZIP'i App_Data/attachments klasörüne aç
     5) Uygulamayı başlat
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF @@TRANCOUNT > 0
BEGIN
    ;THROW 50000, N'Bu bağlantıda açık bir transaction var. Önce COMMIT ya da ROLLBACK çalıştırın.', 1;
END

DECLARE @Now   DATETIME2 = GETDATE();
DECLARE @Today DATETIME2 = CAST(CAST(@Now AS DATE) AS DATETIME2);   -- zamanlar "bugün 00:00"a göre dakika

DECLARE @oguz_Id NVARCHAR(450), @oguz_Email NVARCHAR(256), @oguz_Name NVARCHAR(300);
SELECT @oguz_Id = Id, @oguz_Email = Email, @oguz_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'oguz01';
DECLARE @umut_Id NVARCHAR(450), @umut_Email NVARCHAR(256), @umut_Name NVARCHAR(300);
SELECT @umut_Id = Id, @umut_Email = Email, @umut_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'umut01';
DECLARE @ued_Id NVARCHAR(450), @ued_Email NVARCHAR(256), @ued_Name NVARCHAR(300);
SELECT @ued_Id = Id, @ued_Email = Email, @ued_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'ued01';
DECLARE @baris_Id NVARCHAR(450), @baris_Email NVARCHAR(256), @baris_Name NVARCHAR(300);
SELECT @baris_Id = Id, @baris_Email = Email, @baris_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'baris01';
DECLARE @murat_Id NVARCHAR(450), @murat_Email NVARCHAR(256), @murat_Name NVARCHAR(300);
SELECT @murat_Id = Id, @murat_Email = Email, @murat_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'murat01';

IF @oguz_Id IS NULL OR @umut_Id IS NULL OR @ued_Id IS NULL OR @baris_Id IS NULL OR @murat_Id IS NULL
BEGIN
    ;THROW 50001, N'Beş test kullanıcısından biri bulunamadı (oguz01, umut01, ued01, baris01, murat01).', 1;
END
IF (SELECT COUNT(*) FROM Categories WHERE CategoryId BETWEEN 1 AND 10) <> 10
BEGIN
    ;THROW 50002, N'1–10 numaralı kategoriler bulunamadı.', 1;
END
-- Zamanlanmış gönderim sütunları var mı? (18. aşama migration'ı)
IF COL_LENGTH('Messages', 'ScheduledFor') IS NULL OR COL_LENGTH('Messages', 'ExternalDeliveryFailed') IS NULL
BEGIN
    ;THROW 50004, N'Messages tablosunda ScheduledFor / ExternalDeliveryFailed yok. Önce migration''ları uygulayın.', 1;
END
-- Alıcı türü sütunu metin olarak tutuluyor mu? (To / Cc / Bcc). Başka bir biçim varsa dur.
IF EXISTS (SELECT 1 FROM MessageRecipients WHERE RecipientType NOT IN (N'To', N'Cc', N'Bcc'))
BEGIN
    ;THROW 50003, N'MessageRecipients.RecipientType beklenmeyen bir değer içeriyor; script durduruldu.', 1;
END

BEGIN TRANSACTION;

-- 1) Eski mesajların hepsi. Alıcı, kopya ve ek SATIRLARI cascade ile gider (5.3, 12.15).
--    Eski ek DOSYALARI diskte sahipsiz kalır → uygulama açılınca süpürücü toplar (12.19).
DELETE FROM Messages;
SELECT @@ROWCOUNT AS SilinenEskiMesaj;

DECLARE @m INT;

-- #1 BASE · murat01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -44040, @Today), DATEADD(MINUTE, -44040, @Today), DATEADD(MINUTE, -44040, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -41880, @Now), NULL);

-- #2 BASE · baris01 · Otel rezervasyonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Otel rezervasyonu', N'<p>Merhaba,</p><p>Otel rezervasyonu tamam, giriş 14:00 itibarıyla.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -42600, @Today), DATEADD(MINUTE, -42600, @Today), DATEADD(MINUTE, -42600, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -40440, @Now), NULL);

-- #3 BASE · oguz01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -41240, @Today), DATEADD(MINUTE, -41240, @Today), DATEADD(MINUTE, -41240, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #4 BASE · oguz01 · Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Siparişiniz kargoda', N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -41203, @Today), DATEADD(MINUTE, -41203, @Today), DATEADD(MINUTE, -41203, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #5 BASE · umut01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -41160, @Today), DATEADD(MINUTE, -41160, @Today), DATEADD(MINUTE, -41160, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -39000, @Now), NULL);

-- #6 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -39741, @Today), DATEADD(MINUTE, -39741, @Today), DATEADD(MINUTE, -39741, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc'),
    (@m, N'muhasebe@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #7 BASE · oguz01 · Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Yeni proje başlangıcı', N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -39589, @Today), DATEADD(MINUTE, -39589, @Today), DATEADD(MINUTE, -39589, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #8 BASE · baris01 · Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Siparişiniz kargoda', N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -38322, @Today), DATEADD(MINUTE, -38322, @Today), DATEADD(MINUTE, -38322, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #9 BASE · ued01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -38262, @Today), DATEADD(MINUTE, -38262, @Today), DATEADD(MINUTE, -38262, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc'),
    (@m, @oguz_Email, @oguz_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #10 REPLY · ued01 · Re: Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Re: Siparişiniz kargoda', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -38322, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -38060, @Today), DATEADD(MINUTE, -38060, @Today), DATEADD(MINUTE, -38060, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -37469, @Today));

-- #11 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -37980, @Today), DATEADD(MINUTE, -37980, @Today), DATEADD(MINUTE, -37980, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #12 BASE · umut01 · Gezi programı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Gezi programı', N'<p>Merhaba,</p><p>Üç günlük gezi programını hazırladım, ekte bulabilirsiniz.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -37940, @Today), DATEADD(MINUTE, -37940, @Today), DATEADD(MINUTE, -37940, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, DATEADD(MINUTE, -37691, @Today), NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Harita.jpg', '42d8436517264a168e08b9fa3f329c72', 'image/jpeg', 23033, DATEADD(MINUTE, -37940, @Today));

-- #13 BASE · ued01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -37879, @Today), DATEADD(MINUTE, -37879, @Today), DATEADD(MINUTE, -37879, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, N'info@example.org', NULL, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -36605, @Today));
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sürüm_Notları.txt', '508cc068be4b4bfb97ce85b7cc6c3902', 'text/plain', 226, DATEADD(MINUTE, -37879, @Today));

-- #14 BASE · murat01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -37794, @Today), DATEADD(MINUTE, -37794, @Today), DATEADD(MINUTE, -37794, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #15 REPLY · murat01 · Re: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: Ders notları', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -38262, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -37764, @Today), DATEADD(MINUTE, -37764, @Today), DATEADD(MINUTE, -37764, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #16 REPLY · baris01 · Re: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Re: Ders notları', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -37980, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @murat_Name + N'</b> &lt;' + @murat_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -37727, @Today), DATEADD(MINUTE, -37727, @Today), DATEADD(MINUTE, -37727, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #17 REPLY · oguz01 · Re: Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Uçuş bilgileri', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -37794, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @murat_Name + N'</b> &lt;' + @murat_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 9, DATEADD(MINUTE, -37567, @Today), DATEADD(MINUTE, -37567, @Today), DATEADD(MINUTE, -37567, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #18 BASE · murat01 · Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Konser biletleri', N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -36826, @Today), DATEADD(MINUTE, -36826, @Today), DATEADD(MINUTE, -36826, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, N'deniz.kaya@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Konser_Programı.png', '3c77f38640e14abdb4feaef8a4ba3f5f', 'image/png', 10572, DATEADD(MINUTE, -36826, @Today));

-- #19 FORWARD · oguz01 · Fwd: Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fwd: Konser biletleri', N'<p>Bilgin olsun, aşağıdaki mesaj senin de işine yarayabilir.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @murat_Name + N' &lt;' + @murat_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -36826, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Konser biletleri' + N'<br><b>Kime:</b> ' + @oguz_Email + N', ' + N'deniz.kaya@example.com' + N'</p>' + N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 4, DATEADD(MINUTE, -36634, @Today), DATEADD(MINUTE, -36634, @Today), DATEADD(MINUTE, -36634, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Konser_Programı.png', 'a6cc3a14b9f7436f809bc97fb8fc43fb', 'image/png', 10567, DATEADD(MINUTE, -36634, @Today));

-- #20 BASE · oguz01 · Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Konser biletleri', N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -35341, @Today), DATEADD(MINUTE, -35341, @Today), DATEADD(MINUTE, -35341, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);

-- #21 BASE · umut01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -34954, @Today), DATEADD(MINUTE, -34954, @Today), DATEADD(MINUTE, -34954, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #22 BASE · baris01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -34720, @Today), DATEADD(MINUTE, -34720, @Today), DATEADD(MINUTE, -34720, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);

-- #23 BASE · murat01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -34025, @Today), DATEADD(MINUTE, -34025, @Today), DATEADD(MINUTE, -34025, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -33137, @Today));

-- #24 BASE · baris01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -34004, @Today), DATEADD(MINUTE, -34004, @Today), DATEADD(MINUTE, -34004, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Davetiye.jpg', '12c5d617b0ce42efa6d4395df894de8b', 'image/jpeg', 25219, DATEADD(MINUTE, -34004, @Today));

-- #25 BASE · oguz01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -33566, @Today), DATEADD(MINUTE, -33566, @Today), DATEADD(MINUTE, -33566, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL);

-- #26 BASE · baris01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -33481, @Today), DATEADD(MINUTE, -33481, @Today), DATEADD(MINUTE, -33481, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hediye_Seçenekleri.png', 'ce6c749558d346df86e1a8b86cd0ca4c', 'image/png', 9889, DATEADD(MINUTE, -33481, @Today));

-- #27 BASE · umut01 · Sistem bakımı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Sistem bakımı', N'<p>Merhaba,</p><p>Pazar gecesi 02:00–04:00 arasında planlı bakım yapılacaktır.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -33458, @Today), DATEADD(MINUTE, -33458, @Today), DATEADD(MINUTE, -33458, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #28 FORWARD · oguz01 · Fwd: Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fwd: Doğum günü sürprizi', N'<p>Merhaba,</p><p>Konuyla ilgili mesajı iletiyorum, eklere bakabilirsin.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @baris_Name + N' &lt;' + @baris_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -34004, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Doğum günü sürprizi' + N'<br><b>Kime:</b> ' + @oguz_Email + N'</p>' + N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 2, DATEADD(MINUTE, -33417, @Today), DATEADD(MINUTE, -33417, @Today), DATEADD(MINUTE, -33417, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Davetiye.jpg', '9ccd5cb2bf65480b80addee0031237d2', 'image/jpeg', 22982, DATEADD(MINUTE, -33417, @Today));

-- #29 REPLY · murat01 · Re: Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: Uçuş bilgileri', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -33566, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 9, DATEADD(MINUTE, -33380, @Today), DATEADD(MINUTE, -33380, @Today), DATEADD(MINUTE, -33380, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #30 REPLY · oguz01 · Re: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Ortak hediye', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -33481, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -33243, @Today), DATEADD(MINUTE, -33243, @Today), DATEADD(MINUTE, -33243, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #31 REPLY · baris01 · Re: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Re: Ortak hediye', N'<p>Evet, son tarih kesin. Kolay gelsin.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -33243, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -33481, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -33172, @Today), DATEADD(MINUTE, -33172, @Today), DATEADD(MINUTE, -33172, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #32 BASE · umut01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -32424, @Today), DATEADD(MINUTE, -32424, @Today), DATEADD(MINUTE, -32424, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -32160, @Today)),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Yürüyüş_Rotası.jpg', 'a149a2ef75cd4f8d89e4873ba9f715e6', 'image/jpeg', 25578, DATEADD(MINUTE, -32424, @Today));

-- #33 BASE · baris01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -32397, @Today), DATEADD(MINUTE, -32397, @Today), DATEADD(MINUTE, -32397, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -32320, @Today)),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #34 BASE · ued01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -32292, @Today), DATEADD(MINUTE, -32292, @Today), DATEADD(MINUTE, -32292, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #35 BASE · murat01 · Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Siparişiniz kargoda', N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -32186, @Today), DATEADD(MINUTE, -32186, @Today), DATEADD(MINUTE, -32186, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sipariş_Özeti.pdf', '98e85f5cf4864b0f839aee8d77ae54f3', 'application/pdf', 44858, DATEADD(MINUTE, -32186, @Today));

-- #36 BASE · baris01 · Kod incelemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Kod incelemesi', N'<p>Merhaba,</p><p>Pull request''ine yorum bıraktım; birkaç küçük düzeltme yeterli.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -32048, @Today), DATEADD(MINUTE, -32048, @Today), DATEADD(MINUTE, -32048, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #37 REPLY · oguz01 · Re: Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Kitap önerisi', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -32292, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -31951, @Today), DATEADD(MINUTE, -31951, @Today), DATEADD(MINUTE, -31951, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #38 BASE · baris01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -31889, @Today), DATEADD(MINUTE, -31889, @Today), DATEADD(MINUTE, -31889, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, N'muhasebe@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #39 BASE · baris01 · Otel rezervasyonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Otel rezervasyonu', N'<p>Merhaba,</p><p>Otel rezervasyonu tamam, giriş 14:00 itibarıyla.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -31874, @Today), DATEADD(MINUTE, -31874, @Today), DATEADD(MINUTE, -31874, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #40 REPLY · umut01 · Re: Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Re: Cuma akşamı buluşma', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -31889, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 4, DATEADD(MINUTE, -31572, @Today), DATEADD(MINUTE, -31572, @Today), DATEADD(MINUTE, -31572, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #41 REPLY · oguz01 · Re: Otel rezervasyonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Otel rezervasyonu', N'<p>Teşekkürler, inceledim. Benim için uygun.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -31874, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Otel rezervasyonu tamam, giriş 14:00 itibarıyla.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 9, DATEADD(MINUTE, -31395, @Today), DATEADD(MINUTE, -31395, @Today), DATEADD(MINUTE, -31395, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #42 FORWARD · murat01 · Fwd: Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Fwd: Hafta sonu planı', N'<p>Bilgin olsun, aşağıdaki mesaj senin de işine yarayabilir.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @umut_Name + N' &lt;' + @umut_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -32424, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Hafta sonu planı' + N'<br><b>Kime:</b> ' + @oguz_Email + N', ' + @murat_Email + N', ' + @baris_Email + N'</p>' + N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 2, DATEADD(MINUTE, -31302, @Today), DATEADD(MINUTE, -31302, @Today), DATEADD(MINUTE, -31302, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Yürüyüş_Rotası.jpg', '6dae9f293af14d319855b799014f4aa6', 'image/jpeg', 24672, DATEADD(MINUTE, -31302, @Today));

-- #43 BASE · umut01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -31023, @Today), DATEADD(MINUTE, -31023, @Today), DATEADD(MINUTE, -31023, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #44 BASE · oguz01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -30985, @Today), DATEADD(MINUTE, -30985, @Today), DATEADD(MINUTE, -30985, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'muhasebe@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -30886, @Today), NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', 'cbdfb8a4b2aa4def8438144e8e5d4513', 'application/pdf', 44671, DATEADD(MINUTE, -30985, @Today));

-- #45 BASE · ued01 · Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -30694, @Today), DATEADD(MINUTE, -30694, @Today), DATEADD(MINUTE, -30694, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @murat_Email, @murat_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #46 BASE · murat01 · Otel rezervasyonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Otel rezervasyonu', N'<p>Merhaba,</p><p>Otel rezervasyonu tamam, giriş 14:00 itibarıyla.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -30689, @Today), DATEADD(MINUTE, -30689, @Today), DATEADD(MINUTE, -30689, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, N'destek@example.net', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #47 REPLY · baris01 · Re: Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Re: Haftalık proje toplantısı', N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -30694, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -30367, @Today), DATEADD(MINUTE, -30367, @Today), DATEADD(MINUTE, -30367, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -29571, @Today), NULL);

-- #48 FORWARD · oguz01 · Fwd: Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fwd: Fatura hatırlatması', N'<p>Bilgin olsun, aşağıdaki mesaj senin de işine yarayabilir.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @oguz_Name + N' &lt;' + @oguz_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -30985, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Fatura hatırlatması' + N'<br><b>Kime:</b> ' + @baris_Email + N'<br><b>Bilgi:</b> ' + N'muhasebe@example.org' + N'</p>' + N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 3, DATEADD(MINUTE, -29818, @Today), DATEADD(MINUTE, -29818, @Today), DATEADD(MINUTE, -29818, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, N'selin.ak@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', 'd908c99429af470aa1712a264edd9e2a', 'application/pdf', 44671, DATEADD(MINUTE, -29818, @Today));

-- #49 BASE · baris01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -29510, @Today), DATEADD(MINUTE, -29510, @Today), DATEADD(MINUTE, -29510, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL);

-- #50 BASE · ued01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -29297, @Today), DATEADD(MINUTE, -29297, @Today), DATEADD(MINUTE, -29297, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #51 BASE · oguz01 · Film gecesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Film gecesi', N'<p>Selam,</p><p>Cuma akşamı film gecesi yapalım mı? Liste ekte, oylayalım.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -27598, @Today), DATEADD(MINUTE, -27598, @Today), DATEADD(MINUTE, -27598, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #52 BASE · baris01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -26491, @Today), DATEADD(MINUTE, -26491, @Today), DATEADD(MINUTE, -26491, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, N'info@example.org', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #53 BASE · umut01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -26044, @Today), DATEADD(MINUTE, -26044, @Today), DATEADD(MINUTE, -26044, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -24990, @Today));

-- #54 BASE · oguz01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -25304, @Today), DATEADD(MINUTE, -25304, @Today), DATEADD(MINUTE, -25304, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #55 BASE · ued01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -24968, @Today), DATEADD(MINUTE, -24968, @Today), DATEADD(MINUTE, -24968, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #56 BASE · oguz01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -24777, @Today), DATEADD(MINUTE, -24777, @Today), DATEADD(MINUTE, -24777, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Örnek_Kod.txt', '13b25d260586430f896ffbf8fadd5372', 'text/plain', 220, DATEADD(MINUTE, -24777, @Today));

-- #57 FORWARD · oguz01 · Fwd: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fwd: Ders notları', N'<p>Merhaba,</p><p>Konuyla ilgili mesajı iletiyorum, eklere bakabilirsin.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @oguz_Name + N' &lt;' + @oguz_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -24777, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Ders notları' + N'<br><b>Kime:</b> ' + @umut_Email + N'</p>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 7, DATEADD(MINUTE, -23520, @Today), DATEADD(MINUTE, -23520, @Today), DATEADD(MINUTE, -23520, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Örnek_Kod.txt', '7ed152ac46bb4bba9f590d70b12a9240', 'text/plain', 220, DATEADD(MINUTE, -23520, @Today));

-- #58 BASE · murat01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -23433, @Today), DATEADD(MINUTE, -23433, @Today), DATEADD(MINUTE, -23433, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #59 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -23381, @Today), DATEADD(MINUTE, -23381, @Today), DATEADD(MINUTE, -23381, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', '06c41dfa68c34700840b269a805e3b7a', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -23381, @Today)),
    (@m, N'Örnek_Kod.txt', '5fd521bd10da408687c69fb6ed07cfc9', 'text/plain', 220, DATEADD(MINUTE, -23381, @Today));

-- #60 BASE · murat01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -22238, @Today), DATEADD(MINUTE, -22238, @Today), DATEADD(MINUTE, -22238, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #61 BASE · oguz01 · Tatil günleri duyurusu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Tatil günleri duyurusu', N'<p>Merhaba,</p><p>Resmi tatil günlerinde ofis kapalı olacaktır.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -22084, @Today), DATEADD(MINUTE, -22084, @Today), DATEADD(MINUTE, -22084, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #62 BASE · umut01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -21918, @Today), DATEADD(MINUTE, -21918, @Today), DATEADD(MINUTE, -21918, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);

-- #63 BASE · ued01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -21795, @Today), DATEADD(MINUTE, -21795, @Today), DATEADD(MINUTE, -21795, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -20317, @Today));

-- #64 REPLY · murat01 · Re: Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: Hafta sonu planı', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -21795, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -21384, @Today), DATEADD(MINUTE, -21384, @Today), DATEADD(MINUTE, -21384, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #65 BASE · ued01 · Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Siparişiniz kargoda', N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -21076, @Today), DATEADD(MINUTE, -21076, @Today), DATEADD(MINUTE, -21076, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #66 REPLY · murat01 · Re: Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: Siparişiniz kargoda', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -21076, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -20745, @Today), DATEADD(MINUTE, -20745, @Today), DATEADD(MINUTE, -20745, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, DATEADD(MINUTE, -18940, @Today), NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #67 BASE · oguz01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -20568, @Today), DATEADD(MINUTE, -20568, @Today), DATEADD(MINUTE, -20568, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -19540, @Today));

-- #68 BASE · ued01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -19645, @Today), DATEADD(MINUTE, -19645, @Today), DATEADD(MINUTE, -19645, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #69 BASE · murat01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -19580, @Today), DATEADD(MINUTE, -19580, @Today), DATEADD(MINUTE, -19580, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Bütçe_Q4.xlsx', '6422b82bc549400bb07514aea14bf716', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5410, DATEADD(MINUTE, -19580, @Today));

-- #70 BASE · murat01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -19576, @Today), DATEADD(MINUTE, -19576, @Today), DATEADD(MINUTE, -19576, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -18283, @Today), NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -18188, @Today)),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);

-- #71 BASE · baris01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -19572, @Today), DATEADD(MINUTE, -19572, @Today), DATEADD(MINUTE, -19572, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc'),
    (@m, @ued_Email, @ued_Id, N'Cc'),
    (@m, N'deniz.kaya@example.com', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hediye_Seçenekleri.png', '5ba937e542164f8fb3d2cb53742c6570', 'image/png', 9903, DATEADD(MINUTE, -19572, @Today));

-- #72 BASE · ued01 · Veritabanı yedekleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Veritabanı yedekleri', N'<p>Merhaba,</p><p>Gece yedeklerinin süresi uzadı. Son bir haftanın süre tablosu ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -19267, @Today), DATEADD(MINUTE, -19267, @Today), DATEADD(MINUTE, -19267, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #73 FORWARD · baris01 · Fwd: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Fwd: Ortak hediye', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @baris_Name + N' &lt;' + @baris_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -19572, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Ortak hediye' + N'<br><b>Kime:</b> ' + @umut_Email + N'<br><b>Bilgi:</b> ' + @murat_Email + N', ' + @ued_Email + N'</p>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 8, DATEADD(MINUTE, -18716, @Today), DATEADD(MINUTE, -18716, @Today), DATEADD(MINUTE, -18716, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hediye_Seçenekleri.png', '29fda92efbc74fe9828612f11c8264af', 'image/png', 9887, DATEADD(MINUTE, -18716, @Today));

-- #74 FORWARD · murat01 · Fwd: Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Fwd: Bütçe tablosu güncellendi', N'<p>Bilgin olsun, aşağıdaki mesaj senin de işine yarayabilir.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @murat_Name + N' &lt;' + @murat_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -19580, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Bütçe tablosu güncellendi' + N'<br><b>Kime:</b> ' + @baris_Email + N'</p>' + N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 3, DATEADD(MINUTE, -18245, @Today), DATEADD(MINUTE, -18245, @Today), DATEADD(MINUTE, -18245, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Bütçe_Q4.xlsx', 'dce72b78a2de429fa1461f8623ba5c5f', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5410, DATEADD(MINUTE, -18245, @Today));

-- #75 BASE · oguz01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -18233, @Today), DATEADD(MINUTE, -18233, @Today), DATEADD(MINUTE, -18233, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 1, NULL, NULL);

-- #76 REPLY · umut01 · Re: Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Re: Eğitim kaydı onayı', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -18233, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -18183, @Today), DATEADD(MINUTE, -18183, @Today), DATEADD(MINUTE, -18183, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #77 REPLY · oguz01 · Re: Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Eğitim kaydı onayı', N'<p>Anlaştık, takvime ekledim.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -18183, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @umut_Name + N'</b> &lt;' + @umut_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -18233, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -18051, @Today), DATEADD(MINUTE, -18051, @Today), DATEADD(MINUTE, -18051, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -16655, @Today), NULL);

-- #78 BASE · murat01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -18009, @Today), DATEADD(MINUTE, -18009, @Today), DATEADD(MINUTE, -18009, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 1, NULL, NULL);

-- #79 BASE · baris01 · Yemekhane menüsü
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Yemekhane menüsü', N'<p>Merhaba,</p><p>Önümüzdeki haftanın yemekhane menüsü ekte.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -17977, @Today), DATEADD(MINUTE, -17977, @Today), DATEADD(MINUTE, -17977, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #80 BASE · umut01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -16717, @Today), DATEADD(MINUTE, -16717, @Today), DATEADD(MINUTE, -16717, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -15557, @Today));

-- #81 BASE · baris01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -16698, @Today), DATEADD(MINUTE, -16698, @Today), DATEADD(MINUTE, -16698, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #82 BASE · oguz01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -16509, @Today), DATEADD(MINUTE, -16509, @Today), DATEADD(MINUTE, -16509, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, DATEADD(MINUTE, -15550, @Today), NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #83 TASLAK · murat01 · Bütçe önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Bütçe önerisi', N'<p>Merhaba,</p><p>Gelecek çeyrek için bütçe önerimi ekliyorum. Kısa bir açıklama yazacağım.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -15291, @Today), DATEADD(MINUTE, -14753, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Draft', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Bütçe_Önerisi.xlsx', '8a962db4cc0e4050a1ec290e6659444a', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5406, DATEADD(MINUTE, -15291, @Today));

-- #84 TASLAK · umut01 · Hafta sonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Hafta sonu', N'<p>Selam,</p><p>Cumartesi için şöyle düşünmüştüm…</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -15040, @Today), DATEADD(MINUTE, -14157, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Draft', 1, 0, NULL, NULL);

-- #85 BASE · ued01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -14907, @Today), DATEADD(MINUTE, -14907, @Today), DATEADD(MINUTE, -14907, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'info@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 0, 1, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -13129, @Today), NULL);

-- #86 BASE · ued01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -14841, @Today), DATEADD(MINUTE, -14841, @Today), DATEADD(MINUTE, -14841, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);

-- #87 BASE · baris01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -14769, @Today), DATEADD(MINUTE, -14769, @Today), DATEADD(MINUTE, -14769, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL);

-- #88 BASE · murat01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -14653, @Today), DATEADD(MINUTE, -14653, @Today), DATEADD(MINUTE, -14653, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -13334, @Today), NULL);

-- #89 TASLAK · ued01 · (konusuz)
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'', N'<p>Hatırlatma: faturayı kontrol et.</p><!--mailora-seed-->', NULL, DATEADD(MINUTE, -14568, @Today), DATEADD(MINUTE, -14087, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Draft', 1, 0, NULL, NULL);

-- #90 BASE · umut01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -13829, @Today), DATEADD(MINUTE, -13829, @Today), DATEADD(MINUTE, -13829, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #91 BASE · baris01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -13768, @Today), DATEADD(MINUTE, -13768, @Today), DATEADD(MINUTE, -13768, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #92 BASE · oguz01 · Destek kaydı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Destek kaydı', N'<p>Merhaba,</p><p>Uygulamada gördüğüm hatayı bildirmek istiyorum. Ekran görüntüsü ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -13703, @Today), DATEADD(MINUTE, -13703, @Today), DATEADD(MINUTE, -13703, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, N'destek@example.net', NULL, N'Bcc'),
    (@m, N'info@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hata_Ekranı.png', 'd4eab35870c9458f9eee37a1728a9353', 'image/png', 7517, DATEADD(MINUTE, -13703, @Today));

-- #93 BASE · oguz01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -13679, @Today), DATEADD(MINUTE, -13679, @Today), DATEADD(MINUTE, -13679, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #94 REPLY · murat01 · Re: İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: İzin talebi', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -13679, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -13515, @Today), DATEADD(MINUTE, -13515, @Today), DATEADD(MINUTE, -13515, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #95 REPLY · oguz01 · Re: Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Fatura hatırlatması', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -13768, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -13445, @Today), DATEADD(MINUTE, -13445, @Today), DATEADD(MINUTE, -13445, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL);

-- #96 BASE · baris01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -13410, @Today), DATEADD(MINUTE, -13410, @Today), DATEADD(MINUTE, -13410, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -12172, @Today));
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hediye_Seçenekleri.png', '39852640443a49179b8fade7941855df', 'image/png', 9894, DATEADD(MINUTE, -13410, @Today));

-- #97 TASLAK · murat01 · Duyuru metni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Duyuru metni', N'<p>Değerli çalışma arkadaşlarımız,</p><p>…</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -13147, @Today), DATEADD(MINUTE, -12483, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Draft', 1, 0, NULL, NULL);

-- #98 FORWARD · baris01 · Fwd: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Fwd: Ortak hediye', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @baris_Name + N' &lt;' + @baris_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -13410, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Ortak hediye' + N'<br><b>Kime:</b> ' + @umut_Email + N'<br><b>Bilgi:</b> ' + @ued_Email + N'</p>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 8, DATEADD(MINUTE, -12481, @Today), DATEADD(MINUTE, -12481, @Today), DATEADD(MINUTE, -12481, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, N'deniz.kaya@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hediye_Seçenekleri.png', '773a9cdb16624a44a7853ea852c36ca2', 'image/png', 9890, DATEADD(MINUTE, -12481, @Today));

-- #99 TASLAK · oguz01 · Toplantı notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Toplantı notları', N'<p>Merhaba,</p><p>Bugünkü toplantının notlarını derliyorum, yarın tamamlayıp göndereceğim:</p><ul><li>Takvim</li><li>Bütçe</li></ul><!--mailora-seed-->', 1, DATEADD(MINUTE, -12442, @Today), DATEADD(MINUTE, -12337, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Draft', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Toplantı_Notları.docx', '896431b9bbbc4775b680597e84564d49', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36573, DATEADD(MINUTE, -12442, @Today));

-- #100 BASE · murat01 · Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Haftalık özet', N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -12235, @Today), DATEADD(MINUTE, -12235, @Today), DATEADD(MINUTE, -12235, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, N'selin.ak@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #101 BASE · baris01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -12177, @Today), DATEADD(MINUTE, -12177, @Today), DATEADD(MINUTE, -12177, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -11787, @Today), NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Davetiye.jpg', '09b897e462fc4e16b5d5597d079a7049', 'image/jpeg', 23719, DATEADD(MINUTE, -12177, @Today));

-- #102 BASE · murat01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -11849, @Today), DATEADD(MINUTE, -11849, @Today), DATEADD(MINUTE, -11849, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #103 BASE · oguz01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -10942, @Today), DATEADD(MINUTE, -10942, @Today), DATEADD(MINUTE, -10942, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #104 BASE · ued01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -10900, @Today), DATEADD(MINUTE, -10900, @Today), DATEADD(MINUTE, -10900, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Örnek_Kod.txt', '31e7a506270244cab10ed947e3d933e3', 'text/plain', 220, DATEADD(MINUTE, -10900, @Today)),
    (@m, N'Ders_Notları.docx', '6c145b1da5f14a21904c6be58043867d', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -10900, @Today));

-- #105 TASLAK · baris01 · Ödev hakkında soru
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Ödev hakkında soru', N'<p>Merhaba,</p><p>Ödevin ikinci kısmında bir noktayı tam anlayamadım:</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -10814, @Today), DATEADD(MINUTE, -10468, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Draft', 1, 0, NULL, NULL);

-- #106 REPLY · oguz01 · Re: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Ders notları', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10900, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -10711, @Today), DATEADD(MINUTE, -10711, @Today), DATEADD(MINUTE, -10711, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #107 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -10660, @Today), DATEADD(MINUTE, -10660, @Today), DATEADD(MINUTE, -10660, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, N'muhasebe@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #108 REPLY · ued01 · Re: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Re: Ders notları', N'<p>Olur, gelecek hafta salı nasıl?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10711, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10900, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -10522, @Today), DATEADD(MINUTE, -10522, @Today), DATEADD(MINUTE, -10522, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #109 BASE · ued01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -10516, @Today), DATEADD(MINUTE, -10516, @Today), DATEADD(MINUTE, -10516, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, DATEADD(MINUTE, -10091, @Today), NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', '1146a222636e40c990c22ad7264dee20', 'application/pdf', 44671, DATEADD(MINUTE, -10516, @Today));

-- #110 REPLY · baris01 · Re: Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Re: Fatura hatırlatması', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10516, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -10438, @Today), DATEADD(MINUTE, -10438, @Today), DATEADD(MINUTE, -10438, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, DATEADD(MINUTE, -8836, @Today), NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #111 REPLY · umut01 · Re: Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Re: Müşteri geri bildirimi', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10942, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -10368, @Today), DATEADD(MINUTE, -10368, @Today), DATEADD(MINUTE, -10368, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);

-- #112 BASE · baris01 · Tatil günleri duyurusu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Tatil günleri duyurusu', N'<p>Merhaba,</p><p>Resmi tatil günlerinde ofis kapalı olacaktır.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -10288, @Today), DATEADD(MINUTE, -10288, @Today), DATEADD(MINUTE, -10288, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -9912, @Today)),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #113 TASLAK · ued01 · (konusuz)
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'', N'<p>Cuma için mekan önerisi…</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -10249, @Today), DATEADD(MINUTE, -9515, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Draft', 1, 0, NULL, NULL);

-- #114 REPLY · oguz01 · Re: Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Müşteri geri bildirimi', N'<p>Olur, gelecek hafta salı nasıl?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10368, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @umut_Name + N'</b> &lt;' + @umut_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -10942, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -10119, @Today), DATEADD(MINUTE, -10119, @Today), DATEADD(MINUTE, -10119, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #115 BASE · oguz01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -9584, @Today), DATEADD(MINUTE, -9584, @Today), DATEADD(MINUTE, -9584, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc'),
    (@m, N'selin.ak@example.com', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL);

-- #116 BASE · baris01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -9136, @Today), DATEADD(MINUTE, -9136, @Today), DATEADD(MINUTE, -9136, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #117 BASE · oguz01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -9123, @Today), DATEADD(MINUTE, -9123, @Today), DATEADD(MINUTE, -9123, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL);

-- #118 BASE · oguz01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -9030, @Today), DATEADD(MINUTE, -9030, @Today), DATEADD(MINUTE, -9030, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #119 BASE · murat01 · Film gecesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Film gecesi', N'<p>Selam,</p><p>Cuma akşamı film gecesi yapalım mı? Liste ekte, oylayalım.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -8987, @Today), DATEADD(MINUTE, -8987, @Today), DATEADD(MINUTE, -8987, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 0, 0, NULL, NULL);

-- #120 REPLY · ued01 · Re: Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Re: Hafta sonu planı', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -9030, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -8887, @Today), DATEADD(MINUTE, -8887, @Today), DATEADD(MINUTE, -8887, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #121 BASE · murat01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -8842, @Today), DATEADD(MINUTE, -8842, @Today), DATEADD(MINUTE, -8842, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', 'd21090dedbd64fec9ab452ec6eed3226', 'application/pdf', 44671, DATEADD(MINUTE, -8842, @Today));

-- #122 BASE · umut01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -7726, @Today), DATEADD(MINUTE, -7726, @Today), DATEADD(MINUTE, -7726, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #123 REPLY · murat01 · Re: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: Ortak hediye', N'<p>Teşekkürler, inceledim. Benim için uygun.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -7726, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @umut_Name + N'</b> &lt;' + @umut_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -7514, @Today), DATEADD(MINUTE, -7514, @Today), DATEADD(MINUTE, -7514, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -6155, @Today));

-- #124 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -6662, @Today), DATEADD(MINUTE, -6662, @Today), DATEADD(MINUTE, -6662, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -6124, @Today));
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', 'd280a550438b448ca0566d61a09b79c3', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -6662, @Today)),
    (@m, N'Örnek_Kod.txt', 'ee17869414724d09a6ed97001103e6dc', 'text/plain', 220, DATEADD(MINUTE, -6662, @Today));

-- #125 BASE · murat01 · Ödev teslim tarihi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Ödev teslim tarihi', N'<p>Merhaba,</p><p>Ödev teslim tarihi bir hafta uzatıldı. Yönerge ekte.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -6628, @Today), DATEADD(MINUTE, -6628, @Today), DATEADD(MINUTE, -6628, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'Cc'),
    (@m, @oguz_Email, @oguz_Id, N'Bcc'),
    (@m, N'info@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ödev_Yönergesi.pdf', '3a84b92020c546618b4b2de17427ea77', 'application/pdf', 44845, DATEADD(MINUTE, -6628, @Today));

-- #126 REPLY · oguz01 · Re: Ödev teslim tarihi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Re: Ödev teslim tarihi', N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -6628, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @murat_Name + N'</b> &lt;' + @murat_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Ödev teslim tarihi bir hafta uzatıldı. Yönerge ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -6494, @Today), DATEADD(MINUTE, -6494, @Today), DATEADD(MINUTE, -6494, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL);

-- #127 FORWARD · oguz01 · Fwd: Ödev teslim tarihi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fwd: Ödev teslim tarihi', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @murat_Name + N' &lt;' + @murat_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -6628, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Ödev teslim tarihi' + N'<br><b>Kime:</b> ' + @ued_Email + N'<br><b>Bilgi:</b> ' + @umut_Email + N'</p>' + N'<p>Merhaba,</p><p>Ödev teslim tarihi bir hafta uzatıldı. Yönerge ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 7, DATEADD(MINUTE, -6146, @Today), DATEADD(MINUTE, -6146, @Today), DATEADD(MINUTE, -6146, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ödev_Yönergesi.pdf', '0fde4d702ed54cb2bb7801930706710c', 'application/pdf', 44845, DATEADD(MINUTE, -6146, @Today));

-- #128 FORWARD · oguz01 · Fwd: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Fwd: Ders notları', N'<p>Merhaba,</p><p>Konuyla ilgili mesajı iletiyorum, eklere bakabilirsin.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @murat_Name + N' &lt;' + @murat_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -6662, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Ders notları' + N'<br><b>Kime:</b> ' + @oguz_Email + N'</p>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 7, DATEADD(MINUTE, -5294, @Today), DATEADD(MINUTE, -5294, @Today), DATEADD(MINUTE, -5294, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', '3ee03fc01d0647eaac5be32187bbf4b9', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -5294, @Today)),
    (@m, N'Örnek_Kod.txt', '3eb7a486b0d34e90a5403b8a13a34825', 'text/plain', 220, DATEADD(MINUTE, -5294, @Today));

-- #129 BASE · oguz01 · Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Siparişiniz kargoda', N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -5121, @Today), DATEADD(MINUTE, -5121, @Today), DATEADD(MINUTE, -5121, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL);

-- #130 BASE · ued01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -5117, @Today), DATEADD(MINUTE, -5117, @Today), DATEADD(MINUTE, -5117, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #131 REPLY · murat01 · Re: Siparişiniz kargoda
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Re: Siparişiniz kargoda', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -5121, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Siparişiniz kargoya verildi. Tahmini teslim: 2 iş günü.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -5060, @Today), DATEADD(MINUTE, -5060, @Today), DATEADD(MINUTE, -5060, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #132 BASE · baris01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -4657, @Today), DATEADD(MINUTE, -4657, @Today), DATEADD(MINUTE, -4657, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #133 BASE · umut01 · Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Konser biletleri', N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -4652, @Today), DATEADD(MINUTE, -4652, @Today), DATEADD(MINUTE, -4652, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'deniz.kaya@example.com', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);

-- #134 BASE · baris01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -4648, @Today), DATEADD(MINUTE, -4648, @Today), DATEADD(MINUTE, -4648, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);

-- #135 TASLAK · oguz01 · Gezi için öneriler
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Gezi için öneriler', N'<p>Merhaba,</p><p>Gezi için birkaç yer önerim var:</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -4598, @Today), DATEADD(MINUTE, -4405, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, NULL, N'To'),
    (@m, N'selin.ak@example.com', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Draft', 1, 0, DATEADD(MINUTE, -4332, @Today), NULL);

-- #136 BASE · baris01 · Yeni ofis düzeni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Yeni ofis düzeni', N'<p>Merhaba,</p><p>Ofis yerleşim planı değişti. Yeni oturma planı ekte.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -4495, @Today), DATEADD(MINUTE, -4495, @Today), DATEADD(MINUTE, -4495, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #137 TASLAK · umut01 · Hata kaydı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Hata kaydı', N'<p>Merhaba,</p><p>Dün gördüğüm hatayı adım adım yazıyorum. Ekran görüntüsü ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -3756, @Today), DATEADD(MINUTE, -2879, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Draft', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hata_Ekranı.png', 'fa6bb687742d46db8788d5b5ba23b4a5', 'image/png', 7506, DATEADD(MINUTE, -3756, @Today));

-- #138 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -3547, @Today), DATEADD(MINUTE, -3547, @Today), DATEADD(MINUTE, -3547, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sprint_Görevleri.xlsx', 'b842084d698646249b00266ff05301f7', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5411, DATEADD(MINUTE, -3547, @Today));

-- #139 TASLAK · baris01 · Haftalık rapor
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Haftalık rapor', N'<p>Merhaba,</p><p>Bu haftanın raporu:</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -3513, @Today), DATEADD(MINUTE, -2654, @Today), NULL, NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Draft', 1, 0, DATEADD(MINUTE, -2535, @Today), NULL);

-- #140 ZAMANLANMIŞ · oguz01 · Cuma akşamı yemek
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Cuma akşamı yemek', N'<p>Merhaba,</p><p>Cuma akşamı için yer ayırttım, saat 19:30. Gelemeyecek olan haber versin.</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -3511, @Today), DATEADD(MINUTE, -3500, @Today), NULL, DATEADD(MINUTE, 4950, @Today), 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, NULL, N'To'),
    (@m, @murat_Email, NULL, N'To'),
    (@m, N'deniz.kaya@example.com', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Scheduled', 1, 0, NULL, NULL);

-- #141 ZAMANLANMIŞ · oguz01 · Toplantı saati değişti
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Toplantı saati değişti', N'<p>Merhaba,</p><p>Yarınki toplantıyı 11:00''e aldık, takvimini güncelleyebilir misin?</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -3420, @Today), DATEADD(MINUTE, -3408, @Today), NULL, DATEADD(MINUTE, -900, @Today), 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Scheduled', 1, 0, DATEADD(MINUTE, -2220, @Today), NULL);

-- #142 BASE · umut01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -3413, @Today), DATEADD(MINUTE, -3413, @Today), DATEADD(MINUTE, -3413, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);

-- #143 REPLY · umut01 · Re: Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Re: Sprint değerlendirmesi', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -3547, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -3410, @Today), DATEADD(MINUTE, -3410, @Today), DATEADD(MINUTE, -3410, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, DATEADD(MINUTE, -1769, @Today), NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -1463, @Today), NULL);

-- #144 BASE · oguz01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -3234, @Today), DATEADD(MINUTE, -3234, @Today), DATEADD(MINUTE, -3234, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #145 REPLY · baris01 · Re: Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Re: Kitap önerisi', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -3234, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -3127, @Today), DATEADD(MINUTE, -3127, @Today), DATEADD(MINUTE, -3127, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #146 BASE · murat01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -2394, @Today), DATEADD(MINUTE, -2394, @Today), DATEADD(MINUTE, -2394, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #147 BASE · oguz01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -2146, @Today), DATEADD(MINUTE, -2146, @Today), DATEADD(MINUTE, -2146, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL);

-- #148 BASE · oguz01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -2132, @Today), DATEADD(MINUTE, -2132, @Today), DATEADD(MINUTE, -2132, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -955, @Today), NULL),
    (@m, @baris_Id, N'Inbox', 0, 1, NULL, NULL);

-- #149 DIŞ-TESLİM-HATASI · oguz01 · Teklif dosyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Teklif dosyası', N'<p>Merhaba,</p><p>Teklif dosyasını ekte paylaşıyorum. Sorunuz olursa yazın.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -2020, @Today), DATEADD(MINUTE, -2020, @Today), DATEADD(MINUTE, -2020, @Today), NULL, 1);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To'),
    (@m, N'selin.ak@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Teklif.pdf', '7305974103834b46825bafe7896f0120', 'application/pdf', 44633, DATEADD(MINUTE, -2020, @Today));

-- #150 ZAMANLANMIŞ · umut01 · Ödev teslimi hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Ödev teslimi hatırlatması', N'<p>Merhaba,</p><p>Ödev teslimine iki gün kaldı, son kontrolleri yapmayı unutmayalım.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -1977, @Today), DATEADD(MINUTE, -1962, @Today), NULL, DATEADD(MINUTE, 3405, @Today), 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, NULL, N'To'),
    (@m, @ued_Email, NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Scheduled', 1, 0, NULL, NULL);

-- #151 BASE · umut01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -1752, @Today), DATEADD(MINUTE, -1752, @Today), DATEADD(MINUTE, -1752, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #152 BASE · ued01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -1708, @Today), DATEADD(MINUTE, -1708, @Today), DATEADD(MINUTE, -1708, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 1, NULL, NULL);

-- #153 ZAMANLANMIŞ · oguz01 · Haftalık rapor hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Haftalık rapor hatırlatması', N'<p>Merhaba,</p><p>Yarın sabah haftalık raporu birlikte gözden geçirelim. Taslak ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -893, @Today), DATEADD(MINUTE, -849, @Today), NULL, DATEADD(MINUTE, 1980, @Today), 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Scheduled', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Haftalık_Rapor.docx', '6839b82ac33d42d5883c11cebb46b51a', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36573, DATEADD(MINUTE, -893, @Today));

-- #154 BASE · baris01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -652, @Today), DATEADD(MINUTE, -652, @Today), DATEADD(MINUTE, -652, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -30, @Today));

-- #155 BASE · ued01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -554, @Today), DATEADD(MINUTE, -554, @Today), DATEADD(MINUTE, -554, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #156 BASE · baris01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -400, @Today), DATEADD(MINUTE, -400, @Today), DATEADD(MINUTE, -400, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fişler.pdf', 'cc7cc426a47943bc96344a4b8a3f456d', 'application/pdf', 44737, DATEADD(MINUTE, -400, @Today)),
    (@m, N'Masraf_Raporu.xlsx', 'eee9b2e837d1414bba4a7d141147acc0', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5405, DATEADD(MINUTE, -400, @Today));

-- #157 BASE · murat01 · Gezi programı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Gezi programı', N'<p>Merhaba,</p><p>Üç günlük gezi programını hazırladım, ekte bulabilirsiniz.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -283, @Today), DATEADD(MINUTE, -283, @Today), DATEADD(MINUTE, -283, @Today), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 1, NULL, NULL);

-- #158 BASE · ued01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -225, @Now), DATEADD(MINUTE, -225, @Now), DATEADD(MINUTE, -225, @Now), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #159 BASE · baris01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@baris_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -224, @Now), DATEADD(MINUTE, -224, @Now), DATEADD(MINUTE, -224, @Now), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #160 BASE · ued01 · Yeni ofis düzeni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@ued_Id, N'Yeni ofis düzeni', N'<p>Merhaba,</p><p>Ofis yerleşim planı değişti. Yeni oturma planı ekte.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -163, @Now), DATEADD(MINUTE, -163, @Now), DATEADD(MINUTE, -163, @Now), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @umut_Email, @umut_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @umut_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Oturma_Planı.png', '18aec7ce681b470ca71908b467fa102d', 'image/png', 8857, DATEADD(MINUTE, -163, @Now));

-- #161 BASE · murat01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@murat_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -139, @Now), DATEADD(MINUTE, -139, @Now), DATEADD(MINUTE, -139, @Now), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL);

-- #162 BASE · oguz01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@oguz_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -110, @Now), DATEADD(MINUTE, -110, @Now), DATEADD(MINUTE, -110, @Now), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);

-- #163 BASE · umut01 · Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate, ScheduledFor, ExternalDeliveryFailed)
VALUES (@umut_Id, N'Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -62, @Now), DATEADD(MINUTE, -62, @Now), DATEADD(MINUTE, -62, @Now), NULL, 0);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @umut_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Toplantı_Gündemi.pdf', '7d2835c0c8664c48a2635c74a4b406b2', 'application/pdf', 44792, DATEADD(MINUTE, -62, @Now));


/* ---------- Kontrol ---------- */
-- a) Toplamlar
SELECT (SELECT COUNT(*) FROM Messages)                         AS Mesaj,
       (SELECT COUNT(*) FROM Messages WHERE SentDate IS NULL AND ScheduledFor IS NULL) AS Taslak,
       (SELECT COUNT(*) FROM Messages WHERE ScheduledFor IS NOT NULL)                AS Zamanlanmis,
       (SELECT COUNT(*) FROM MessageRecipients)                AS AliciSatiri,
       (SELECT COUNT(*) FROM MailboxItems)                     AS Kopya,
       (SELECT COUNT(*) FROM Attachments)                      AS Ek;

-- b) Alıcı türleri
SELECT RecipientType, COUNT(*) AS Adet,
       SUM(CASE WHEN UserId IS NULL THEN 1 ELSE 0 END) AS KayitsizAdres
FROM MessageRecipients GROUP BY RecipientType;

-- c) Kullanıcı başına klasörler (uygulamadaki BuildScope koşullarıyla aynı)
SELECT u.UserName,
       SUM(CASE WHEN i.Folder = N'Inbox' AND i.DeletedDate IS NULL AND i.ArchivedDate IS NULL THEN 1 ELSE 0 END) AS GelenKutusu,
       SUM(CASE WHEN i.Folder = N'Inbox' AND i.DeletedDate IS NULL AND i.ArchivedDate IS NULL AND i.IsRead = 0 THEN 1 ELSE 0 END) AS Okunmamis,
       SUM(CASE WHEN i.Folder = N'Sent'  AND i.DeletedDate IS NULL THEN 1 ELSE 0 END) AS Gonderilen,
       SUM(CASE WHEN i.Folder = N'Draft' AND i.DeletedDate IS NULL THEN 1 ELSE 0 END) AS Taslak,
       SUM(CASE WHEN i.Folder = N'Scheduled' AND i.DeletedDate IS NULL THEN 1 ELSE 0 END) AS Zamanlanmis,
       SUM(CASE WHEN i.IsStarred = 1 AND i.DeletedDate IS NULL THEN 1 ELSE 0 END)     AS Yildizli,
       SUM(CASE WHEN i.Folder = N'Inbox' AND i.ArchivedDate IS NOT NULL AND i.DeletedDate IS NULL THEN 1 ELSE 0 END) AS Arsiv,
       SUM(CASE WHEN i.DeletedDate IS NOT NULL THEN 1 ELSE 0 END)                     AS Cop
FROM MailboxItems i JOIN AspNetUsers u ON u.Id = i.UserId
GROUP BY u.UserName ORDER BY u.UserName;

-- d) Gelecek tarihli kayıt olmamalı: 0  (ScheduledFor hariç; zamanlanmışların zamanı zaten ileride)
SELECT COUNT(*) AS GelecekTarihli FROM Messages WHERE SentDate > @Now OR UpdatedDate > @Now OR CreatedDate > @Now;

-- e) Folder yalnızca enum adlarından biri olmalı: 0
SELECT COUNT(*) AS GecersizFolder FROM MailboxItems WHERE Folder NOT IN (N'Inbox', N'Sent', N'Draft', N'Scheduled');

-- f) 20.3 örnekleri
SELECT
  (SELECT COUNT(*) FROM Messages WHERE SentDate IS NULL AND ScheduledFor > @Now)             AS ZamanlanmisGelecek,      -- 3
  (SELECT COUNT(*) FROM Messages WHERE SentDate IS NULL AND ScheduledFor <= @Now)            AS ZamaniGecmis,            -- 1 (çöpteki)
  (SELECT COUNT(*) FROM MailboxItems WHERE Folder = N'Scheduled' AND DeletedDate IS NOT NULL) AS CoptekiZamanlanmis,      -- 1
  (SELECT COUNT(*) FROM MailboxItems WHERE DeletedDate IS NOT NULL
     AND DeletedDate <= DATEADD(DAY, -27, @Now) AND DeletedDate > DATEADD(DAY, -30, @Now))  AS SuresiDolmakUzere,       -- en az 3
  (SELECT COUNT(*) FROM MailboxItems WHERE DeletedDate IS NOT NULL
     AND DeletedDate <= DATEADD(DAY, -30, @Now))                                            AS SuresiDolmus,            -- 0
  (SELECT COUNT(*) FROM Messages WHERE ExternalDeliveryFailed = 1)                           AS DisTeslimHatasi;         -- 1

-- Her şey doğruysa:  COMMIT;
-- Değilse:           ROLLBACK;
