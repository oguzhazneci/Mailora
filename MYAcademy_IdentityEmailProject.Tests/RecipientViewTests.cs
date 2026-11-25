using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Messages;

namespace MYAcademy_IdentityEmailProject.Tests;

// RecipientView (12.11, 12.12): kim hangi alıcıyı görür, Yanıtla / Tümünü Yanıtla kime gider.
public class RecipientViewTests
{
    /* ---------- Senaryo (Kavram 1) ----------
       ada gönderiyor.  Kime: ued, kaan   Bilgi: baris   Gizli: can, deniz */
    private const string Ada = "u-ada", Ued = "u-ued", Kaan = "u-kaan", Baris = "u-baris", Can = "u-can", Deniz = "u-deniz";
    private const string AdaEmail = "ada@mailora.test";

    private static Message GroupMessage() => Build(Ada,
        ("ued@mailora.test", Ued, RecipientType.To),
        ("kaan@mailora.test", Kaan, RecipientType.To),
        ("baris@mailora.test", Baris, RecipientType.Cc),
        ("can@mailora.test", Can, RecipientType.Bcc),
        ("deniz@mailora.test", Deniz, RecipientType.Bcc));

    private static Message Build(string senderId, params (string Email, string? UserId, RecipientType Type)[] recipients)
    {
        var message = new Message { SenderId = senderId };
        foreach (var (email, userId, type) in recipients)
        {
            message.Recipients.Add(new MessageRecipient { Email = email, UserId = userId, RecipientType = type });
        }
        return message;
    }

    private static IEnumerable<string> Emails(IEnumerable<VisibleRecipient> list) => list.Select(r => r.Email);

    /* =====================================================================
       GÖRÜNÜRLÜK (12.11 tablosu)
       ===================================================================== */

    [Fact]
    public void For_Sender_SeesAllBccRecipients()
    {
        var rv = RecipientView.For(GroupMessage(), Ada);

        Assert.True(rv.ViewerIsSender);
        Assert.Equal(new[] { "can@mailora.test", "deniz@mailora.test" }, Emails(rv.Bcc));
        Assert.Equal(5, rv.Count);
    }

    [Fact]
    public void For_ToRecipient_SeesNoBccAndCountExcludesThem()
    {
        var rv = RecipientView.For(GroupMessage(), Ued);

        Assert.Empty(rv.Bcc);
        Assert.Equal(3, rv.Count);   // sayı da Gizli alıcıların VARLIĞINI ele vermemeli (12.11 Kavram 4)
    }

    [Fact]
    public void For_CcRecipient_SeesNoBccAndIsMarkedAsMeInCc()
    {
        var rv = RecipientView.For(GroupMessage(), Baris);

        Assert.Empty(rv.Bcc);
        var me = Assert.Single(rv.Cc);
        Assert.True(me.IsMe);
    }

    [Fact]
    public void For_BccRecipient_SeesOnlyHimselfInBcc()
    {
        var rv = RecipientView.For(GroupMessage(), Can);

        Assert.True(rv.ViewerIsBcc);
        var me = Assert.Single(rv.Bcc);
        Assert.Equal("can@mailora.test", me.Email);
        Assert.True(me.IsMe);
        Assert.DoesNotContain("deniz@mailora.test", Emails(rv.Bcc));   // diğer Gizli alıcı
        Assert.Equal(4, rv.Count);                                     // 2 Kime + 1 Bilgi + kendisi
    }

    [Fact]
    public void ForOutsider_HidesBccAndMarksNobodyAsMe()
    {
        var rv = RecipientView.ForOutsider(GroupMessage());

        Assert.Empty(rv.Bcc);
        Assert.All(rv.To.Concat(rv.Cc), r => Assert.False(r.IsMe));
    }

    // 12.11.d'de cevapsız kalan soru: dış (Mailora'ya kayıtlı olmayan) Gizli alıcının UserId'si null.
    // "Dışarıdan bakan" boş Id ile hiçbir satırla eşleşmemeli; null ile null eşleşseydi dış Gizli alıcılar sızardı.
    [Fact]
    public void ForOutsider_ExternalBccWithNullUserId_IsNotRevealed()
    {
        var message = Build(Ada,
            ("ued@mailora.test", Ued, RecipientType.To),
            ("dis@example.com", null, RecipientType.Bcc));

        var rv = RecipientView.ForOutsider(message);

        Assert.Empty(rv.Bcc);
    }

    /* =====================================================================
       YANIT HEDEFLERİ (12.12 tablosu)
       ===================================================================== */

    [Fact]
    public void Reply_FromToRecipient_GoesOnlyToSender()
    {
        var targets = RecipientView.For(GroupMessage(), Ued).GetReplyTargets(AdaEmail, all: false);

        Assert.Equal(new[] { AdaEmail }, targets.To);
        Assert.Empty(targets.Cc);
    }

    [Fact]
    public void ReplyAll_FromToRecipient_IncludesOthersButNeverSelfOrBcc()
    {
        var targets = RecipientView.For(GroupMessage(), Ued).GetReplyTargets(AdaEmail, all: true);

        Assert.Equal(new[] { AdaEmail, "kaan@mailora.test" }, targets.To);   // gönderen önce, kendisi yok
        Assert.Equal(new[] { "baris@mailora.test" }, targets.Cc);

        var everyone = targets.To.Concat(targets.Cc).ToList();
        Assert.DoesNotContain("can@mailora.test", everyone);
        Assert.DoesNotContain("deniz@mailora.test", everyone);
    }

    [Fact]
    public void ReplyAll_FromBccRecipient_FallsBackToPlainReply()
    {
        // URL'ye replyAll=true elle yazılsa bile (12.12.b Deney 5)
        var targets = RecipientView.For(GroupMessage(), Can).GetReplyTargets(AdaEmail, all: true);

        Assert.Equal(new[] { AdaEmail }, targets.To);
        Assert.Empty(targets.Cc);
    }

    [Fact]
    public void Reply_FromSender_GoesToAllToRecipients()
    {
        var targets = RecipientView.For(GroupMessage(), Ada).GetReplyTargets(AdaEmail, all: false);

        Assert.Equal(new[] { "ued@mailora.test", "kaan@mailora.test" }, targets.To);
        Assert.Empty(targets.Cc);
    }

    [Fact]
    public void ReplyAll_FromSender_AddsCcButNeverBcc()
    {
        // Gönderen Gizli listeyi GÖRÜR ama yanıta TAŞIYAMAZ: yanıtı yeni alıcılar okur (12.12.d, soru 2)
        var targets = RecipientView.For(GroupMessage(), Ada).GetReplyTargets(AdaEmail, all: true);

        Assert.Equal(new[] { "baris@mailora.test" }, targets.Cc);
        Assert.DoesNotContain("can@mailora.test", targets.To.Concat(targets.Cc));
    }

    [Fact]
    public void ReplyAll_SenderAlsoInToWithDifferentCase_AppearsOnce()
    {
        // ada kendini de Kime'ye yazmış (büyük harfle). ued Tümünü Yanıtla diyor (12.12.d, soru 3).
        var message = Build(Ada,
            ("ADA@MAILORA.TEST", Ada, RecipientType.To),
            ("ued@mailora.test", Ued, RecipientType.To));

        var targets = RecipientView.For(message, Ued).GetReplyTargets(AdaEmail, all: true);

        Assert.Equal(new[] { AdaEmail }, targets.To);
    }

    /* ---------- Tümünü Yanıtla butonu ne zaman görünür? ---------- */

    [Fact]
    public void CanReplyAll_ToRecipientInGroup_IsTrue()
    {
        Assert.True(RecipientView.For(GroupMessage(), Ued).CanReplyAll(AdaEmail));
    }

    [Fact]
    public void CanReplyAll_BccRecipient_IsFalse()
    {
        Assert.False(RecipientView.For(GroupMessage(), Can).CanReplyAll(AdaEmail));
    }

    [Fact]
    public void CanReplyAll_OneToOneMessage_IsFalse()
    {
        var message = Build(Ada, ("ued@mailora.test", Ued, RecipientType.To));

        Assert.False(RecipientView.For(message, Ued).CanReplyAll(AdaEmail));
    }

    [Fact]
    public void CanReplyAll_SenderWithTwoToAndNoCc_IsFalse()
    {
        // 12.12.d, soru 4: "all.Count > 1" kuralı burada gereksiz bir buton gösterirdi
        var message = Build(Ada,
            ("ued@mailora.test", Ued, RecipientType.To),
            ("kaan@mailora.test", Kaan, RecipientType.To));

        Assert.False(RecipientView.For(message, Ada).CanReplyAll(AdaEmail));
    }
}