/* =====================================================================
   Mailora — ZENGİN örnek veri seti (13.4)
   - Veritabanındaki BÜTÜN mesajları siler, yerine yaklaşık 150 mesaj yükler (son 30 gün)
   - Tek / çok alıcılı, Bilgi (Cc) ve Gizli (Bcc) alıcılı, Mailora dışı adresli mesajlar
   - "Re:" yanıt zincirleri (ikinci seviye dahil), "Fwd:" iletmeler (ekleriyle)
   - Her kullanıcıda: taslak, yıldızlı, arşiv, çöp ve okunmamış örnekleri
   - Eklerin dosyaları ayrı bir ZIP'te; adları buradaki StoredName değerleriyle aynı
   - Kullanıcılar: oguz01, ada01, ued01, baris01, murat01 (ahmet01 bilerek boş kalır)
   - Gövdeler <!--mailora-seed--> işaretlidir: 99-dashboard-seed-cleanup.sql bu mesajları bulur

   ÇALIŞTIRMA SIRASI (README / 13.4):
     1) Uygulamayı DURDUR (süpürücü çalışmasın)
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
DECLARE @ada_Id NVARCHAR(450), @ada_Email NVARCHAR(256), @ada_Name NVARCHAR(300);
SELECT @ada_Id = Id, @ada_Email = Email, @ada_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'ada01';
DECLARE @ued_Id NVARCHAR(450), @ued_Email NVARCHAR(256), @ued_Name NVARCHAR(300);
SELECT @ued_Id = Id, @ued_Email = Email, @ued_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'ued01';
DECLARE @baris_Id NVARCHAR(450), @baris_Email NVARCHAR(256), @baris_Name NVARCHAR(300);
SELECT @baris_Id = Id, @baris_Email = Email, @baris_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'baris01';
DECLARE @murat_Id NVARCHAR(450), @murat_Email NVARCHAR(256), @murat_Name NVARCHAR(300);
SELECT @murat_Id = Id, @murat_Email = Email, @murat_Name = CASE WHEN LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) = N'' THEN Email ELSE LTRIM(RTRIM(ISNULL(Name, N'') + N' ' + ISNULL(Surname, N''))) END FROM AspNetUsers WHERE UserName = N'murat01';

IF @oguz_Id IS NULL OR @ada_Id IS NULL OR @ued_Id IS NULL OR @baris_Id IS NULL OR @murat_Id IS NULL
BEGIN
    ;THROW 50001, N'Beş test kullanıcısından biri bulunamadı (oguz01, ada01, ued01, baris01, murat01).', 1;
END
IF (SELECT COUNT(*) FROM Categories WHERE CategoryId BETWEEN 1 AND 10) <> 10
BEGIN
    ;THROW 50002, N'1–10 numaralı kategoriler bulunamadı.', 1;
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

-- #1 BASE · murat01 · Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Yeni proje başlangıcı', N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -41222, @Today), DATEADD(MINUTE, -41222, @Today), DATEADD(MINUTE, -41222, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #2 BASE · ada01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -40992, @Today), DATEADD(MINUTE, -40992, @Today), DATEADD(MINUTE, -40992, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, N'info@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL);

-- #3 BASE · baris01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -40929, @Today), DATEADD(MINUTE, -40929, @Today), DATEADD(MINUTE, -40929, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -40761, @Today));

-- #4 REPLY · ada01 · Re: Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Masraf raporu', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -40929, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -40879, @Today), DATEADD(MINUTE, -40879, @Today), DATEADD(MINUTE, -40879, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #5 BASE · baris01 · Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -40635, @Today), DATEADD(MINUTE, -40635, @Today), DATEADD(MINUTE, -40635, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, N'deniz.kaya@example.com', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Toplantı_Gündemi.pdf', '594033274f39402dbec10415e860e3a1', 'application/pdf', 44792, DATEADD(MINUTE, -40635, @Today));

-- #6 BASE · ada01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -40482, @Today), DATEADD(MINUTE, -40482, @Today), DATEADD(MINUTE, -40482, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'muhasebe@example.org', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #7 REPLY · ued01 · Re: Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Masraf raporu', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -40992, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -40418, @Today), DATEADD(MINUTE, -40418, @Today), DATEADD(MINUTE, -40418, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, N'info@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, DATEADD(MINUTE, -39702, @Today), NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #8 BASE · ada01 · Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Haftalık özet', N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -39816, @Today), DATEADD(MINUTE, -39816, @Today), DATEADD(MINUTE, -39816, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #9 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -39741, @Today), DATEADD(MINUTE, -39741, @Today), DATEADD(MINUTE, -39741, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc'),
    (@m, N'muhasebe@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -39644, @Today)),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL);

-- #10 BASE · baris01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -39632, @Today), DATEADD(MINUTE, -39632, @Today), DATEADD(MINUTE, -39632, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, N'deniz.kaya@example.com', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, DATEADD(MINUTE, -38871, @Today), NULL);

-- #11 FORWARD · baris01 · Fwd: Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Fwd: Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Konuyla ilgili mesajı iletiyorum, eklere bakabilirsin.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @baris_Name + N' &lt;' + @baris_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -40635, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Haftalık proje toplantısı' + N'<br><b>Kime:</b> ' + @oguz_Email + N'</p>' + N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 1, DATEADD(MINUTE, -39602, @Today), DATEADD(MINUTE, -39602, @Today), DATEADD(MINUTE, -39602, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Toplantı_Gündemi.pdf', 'db0dde81f3cf473585c5efa04d2ea12b', 'application/pdf', 44792, DATEADD(MINUTE, -39602, @Today));

-- #12 BASE · ada01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -39027, @Today), DATEADD(MINUTE, -39027, @Today), DATEADD(MINUTE, -39027, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #13 BASE · ued01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -38260, @Today), DATEADD(MINUTE, -38260, @Today), DATEADD(MINUTE, -38260, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'info@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -36885, @Today)),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);

-- #14 BASE · ued01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -38161, @Today), DATEADD(MINUTE, -38161, @Today), DATEADD(MINUTE, -38161, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, N'info@example.org', NULL, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sürüm_Notları.txt', '1d740042ce7440fe82cc25b371ceab29', 'text/plain', 226, DATEADD(MINUTE, -38161, @Today));

-- #15 BASE · ued01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -37927, @Today), DATEADD(MINUTE, -37927, @Today), DATEADD(MINUTE, -37927, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);

-- #16 BASE · ada01 · Gezi programı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Gezi programı', N'<p>Merhaba,</p><p>Üç günlük gezi programını hazırladım, ekte bulabilirsiniz.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -37741, @Today), DATEADD(MINUTE, -37741, @Today), DATEADD(MINUTE, -37741, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Harita.jpg', '443851ffca0b449f8847ddbe05ced423', 'image/jpeg', 22344, DATEADD(MINUTE, -37741, @Today));

-- #17 FORWARD · oguz01 · Fwd: Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Fwd: Sunucu güncellemesi', N'<p>Merhaba,</p><p>Konuyla ilgili mesajı iletiyorum, eklere bakabilirsin.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @ued_Name + N' &lt;' + @ued_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -38161, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Sunucu güncellemesi' + N'<br><b>Kime:</b> ' + @murat_Email + N', ' + N'info@example.org' + N'</p>' + N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 10, DATEADD(MINUTE, -37698, @Today), DATEADD(MINUTE, -37698, @Today), DATEADD(MINUTE, -37698, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sürüm_Notları.txt', 'c99da266d72b4aadbefdbef6b8f85d5d', 'text/plain', 226, DATEADD(MINUTE, -37698, @Today));

-- #18 BASE · oguz01 · Sistem bakımı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sistem bakımı', N'<p>Merhaba,</p><p>Pazar gecesi 02:00–04:00 arasında planlı bakım yapılacaktır.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -36545, @Today), DATEADD(MINUTE, -36545, @Today), DATEADD(MINUTE, -36545, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 1, NULL, NULL);

-- #19 FORWARD · oguz01 · Fwd: Gezi programı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Fwd: Gezi programı', N'<p>Bilgin olsun, aşağıdaki mesaj senin de işine yarayabilir.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @ada_Name + N' &lt;' + @ada_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -37741, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Gezi programı' + N'<br><b>Kime:</b> ' + @oguz_Email + N'</p>' + N'<p>Merhaba,</p><p>Üç günlük gezi programını hazırladım, ekte bulabilirsiniz.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 9, DATEADD(MINUTE, -36421, @Today), DATEADD(MINUTE, -36421, @Today), DATEADD(MINUTE, -36421, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Harita.jpg', '3ce280d6b9d1486086560e219013bb69', 'image/jpeg', 22656, DATEADD(MINUTE, -36421, @Today));

-- #20 REPLY · ada01 · Re: Sistem bakımı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Sistem bakımı', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -36545, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Pazar gecesi 02:00–04:00 arasında planlı bakım yapılacaktır.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 5, DATEADD(MINUTE, -36318, @Today), DATEADD(MINUTE, -36318, @Today), DATEADD(MINUTE, -36318, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #21 BASE · oguz01 · Tatil günleri duyurusu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Tatil günleri duyurusu', N'<p>Merhaba,</p><p>Resmi tatil günlerinde ofis kapalı olacaktır.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -35332, @Today), DATEADD(MINUTE, -35332, @Today), DATEADD(MINUTE, -35332, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, DATEADD(MINUTE, -34209, @Today), NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #22 BASE · ued01 · Kod incelemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Kod incelemesi', N'<p>Merhaba,</p><p>Pull request''ine yorum bıraktım; birkaç küçük düzeltme yeterli.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -35320, @Today), DATEADD(MINUTE, -35320, @Today), DATEADD(MINUTE, -35320, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, N'info@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -34824, @Today), NULL);

-- #23 BASE · baris01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -35307, @Today), DATEADD(MINUTE, -35307, @Today), DATEADD(MINUTE, -35307, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #24 BASE · murat01 · Ödev teslim tarihi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Ödev teslim tarihi', N'<p>Merhaba,</p><p>Ödev teslim tarihi bir hafta uzatıldı. Yönerge ekte.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -35192, @Today), DATEADD(MINUTE, -35192, @Today), DATEADD(MINUTE, -35192, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);

-- #25 REPLY · ued01 · Re: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Ortak hediye', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -35307, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -35173, @Today), DATEADD(MINUTE, -35173, @Today), DATEADD(MINUTE, -35173, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #26 BASE · baris01 · Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Haftalık özet', N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -34984, @Today), DATEADD(MINUTE, -34984, @Today), DATEADD(MINUTE, -34984, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #27 BASE · ada01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -34954, @Today), DATEADD(MINUTE, -34954, @Today), DATEADD(MINUTE, -34954, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #28 REPLY · ada01 · Re: Ödev teslim tarihi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Ödev teslim tarihi', N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -35192, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @murat_Name + N'</b> &lt;' + @murat_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Ödev teslim tarihi bir hafta uzatıldı. Yönerge ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 7, DATEADD(MINUTE, -34865, @Today), DATEADD(MINUTE, -34865, @Today), DATEADD(MINUTE, -34865, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, DATEADD(MINUTE, -32893, @Today), NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #29 BASE · baris01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -34720, @Today), DATEADD(MINUTE, -34720, @Today), DATEADD(MINUTE, -34720, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 1, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #30 BASE · oguz01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -34683, @Today), DATEADD(MINUTE, -34683, @Today), DATEADD(MINUTE, -34683, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Bütçe_Q4.xlsx', 'ce24f41fdf764ce1905d7fd5a8a73652', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5412, DATEADD(MINUTE, -34683, @Today));

-- #31 REPLY · ued01 · Re: Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Bütçe tablosu güncellendi', N'<p>Teşekkürler, inceledim. Benim için uygun.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -34720, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -34473, @Today), DATEADD(MINUTE, -34473, @Today), DATEADD(MINUTE, -34473, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #32 REPLY · ued01 · Re: Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Bütçe tablosu güncellendi', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -34683, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -34342, @Today), DATEADD(MINUTE, -34342, @Today), DATEADD(MINUTE, -34342, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #33 BASE · baris01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -33901, @Today), DATEADD(MINUTE, -33901, @Today), DATEADD(MINUTE, -33901, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, N'info@example.org', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', '87f9f29a1c024c9f8a0d2c558c8f2630', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -33901, @Today)),
    (@m, N'Örnek_Kod.txt', 'cd0d457cc44a4354b31ee837f9676b51', 'text/plain', 220, DATEADD(MINUTE, -33901, @Today));

-- #34 BASE · murat01 · Kod incelemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Kod incelemesi', N'<p>Merhaba,</p><p>Pull request''ine yorum bıraktım; birkaç küçük düzeltme yeterli.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -33661, @Today), DATEADD(MINUTE, -33661, @Today), DATEADD(MINUTE, -33661, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'destek@example.net', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #35 FORWARD · oguz01 · Fwd: Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Fwd: Bütçe tablosu güncellendi', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @oguz_Name + N' &lt;' + @oguz_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -34683, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Bütçe tablosu güncellendi' + N'<br><b>Kime:</b> ' + @ued_Email + N'</p>' + N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 3, DATEADD(MINUTE, -33655, @Today), DATEADD(MINUTE, -33655, @Today), DATEADD(MINUTE, -33655, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Bütçe_Q4.xlsx', 'c15e4f0fd1e14c389985577b08a5ddad', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5412, DATEADD(MINUTE, -33655, @Today));

-- #36 FORWARD · baris01 · Fwd: Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Fwd: Ders notları', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @baris_Name + N' &lt;' + @baris_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -33901, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Ders notları' + N'<br><b>Kime:</b> ' + @murat_Email + N'<br><b>Bilgi:</b> ' + N'info@example.org' + N'</p>' + N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 7, DATEADD(MINUTE, -33331, @Today), DATEADD(MINUTE, -33331, @Today), DATEADD(MINUTE, -33331, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, N'deniz.kaya@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', '3f329c725e334ec28e530017c18e0d79', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -33331, @Today)),
    (@m, N'Örnek_Kod.txt', 'cc6c390242d8436597263a164e08b9fa', 'text/plain', 220, DATEADD(MINUTE, -33331, @Today));

-- #37 BASE · ued01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -32491, @Today), DATEADD(MINUTE, -32491, @Today), DATEADD(MINUTE, -32491, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #38 BASE · ada01 · Yeni ofis düzeni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Yeni ofis düzeni', N'<p>Merhaba,</p><p>Ofis yerleşim planı değişti. Yeni oturma planı ekte.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -32019, @Today), DATEADD(MINUTE, -32019, @Today), DATEADD(MINUTE, -32019, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @ued_Email, @ued_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -31697, @Today));

-- #39 BASE · ued01 · Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Yeni proje başlangıcı', N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -31956, @Today), DATEADD(MINUTE, -31956, @Today), DATEADD(MINUTE, -31956, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #40 REPLY · baris01 · Re: Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Re: Yeni proje başlangıcı', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -31956, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -31770, @Today), DATEADD(MINUTE, -31770, @Today), DATEADD(MINUTE, -31770, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, DATEADD(MINUTE, -29915, @Today), NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -31403, @Today));

-- #41 BASE · ada01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -31117, @Today), DATEADD(MINUTE, -31117, @Today), DATEADD(MINUTE, -31117, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #42 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -31023, @Today), DATEADD(MINUTE, -31023, @Today), DATEADD(MINUTE, -31023, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -29785, @Today));

-- #43 BASE · murat01 · Otel rezervasyonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Otel rezervasyonu', N'<p>Merhaba,</p><p>Otel rezervasyonu tamam, giriş 14:00 itibarıyla.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -30689, @Today), DATEADD(MINUTE, -30689, @Today), DATEADD(MINUTE, -30689, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, N'destek@example.net', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -29718, @Today));

-- #44 BASE · baris01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -30433, @Today), DATEADD(MINUTE, -30433, @Today), DATEADD(MINUTE, -30433, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sprint_Raporu.pdf', '86150045508c4068be4b8bfb57ce85b7', 'application/pdf', 44641, DATEADD(MINUTE, -30433, @Today));

-- #45 BASE · ued01 · Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Haftalık özet', N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -29483, @Today), DATEADD(MINUTE, -29483, @Today), DATEADD(MINUTE, -29483, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #46 BASE · oguz01 · Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -29343, @Today), DATEADD(MINUTE, -29343, @Today), DATEADD(MINUTE, -29343, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #47 BASE · oguz01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -29297, @Today), DATEADD(MINUTE, -29297, @Today), DATEADD(MINUTE, -29297, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #48 REPLY · baris01 · Re: Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Re: Haftalık proje toplantısı', N'<p>Tamamdır, ben de katılıyorum.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -29343, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -29136, @Today), DATEADD(MINUTE, -29136, @Today), DATEADD(MINUTE, -29136, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -27160, @Today), NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #49 REPLY · ued01 · Re: Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Uçuş bilgileri', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -29297, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 9, DATEADD(MINUTE, -28966, @Today), DATEADD(MINUTE, -28966, @Today), DATEADD(MINUTE, -28966, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #50 BASE · ada01 · Sistem bakımı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Sistem bakımı', N'<p>Merhaba,</p><p>Pazar gecesi 02:00–04:00 arasında planlı bakım yapılacaktır.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -27661, @Today), DATEADD(MINUTE, -27661, @Today), DATEADD(MINUTE, -27661, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #51 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -27604, @Today), DATEADD(MINUTE, -27604, @Today), DATEADD(MINUTE, -27604, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', '40e13abd74fe4ef8a4ba3f5f431545a5', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -27604, @Today)),
    (@m, N'Örnek_Kod.txt', 'b8fc43fb7a134903903cc61b3c77f386', 'text/plain', 220, DATEADD(MINUTE, -27604, @Today));

-- #52 BASE · oguz01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -27556, @Today), DATEADD(MINUTE, -27556, @Today), DATEADD(MINUTE, -27556, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #53 REPLY · murat01 · Re: Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Re: Kitap önerisi', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -27556, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -27392, @Today), DATEADD(MINUTE, -27392, @Today), DATEADD(MINUTE, -27392, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #54 BASE · ued01 · Gezi programı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Gezi programı', N'<p>Merhaba,</p><p>Üç günlük gezi programını hazırladım, ekte bulabilirsiniz.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -26566, @Today), DATEADD(MINUTE, -26566, @Today), DATEADD(MINUTE, -26566, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #55 BASE · oguz01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -26483, @Today), DATEADD(MINUTE, -26483, @Today), DATEADD(MINUTE, -26483, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @ada_Email, @ada_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Davetiye.jpg', 'f894de8ba4a14431aa1c7fc8a6cc3a14', 'image/jpeg', 23324, DATEADD(MINUTE, -26483, @Today));

-- #56 BASE · baris01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -26188, @Today), DATEADD(MINUTE, -26188, @Today), DATEADD(MINUTE, -26188, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, N'info@example.org', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #57 BASE · ada01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -26091, @Today), DATEADD(MINUTE, -26091, @Today), DATEADD(MINUTE, -26091, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #58 BASE · oguz01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -25304, @Today), DATEADD(MINUTE, -25304, @Today), DATEADD(MINUTE, -25304, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -24544, @Today));

-- #59 BASE · baris01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -25013, @Today), DATEADD(MINUTE, -25013, @Today), DATEADD(MINUTE, -25013, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @oguz_Email, @oguz_Id, N'Cc'),
    (@m, @murat_Email, @murat_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #60 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -24972, @Today), DATEADD(MINUTE, -24972, @Today), DATEADD(MINUTE, -24972, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, DATEADD(MINUTE, -24281, @Today));
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sprint_Görevleri.xlsx', '7b0c1eda12c54617b0ce22efe6d4395d', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5413, DATEADD(MINUTE, -24972, @Today)),
    (@m, N'Sprint_Raporu.pdf', '58d3e6df06e148b8acd0ca4c526a1dcc', 'application/pdf', 44641, DATEADD(MINUTE, -24972, @Today));

-- #61 BASE · ued01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -24968, @Today), DATEADD(MINUTE, -24968, @Today), DATEADD(MINUTE, -24968, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #62 BASE · baris01 · Film gecesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Film gecesi', N'<p>Selam,</p><p>Cuma akşamı film gecesi yapalım mı? Liste ekte, oylayalım.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -23699, @Today), DATEADD(MINUTE, -23699, @Today), DATEADD(MINUTE, -23699, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #63 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -23525, @Today), DATEADD(MINUTE, -23525, @Today), DATEADD(MINUTE, -23525, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);

-- #64 REPLY · ada01 · Re: Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Sprint değerlendirmesi', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -23525, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -23382, @Today), DATEADD(MINUTE, -23382, @Today), DATEADD(MINUTE, -23382, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #65 BASE · ada01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -23328, @Today), DATEADD(MINUTE, -23328, @Today), DATEADD(MINUTE, -23328, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #66 BASE · ada01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -22514, @Today), DATEADD(MINUTE, -22514, @Today), DATEADD(MINUTE, -22514, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -22281, @Today), NULL);

-- #67 BASE · oguz01 · Destek kaydı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Destek kaydı', N'<p>Merhaba,</p><p>Uygulamada gördüğüm hatayı bildirmek istiyorum. Ekran görüntüsü ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -22275, @Today), DATEADD(MINUTE, -22275, @Today), DATEADD(MINUTE, -22275, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, N'destek@example.net', NULL, N'Bcc'),
    (@m, N'info@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hata_Ekranı.png', 'bf65980bc0ad4ee0831237d2b5d896e0', 'image/png', 7505, DATEADD(MINUTE, -22275, @Today));

-- #68 BASE · oguz01 · Tatil günleri duyurusu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Tatil günleri duyurusu', N'<p>Merhaba,</p><p>Resmi tatil günlerinde ofis kapalı olacaktır.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -22120, @Today), DATEADD(MINUTE, -22120, @Today), DATEADD(MINUTE, -22120, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #69 REPLY · ued01 · Re: Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Uçuş bilgileri', N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -22514, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 9, DATEADD(MINUTE, -22065, @Today), DATEADD(MINUTE, -22065, @Today), DATEADD(MINUTE, -22065, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -21472, @Today), NULL);

-- #70 REPLY · ada01 · Re: Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Uçuş bilgileri', N'<p>Olur, gelecek hafta salı nasıl?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -22065, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -22514, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 9, DATEADD(MINUTE, -21889, @Today), DATEADD(MINUTE, -21889, @Today), DATEADD(MINUTE, -21889, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #71 BASE · ada01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -20622, @Today), DATEADD(MINUTE, -20622, @Today), DATEADD(MINUTE, -20622, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, DATEADD(MINUTE, -19645, @Today), NULL);

-- #72 BASE · baris01 · Cuma akşamı buluşma
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Cuma akşamı buluşma', N'<p>Merhaba,</p><p>Cuma akşamı ekip olarak yemeğe çıkıyoruz, katılır mısın?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -20523, @Today), DATEADD(MINUTE, -20523, @Today), DATEADD(MINUTE, -20523, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #73 BASE · ada01 · Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Haftalık özet', N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -20448, @Today), DATEADD(MINUTE, -20448, @Today), DATEADD(MINUTE, -20448, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 1, NULL, NULL);

-- #74 REPLY · baris01 · Re: Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Re: Fatura hatırlatması', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -20622, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 3, DATEADD(MINUTE, -20445, @Today), DATEADD(MINUTE, -20445, @Today), DATEADD(MINUTE, -20445, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #75 BASE · ued01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -19645, @Today), DATEADD(MINUTE, -19645, @Today), DATEADD(MINUTE, -19645, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);

-- #76 BASE · ued01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -19345, @Today), DATEADD(MINUTE, -19345, @Today), DATEADD(MINUTE, -19345, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'Cc'),
    (@m, @ada_Email, @ada_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #77 BASE · ued01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -19147, @Today), DATEADD(MINUTE, -19147, @Today), DATEADD(MINUTE, -19147, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #78 BASE · ada01 · İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'İzin talebi', N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -19024, @Today), DATEADD(MINUTE, -19024, @Today), DATEADD(MINUTE, -19024, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #79 BASE · oguz01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -18876, @Today), DATEADD(MINUTE, -18876, @Today), DATEADD(MINUTE, -18876, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 1, NULL, NULL);

-- #80 REPLY · oguz01 · Re: İzin talebi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Re: İzin talebi', N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -19024, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Önümüzdeki ayın ilk haftası için izin talebimi iletiyorum. Uygun görürseniz sevinirim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -18835, @Today), DATEADD(MINUTE, -18835, @Today), DATEADD(MINUTE, -18835, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #81 REPLY · ada01 · Re: İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: İndirim kampanyası', N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -18876, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -18451, @Today), DATEADD(MINUTE, -18451, @Today), DATEADD(MINUTE, -18451, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #82 REPLY · oguz01 · Re: İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Re: İndirim kampanyası', N'<p>Anlaştık, takvime ekledim.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -18451, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Süper fikir! Ben varım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -18876, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -18282, @Today), DATEADD(MINUTE, -18282, @Today), DATEADD(MINUTE, -18282, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #83 BASE · ada01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -17982, @Today), DATEADD(MINUTE, -17982, @Today), DATEADD(MINUTE, -17982, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #84 BASE · oguz01 · Bütçe tablosu güncellendi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Bütçe tablosu güncellendi', N'<p>Merhaba,</p><p>Çeyrek bütçe tablosunu güncelledim. Sapmalar <b>kırmızı</b> ile işaretli.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -17846, @Today), DATEADD(MINUTE, -17846, @Today), DATEADD(MINUTE, -17846, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 1, NULL, NULL);

-- #85 BASE · oguz01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -17512, @Today), DATEADD(MINUTE, -17512, @Today), DATEADD(MINUTE, -17512, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'Cc'),
    (@m, @murat_Email, @murat_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sürüm_Notları.txt', 'a9f715e628444ddf97cb193c9ccd5cb2', 'text/plain', 226, DATEADD(MINUTE, -17512, @Today));

-- #86 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -17485, @Today), DATEADD(MINUTE, -17485, @Today), DATEADD(MINUTE, -17485, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #87 BASE · murat01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -17458, @Today), DATEADD(MINUTE, -17458, @Today), DATEADD(MINUTE, -17458, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 1, NULL, NULL);

-- #88 BASE · baris01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -17452, @Today), DATEADD(MINUTE, -17452, @Today), DATEADD(MINUTE, -17452, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #89 REPLY · ada01 · Re: Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Sunucu güncellemesi', N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -17512, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 10, DATEADD(MINUTE, -17040, @Today), DATEADD(MINUTE, -17040, @Today), DATEADD(MINUTE, -17040, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -16288, @Today));

-- #90 REPLY · oguz01 · Re: Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Re: Sunucu güncellemesi', N'<p>Evet, son tarih kesin. Kolay gelsin.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -17040, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Harika, ekleri aldım. Yarın dönüş yaparım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -17512, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 10, DATEADD(MINUTE, -16920, @Today), DATEADD(MINUTE, -16920, @Today), DATEADD(MINUTE, -16920, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);

-- #91 BASE · ued01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -16529, @Today), DATEADD(MINUTE, -16529, @Today), DATEADD(MINUTE, -16529, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #92 BASE · ada01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -16262, @Today), DATEADD(MINUTE, -16262, @Today), DATEADD(MINUTE, -16262, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #93 BASE · baris01 · Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Konser biletleri', N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -15266, @Today), DATEADD(MINUTE, -15266, @Today), DATEADD(MINUTE, -15266, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Konser_Programı.png', 'f4863b0fc39a4e8db7ae54f3a149a2ef', 'image/png', 10572, DATEADD(MINUTE, -15266, @Today));

-- #94 REPLY · oguz01 · Re: Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Re: Konser biletleri', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -15266, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 4, DATEADD(MINUTE, -15205, @Today), DATEADD(MINUTE, -15205, @Today), DATEADD(MINUTE, -15205, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #95 TASLAK · baris01 · Ödev hakkında soru
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Ödev hakkında soru', N'<p>Merhaba,</p><p>Ödevin ikinci kısmında bir noktayı tam anlayamadım:</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -15040, @Today), DATEADD(MINUTE, -14157, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Draft', 1, 0, NULL, NULL);

-- #96 FORWARD · baris01 · Fwd: Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Fwd: Konser biletleri', N'<p>Merhaba,</p><p>Konuyla ilgili mesajı iletiyorum, eklere bakabilirsin.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @baris_Name + N' &lt;' + @baris_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -15266, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Konser biletleri' + N'<br><b>Kime:</b> ' + @oguz_Email + N'</p>' + N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 4, DATEADD(MINUTE, -14517, @Today), DATEADD(MINUTE, -14517, @Today), DATEADD(MINUTE, -14517, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Konser_Programı.png', '3af15d3198554799814f4aa6d9a50acb', 'image/png', 10603, DATEADD(MINUTE, -14517, @Today));

-- #97 BASE · baris01 · Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Haftalık özet', N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -13190, @Today), DATEADD(MINUTE, -13190, @Today), DATEADD(MINUTE, -13190, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #98 REPLY · ued01 · Re: Haftalık özet
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Haftalık özet', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -13190, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Bu haftaki etkinliklerinizin özeti hazır. Ayrıntılı tablo ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 5, DATEADD(MINUTE, -13112, @Today), DATEADD(MINUTE, -13112, @Today), DATEADD(MINUTE, -13112, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #99 BASE · murat01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -12414, @Today), DATEADD(MINUTE, -12414, @Today), DATEADD(MINUTE, -12414, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #100 BASE · baris01 · Piknik organizasyonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Piknik organizasyonu', N'<p>Merhaba,</p><p>Pazar günü piknik için yer ayırttık. Kim ne getirecek, listeyi doldurursanız sevinirim.</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -12305, @Today), DATEADD(MINUTE, -12305, @Today), DATEADD(MINUTE, -12305, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);

-- #101 TASLAK · oguz01 · Gezi için öneriler
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Gezi için öneriler', N'<p>Merhaba,</p><p>Gezi için birkaç yer önerim var:</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -11690, @Today), DATEADD(MINUTE, -11419, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, NULL, N'To'),
    (@m, N'deniz.kaya@example.com', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Draft', 1, 0, NULL, NULL);

-- #102 TASLAK · ada01 · Hata kaydı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Hata kaydı', N'<p>Merhaba,</p><p>Dün gördüğüm hatayı adım adım yazıyorum. Ekran görüntüsü ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -10563, @Today), DATEADD(MINUTE, -9767, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Draft', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Hata_Ekranı.png', '4edd9e2acbdf48a4b2aa4def0438144e', 'image/png', 7504, DATEADD(MINUTE, -10563, @Today));

-- #103 BASE · oguz01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -9584, @Today), DATEADD(MINUTE, -9584, @Today), DATEADD(MINUTE, -9584, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc'),
    (@m, N'selin.ak@example.com', NULL, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #104 BASE · baris01 · Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -9565, @Today), DATEADD(MINUTE, -9565, @Today), DATEADD(MINUTE, -9565, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -8280, @Today)),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #105 BASE · murat01 · Yeni ofis düzeni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Yeni ofis düzeni', N'<p>Merhaba,</p><p>Ofis yerleşim planı değişti. Yeni oturma planı ekte.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -9522, @Today), DATEADD(MINUTE, -9522, @Today), DATEADD(MINUTE, -9522, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, DATEADD(MINUTE, -8824, @Today), NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #106 TASLAK · murat01 · Duyuru metni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Duyuru metni', N'<p>Değerli çalışma arkadaşlarımız,</p><p>…</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -9437, @Today), DATEADD(MINUTE, -9020, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Draft', 1, 0, NULL, NULL);

-- #107 BASE · oguz01 · Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Uçuş bilgileri', N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->', 9, DATEADD(MINUTE, -9327, @Today), DATEADD(MINUTE, -9327, @Today), DATEADD(MINUTE, -9327, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -7525, @Today), NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Biniş_Kartı.pdf', 'fadd5372d9084994a9af770aa1712a26', 'application/pdf', 44782, DATEADD(MINUTE, -9327, @Today));

-- #108 REPLY · ada01 · Re: Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Re: Haftalık proje toplantısı', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -9565, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -9248, @Today), DATEADD(MINUTE, -9248, @Today), DATEADD(MINUTE, -9248, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL);

-- #109 BASE · ada01 · Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Yeni proje başlangıcı', N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -8962, @Today), DATEADD(MINUTE, -8962, @Today), DATEADD(MINUTE, -8962, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Proje_Planı.xlsx', 'b12a924013b24d268586f30f496ffbf8', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5409, DATEADD(MINUTE, -8962, @Today));

-- #110 FORWARD · ada01 · Fwd: Uçuş bilgileri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Fwd: Uçuş bilgileri', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @oguz_Name + N' &lt;' + @oguz_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -9327, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Uçuş bilgileri' + N'<br><b>Kime:</b> ' + @ada_Email + N'</p>' + N'<p>Merhaba,</p><p>Uçuş saatimiz değişti: kalkış 07:45. Biniş kartları ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 9, DATEADD(MINUTE, -8846, @Today), DATEADD(MINUTE, -8846, @Today), DATEADD(MINUTE, -8846, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, N'selin.ak@example.com', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Biniş_Kartı.pdf', '805e3b7a7ed142ac86bb5bbadf590d70', 'application/pdf', 44782, DATEADD(MINUTE, -8846, @Today));

-- #111 BASE · murat01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -8842, @Today), DATEADD(MINUTE, -8842, @Today), DATEADD(MINUTE, -8842, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', 'ed07cfc906c44dfaa8c30700440b269a', 'application/pdf', 44671, DATEADD(MINUTE, -8842, @Today));

-- #112 FORWARD · murat01 · Fwd: Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Fwd: Fatura hatırlatması', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @murat_Name + N' &lt;' + @murat_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -8842, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Fatura hatırlatması' + N'<br><b>Kime:</b> ' + @ada_Email + N'</p>' + N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 3, DATEADD(MINUTE, -8128, @Today), DATEADD(MINUTE, -8128, @Today), DATEADD(MINUTE, -8128, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', 'a14bf7165fd541bd90dab08607c69fb6', 'application/pdf', 44671, DATEADD(MINUTE, -8128, @Today));

-- #113 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -7931, @Today), DATEADD(MINUTE, -7931, @Today), DATEADD(MINUTE, -7931, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sprint_Görevleri.xlsx', '1a4943d26422482b8549f00b707514ae', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5413, DATEADD(MINUTE, -7931, @Today));

-- #114 FORWARD · ada01 · Fwd: Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Fwd: Yeni proje başlangıcı', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @ada_Name + N' &lt;' + @ada_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -8962, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Yeni proje başlangıcı' + N'<br><b>Kime:</b> ' + @murat_Email + N'</p>' + N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 1, DATEADD(MINUTE, -7873, @Today), DATEADD(MINUTE, -7873, @Today), DATEADD(MINUTE, -7873, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Proje_Planı.xlsx', '42163f8ff3d24b53b42c65701f3dfc85', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5409, DATEADD(MINUTE, -7873, @Today));

-- #115 TASLAK · ada01 · Hafta sonu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Hafta sonu', N'<p>Selam,</p><p>Cumartesi için şöyle düşünmüştüm…</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -7869, @Today), DATEADD(MINUTE, -7350, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Draft', 1, 0, NULL, NULL);

-- #116 BASE · ued01 · Fatura hatırlatması
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Fatura hatırlatması', N'<p>Merhaba,</p><p>Bu ayın faturasının son ödeme tarihi yaklaşıyor. Fatura ekte.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -7824, @Today), DATEADD(MINUTE, -7824, @Today), DATEADD(MINUTE, -7824, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Fatura_Kasım.pdf', '1c8264af5b334c249a9d3e2e5ba937e5', 'application/pdf', 44671, DATEADD(MINUTE, -7824, @Today));

-- #117 FORWARD · oguz01 · Fwd: Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Fwd: Sprint değerlendirmesi', N'<p>Göz atar mısın? Senin de görüşünü almak istedim.</p><p><br></p><p>---------- İletilen mesaj ----------<br><b>Kimden:</b> ' + @oguz_Name + N' &lt;' + @oguz_Email + N'&gt;<br><b>Tarih:</b> ' + FORMAT(DATEADD(MINUTE, -7931, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N'<br><b>Konu:</b> Sprint değerlendirmesi' + N'<br><b>Kime:</b> ' + @ada_Email + N'</p>' + N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->' + N'<!--mailora-seed-->', 1, DATEADD(MINUTE, -7714, @Today), DATEADD(MINUTE, -7714, @Today), DATEADD(MINUTE, -7714, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Sprint_Görevleri.xlsx', '23ba5c5f29fd492ebbc72fe9428612f1', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5413, DATEADD(MINUTE, -7714, @Today));

-- #118 BASE · ued01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -7708, @Today), DATEADD(MINUTE, -7708, @Today), DATEADD(MINUTE, -7708, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 1, NULL, NULL);

-- #119 BASE · ued01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -7604, @Today), DATEADD(MINUTE, -7604, @Today), DATEADD(MINUTE, -7604, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -6290, @Today), NULL);

-- #120 BASE · ada01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -7382, @Today), DATEADD(MINUTE, -7382, @Today), DATEADD(MINUTE, -7382, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #121 REPLY · murat01 · Re: Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Re: Ortak hediye', N'<p>Bilgi için teşekkürler. Bir sorum olacak: son tarih kesinleşti mi?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -7382, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ada_Name + N'</b> &lt;' + @ada_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 8, DATEADD(MINUTE, -7301, @Today), DATEADD(MINUTE, -7301, @Today), DATEADD(MINUTE, -7301, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);

-- #122 REPLY · murat01 · Re: Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Re: Sprint değerlendirmesi', N'<p>Elinize sağlık. Birkaç küçük notum var, toplantıda konuşalım.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -7604, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 1, DATEADD(MINUTE, -7161, @Today), DATEADD(MINUTE, -7161, @Today), DATEADD(MINUTE, -7161, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #123 BASE · oguz01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -6311, @Today), DATEADD(MINUTE, -6311, @Today), DATEADD(MINUTE, -6311, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, N'muhasebe@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #124 BASE · oguz01 · Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Kitap önerisi', N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -6204, @Today), DATEADD(MINUTE, -6204, @Today), DATEADD(MINUTE, -6204, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #125 BASE · oguz01 · Konser biletleri
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Konser biletleri', N'<p>Merhaba,</p><p>Önümüzdeki ayki konser için biletler satışa çıktı. Birlikte alalım mı?</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -6142, @Today), DATEADD(MINUTE, -6142, @Today), DATEADD(MINUTE, -6142, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #126 BASE · murat01 · Ders notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Ders notları', N'<p>Merhaba,</p><p>Bugünkü dersin notlarını paylaşıyorum. Konular: <i>Identity, EF Core, migration</i>.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -6071, @Today), DATEADD(MINUTE, -6071, @Today), DATEADD(MINUTE, -6071, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -5459, @Today));
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ders_Notları.docx', '6659444adce74b78a2def29f21461f86', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36569, DATEADD(MINUTE, -6071, @Today)),
    (@m, N'Örnek_Kod.txt', 'e0a6e9f28a964db48c0e3050e1ec290e', 'text/plain', 220, DATEADD(MINUTE, -6071, @Today));

-- #127 BASE · murat01 · Ödev teslim tarihi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Ödev teslim tarihi', N'<p>Merhaba,</p><p>Ödev teslim tarihi bir hafta uzatıldı. Yönerge ekte.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -6062, @Today), DATEADD(MINUTE, -6062, @Today), DATEADD(MINUTE, -6062, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'Cc'),
    (@m, @oguz_Email, @oguz_Id, N'Bcc'),
    (@m, N'info@example.org', NULL, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -5534, @Today));
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Ödev_Yönergesi.pdf', '70c9958fdeee47a1b28a935343c0319f', 'application/pdf', 44845, DATEADD(MINUTE, -6062, @Today));

-- #128 REPLY · baris01 · Re: Kitap önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Re: Kitap önerisi', N'<p>Teşekkürler, inceledim. Benim için uygun.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -6204, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @oguz_Name + N'</b> &lt;' + @oguz_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Geçen gün bahsettiğim kitabı bitirdim, kesinlikle tavsiye ederim.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -5725, @Today), DATEADD(MINUTE, -5725, @Today), DATEADD(MINUTE, -5725, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #129 BASE · oguz01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -5020, @Today), DATEADD(MINUTE, -5020, @Today), DATEADD(MINUTE, -5020, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, N'selin.ak@example.com', NULL, N'Cc'),
    (@m, @murat_Email, @murat_Id, N'Bcc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Geri_Bildirimler.csv', '941855dfefab4d739c8ae036d4eab358', 'text/csv', 689, DATEADD(MINUTE, -5020, @Today));

-- #130 BASE · ada01 · Sunum taslağı hakkında
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Sunum taslağı hakkında', N'<p>Merhaba,</p><p>Sunum taslağına göz atabildin mi? Özellikle <b>maliyet</b> bölümü için yorumlarını bekliyorum.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -4959, @Today), DATEADD(MINUTE, -4959, @Today), DATEADD(MINUTE, -4959, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -4780, @Today));

-- #131 BASE · ued01 · Sprint değerlendirmesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Sprint değerlendirmesi', N'<p>Merhaba ekip,</p><p>Bu sprintte tamamlanan işler:</p><ul><li>Giriş ekranı</li><li>Raporlama modülü</li></ul><p>Ayrıntılar ekte. Kalanları cuma konuşalım.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -4852, @Today), DATEADD(MINUTE, -4852, @Today), DATEADD(MINUTE, -4852, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, DATEADD(MINUTE, -4398, @Today)),
    (@m, @murat_Id, N'Inbox', 0, 1, NULL, NULL);

-- #132 BASE · murat01 · Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Yeni proje başlangıcı', N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -4536, @Today), DATEADD(MINUTE, -4536, @Today), DATEADD(MINUTE, -4536, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #133 TASLAK · ued01 · (konusuz)
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'', N'<p>Cuma için mekan önerisi…</p><!--mailora-seed-->', 4, DATEADD(MINUTE, -3651, @Today), DATEADD(MINUTE, -3344, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Draft', 1, 0, NULL, NULL);

-- #134 BASE · baris01 · Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Doğum günü sürprizi', N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -3304, @Today), DATEADD(MINUTE, -3304, @Today), DATEADD(MINUTE, -3304, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'Cc'),
    (@m, @oguz_Email, @oguz_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #135 BASE · ada01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -3246, @Today), DATEADD(MINUTE, -3246, @Today), DATEADD(MINUTE, -3246, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To'),
    (@m, @murat_Email, @murat_Id, N'To'),
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, DATEADD(MINUTE, -1996, @Today), NULL);

-- #136 REPLY · ued01 · Re: Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Re: Doğum günü sürprizi', N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -3304, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -3087, @Today), DATEADD(MINUTE, -3087, @Today), DATEADD(MINUTE, -3087, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To'),
    (@m, @ada_Email, @ada_Id, N'Cc'),
    (@m, @oguz_Email, @oguz_Id, N'Cc');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, DATEADD(MINUTE, -2128, @Today), NULL);

-- #137 REPLY · baris01 · Re: Doğum günü sürprizi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Re: Doğum günü sürprizi', N'<p>Anlaştık, takvime ekledim.</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -3087, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @ued_Name + N'</b> &lt;' + @ued_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Bu hafta biraz yoğunum, gelecek haftaya alabilir miyiz?</p><p><br></p><p>' + FORMAT(DATEADD(MINUTE, -3304, @Today), N'd MMMM yyyy dddd HH:mm', N'tr-TR') + N' tarihinde <b>' + @baris_Name + N'</b> &lt;' + @baris_Email + N' &gt; yazdı:</p><blockquote>' + N'<p>Selam,</p><p>Cumartesi akşamı küçük bir sürpriz hazırlıyoruz. Gelebilir misin? Davetiye ekte.</p><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->' + N'</blockquote><!--mailora-seed-->', 2, DATEADD(MINUTE, -2974, @Today), DATEADD(MINUTE, -2974, @Today), DATEADD(MINUTE, -2974, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 0, NULL, NULL);

-- #138 BASE · ued01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -2312, @Today), DATEADD(MINUTE, -2312, @Today), DATEADD(MINUTE, -2312, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 1, 0, NULL, NULL);

-- #139 TASLAK · murat01 · Bütçe önerisi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Bütçe önerisi', N'<p>Merhaba,</p><p>Gelecek çeyrek için bütçe önerimi ekliyorum. Kısa bir açıklama yazacağım.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -2174, @Today), DATEADD(MINUTE, -1828, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Draft', 1, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Bütçe_Önerisi.xlsx', '4920d67e39854640843a0917db8fade7', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 5408, DATEADD(MINUTE, -2174, @Today));

-- #140 BASE · ada01 · Ortak hediye
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Ortak hediye', N'<p>Merhaba,</p><p>Doğum günü için ortak hediye alalım dedik. Seçenekler ekte, fikrinizi yazın.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -2160, @Today), DATEADD(MINUTE, -2160, @Today), DATEADD(MINUTE, -2160, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To'),
    (@m, N'info@example.org', NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 1, 1, NULL, NULL);

-- #141 TASLAK · baris01 · Haftalık rapor
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Haftalık rapor', N'<p>Merhaba,</p><p>Bu haftanın raporu:</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -2071, @Today), DATEADD(MINUTE, -1962, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Draft', 1, 0, DATEADD(MINUTE, -1863, @Today), NULL);

-- #142 BASE · ada01 · Yeni proje başlangıcı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Yeni proje başlangıcı', N'<p>Merhaba ekip,</p><p>Yeni projenin başlangıç toplantısı pazartesi 14:00''te. Herkesin katılımı önemli.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -2006, @Today), DATEADD(MINUTE, -2006, @Today), DATEADD(MINUTE, -2006, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 1, 0, NULL, NULL);

-- #143 BASE · ada01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -1957, @Today), DATEADD(MINUTE, -1957, @Today), DATEADD(MINUTE, -1957, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #144 BASE · baris01 · İndirim kampanyası
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'İndirim kampanyası', N'<p>Merhaba,</p><p>Sezon sonu indirimleri başladı, seçili ürünlerde %30.</p><!--mailora-seed-->', 8, DATEADD(MINUTE, -1828, @Today), DATEADD(MINUTE, -1828, @Today), DATEADD(MINUTE, -1828, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 1, NULL, NULL);

-- #145 TASLAK · ued01 · (konusuz)
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'', N'<p>Hatırlatma: faturayı kontrol et.</p><!--mailora-seed-->', NULL, DATEADD(MINUTE, -860, @Today), DATEADD(MINUTE, -30, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Draft', 1, 0, NULL, NULL);

-- #146 BASE · ued01 · Hesap güvenliği bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Hesap güvenliği bildirimi', N'<p>Merhaba,</p><p>Hesabınıza yeni bir cihazdan giriş yapıldı. Siz değilseniz şifrenizi değiştirin.</p><!--mailora-seed-->', 5, DATEADD(MINUTE, -554, @Today), DATEADD(MINUTE, -554, @Today), DATEADD(MINUTE, -554, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #147 BASE · murat01 · Masraf raporu
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Masraf raporu', N'<p>Merhaba,</p><p>Geçen ayın masraf raporunu ekte bulabilirsin. Onayını bekliyorum.</p><!--mailora-seed-->', 3, DATEADD(MINUTE, -523, @Today), DATEADD(MINUTE, -523, @Today), DATEADD(MINUTE, -523, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 1, 0, NULL, NULL);

-- #148 TASLAK · oguz01 · Toplantı notları
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Toplantı notları', N'<p>Merhaba,</p><p>Bugünkü toplantının notlarını derliyorum, yarın tamamlayıp göndereceğim:</p><ul><li>Takvim</li><li>Bütçe</li></ul><!--mailora-seed-->', 1, DATEADD(MINUTE, -433, @Today), DATEADD(MINUTE, -225, @Today), NULL);
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, NULL, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Draft', 1, 0, DATEADD(MINUTE, -30, @Today), NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Toplantı_Notları.docx', '1662ea44e7854ea892c36ca2e1870483', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 36573, DATEADD(MINUTE, -433, @Today));

-- #149 BASE · murat01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -150, @Today), DATEADD(MINUTE, -150, @Today), DATEADD(MINUTE, -150, @Today));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 1, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);

-- #150 BASE · ada01 · Haftalık proje toplantısı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ada_Id, N'Haftalık proje toplantısı', N'<p>Merhaba,</p><p>Haftalık proje toplantımızı perşembe 10:00''da yapalım. Gündem ekte.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -208, @Now), DATEADD(MINUTE, -208, @Now), DATEADD(MINUTE, -208, @Now));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ada_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Toplantı_Gündemi.pdf', 'bbbc87757680497e84564d49773a9cdb', 'application/pdf', 44792, DATEADD(MINUTE, -208, @Now));

-- #151 BASE · oguz01 · Eğitim kaydı onayı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@oguz_Id, N'Eğitim kaydı onayı', N'<p>Merhaba,</p><p>ASP.NET Core eğitimine kaydınız onaylandı. İlk ders salı günü.</p><!--mailora-seed-->', 7, DATEADD(MINUTE, -178, @Now), DATEADD(MINUTE, -178, @Now), DATEADD(MINUTE, -178, @Now));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ued_Email, @ued_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @oguz_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ued_Id, N'Inbox', 0, 0, NULL, NULL);

-- #152 BASE · ued01 · Hafta sonu planı
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Hafta sonu planı', N'<p>Selam,</p><p>Hafta sonu için bir planın var mı? Hava güzel olursa sahilde yürüyüş yapabiliriz. Rotayı ekledim.</p><!--mailora-seed-->', 2, DATEADD(MINUTE, -114, @Now), DATEADD(MINUTE, -114, @Now), DATEADD(MINUTE, -114, @Now));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @murat_Email, @murat_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @murat_Id, N'Inbox', 0, 0, NULL, NULL);

-- #153 BASE · murat01 · Müşteri geri bildirimi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@murat_Id, N'Müşteri geri bildirimi', N'<p>Merhaba,</p><p>Müşteriden gelen geri bildirimleri derledim. Genel memnuniyet yüksek, iki küçük talep var.</p><!--mailora-seed-->', 1, DATEADD(MINUTE, -92, @Now), DATEADD(MINUTE, -92, @Now), DATEADD(MINUTE, -92, @Now));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @baris_Email, @baris_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @murat_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @baris_Id, N'Inbox', 0, 0, NULL, NULL);

-- #154 BASE · ued01 · Yeni ofis düzeni
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@ued_Id, N'Yeni ofis düzeni', N'<p>Merhaba,</p><p>Ofis yerleşim planı değişti. Yeni oturma planı ekte.</p><!--mailora-seed-->', 6, DATEADD(MINUTE, -77, @Now), DATEADD(MINUTE, -77, @Now), DATEADD(MINUTE, -77, @Now));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @ada_Email, @ada_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @ued_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @ada_Id, N'Inbox', 0, 0, NULL, NULL);
INSERT INTO Attachments (MessageId, FileName, StoredName, ContentType, SizeBytes, CreatedDate) VALUES
    (@m, N'Oturma_Planı.png', '62fc4e16f5d5497d879a7049ea5c3937', 'image/png', 8830, DATEADD(MINUTE, -77, @Now));

-- #155 BASE · baris01 · Sunucu güncellemesi
INSERT INTO Messages (SenderId, Subject, Body, CategoryId, CreatedDate, UpdatedDate, SentDate)
VALUES (@baris_Id, N'Sunucu güncellemesi', N'<p>Merhaba,</p><p>Test sunucusu .NET 10''a güncellendi. Bir sorun görürsen haber ver. Sürüm notları ekte.</p><!--mailora-seed-->', 10, DATEADD(MINUTE, -51, @Now), DATEADD(MINUTE, -51, @Now), DATEADD(MINUTE, -51, @Now));
SET @m = SCOPE_IDENTITY();
INSERT INTO MessageRecipients (MessageId, Email, UserId, RecipientType) VALUES
    (@m, @oguz_Email, @oguz_Id, N'To');
INSERT INTO MailboxItems (MessageId, UserId, Folder, IsRead, IsStarred, DeletedDate, ArchivedDate) VALUES
    (@m, @baris_Id, N'Sent', 1, 0, NULL, NULL),
    (@m, @oguz_Id, N'Inbox', 0, 0, NULL, NULL);


/* ---------- Kontrol ---------- */
-- a) Toplamlar
SELECT (SELECT COUNT(*) FROM Messages)                         AS Mesaj,
       (SELECT COUNT(*) FROM Messages WHERE SentDate IS NULL)  AS Taslak,
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
       SUM(CASE WHEN i.IsStarred = 1 AND i.DeletedDate IS NULL THEN 1 ELSE 0 END)     AS Yildizli,
       SUM(CASE WHEN i.Folder = N'Inbox' AND i.ArchivedDate IS NOT NULL AND i.DeletedDate IS NULL THEN 1 ELSE 0 END) AS Arsiv,
       SUM(CASE WHEN i.DeletedDate IS NOT NULL THEN 1 ELSE 0 END)                     AS Cop
FROM MailboxItems i JOIN AspNetUsers u ON u.Id = i.UserId
GROUP BY u.UserName ORDER BY u.UserName;

-- d) Gelecek tarihli kayıt olmamalı: 0
SELECT COUNT(*) AS GelecekTarihli FROM Messages WHERE SentDate > @Now OR UpdatedDate > @Now;

-- Her şey doğruysa:  COMMIT;
-- Değilse:           ROLLBACK;
