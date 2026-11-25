/* Mailora — Dashboard test verisini SİL (11.1)
   Sadece gövdesinde <!--mailora-seed--> işareti olan mesajlar silinir.
   MessageRecipients, MailboxItems ve Attachments satırları veritabanındaki ON DELETE CASCADE ile gider (5.3, 12.15).
   Bu mesajlara sonradan ek eklendiyse diskteki dosyaları sahipsiz kalır; OrphanAttachmentCleaner toplar (12.19). */

SET XACT_ABORT ON;

-- 13.4: Aynı bağlantıda önceki çalıştırmanın transaction'ı açık kaldıysa DURDUR.
-- (11.1'deki ders: @@TRANCOUNT = 2 olunca COMMIT sadece sayacı düşürür, hiçbir şey kaydedilmez.)
IF @@TRANCOUNT > 0
BEGIN
    ;THROW 50000, N'Bu bağlantıda açık bir transaction var. Önce COMMIT ya da ROLLBACK çalıştırın.', 1;
END
BEGIN TRANSACTION;

DELETE FROM Messages WHERE Body LIKE N'%mailora-seed%';
SELECT @@ROWCOUNT AS SilinenMesaj;

-- Kontrol: kalan işaretli mesaj 0 olmalı
SELECT COUNT(*) AS KalanSeed FROM Messages WHERE Body LIKE N'%mailora-seed%';

-- Doğruysa: COMMIT;   Değilse: ROLLBACK;
