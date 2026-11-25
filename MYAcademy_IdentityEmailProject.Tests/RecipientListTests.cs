using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Messages;

namespace MYAcademy_IdentityEmailProject.Tests;

// RecipientList (12.9): Kime / Bilgi / Gizli metinlerini ayrıştırma, tekilleştirme ve adres kuralları.
public class RecipientListTests
{
    /* ---------- Kural 1: aynı adres iki alanda olamaz, öncelik Kime > Bilgi > Gizli ---------- */

    [Fact]
    public void Parse_SameAddressInToAndCc_KeepsOnlyInTo()
    {
        var list = RecipientList.Parse("ada@mailora.test", "ADA@mailora.test", null);

        Assert.Single(list.To);
        Assert.Empty(list.Cc);
    }

    [Fact]
    public void Parse_SameAddressInCcAndBcc_KeepsOnlyInCc()
    {
        var list = RecipientList.Parse(null, "ada@mailora.test", "ada@mailora.test");

        Assert.Single(list.Cc);
        Assert.Empty(list.Bcc);   // Gizli'ye düşseydi "Bilgi'de görünen kişi aynı zamanda gizli" olurdu (12.11.d, soru 3)
    }

    /* ---------- Kural 2: aynı alanda tekrar → bir kez, ilk yazıldığı haliyle ---------- */

    [Fact]
    public void Parse_DuplicateInSameField_KeepsFirstSpelling()
    {
        var list = RecipientList.Parse("ada@mailora.test, Ada@Mailora.Test", null, null);

        // Assert.Single tek elemanı DÖNDÜRÜR: hem "tek" hem "şu" tek satırda
        string only = Assert.Single(list.To);
        Assert.Equal("ada@mailora.test", only);
    }

    /* ---------- Kural 3: ayırıcılar ve boş parçalar ---------- */

    [Theory]
    [InlineData("a@x.com,b@x.com")]
    [InlineData("a@x.com; b@x.com")]
    [InlineData("a@x.com b@x.com")]
    [InlineData("a@x.com\nb@x.com")]
    [InlineData("  a@x.com ,, b@x.com , ")]
    public void Parse_AnySeparator_SplitsIntoTrimmedAddresses(string raw)
    {
        var list = RecipientList.Parse(raw, null, null);

        // Koleksiyon karşılaştırması: eleman eleman VE sırasıyla
        Assert.Equal(new[] { "a@x.com", "b@x.com" }, list.To);
    }

    /* ---------- Kural 4: boş girdi ---------- */

    [Fact]
    public void Parse_NullEmptyAndWhitespace_ReturnsEmptyList()
    {
        var list = RecipientList.Parse(null, "", "   ");

        Assert.Equal(0, list.Count);
    }

    /* ---------- Kural 5: Parse sınır uygulamaz (sınırı ValidateRecipients uygular) ---------- */

    [Fact]
    public void Parse_MoreThanMaxRecipients_KeepsAllSoValidationCanReport()
    {
        string raw = string.Join(",", Enumerable.Range(1, RecipientList.MaxRecipients + 5).Select(i => $"u{i}@x.com"));

        var list = RecipientList.Parse(raw, null, null);

        // Kırpsaydı kullanıcı "21. adres neden gitmedi?" diye sorardı; hata mesajı "(şu an 25)" diyemezdi
        Assert.Equal(RecipientList.MaxRecipients + 5, list.Count);
    }

    /* ---------- Kural 6: All öncelik sırasıyla ve türleriyle ---------- */

    [Fact]
    public void All_ReturnsRecipientsInPriorityOrderWithTypes()
    {
        var list = RecipientList.Parse("t@x.com", "c@x.com", "b@x.com");

        var expected = new[]
        {
            ("t@x.com", RecipientType.To),
            ("c@x.com", RecipientType.Cc),
            ("b@x.com", RecipientType.Bcc)
        };
        Assert.Equal(expected, list.All);
    }

    /* ---------- Kural 7: adres biçimi (istemcideki EMAIL_RE ile aynı kural) ---------- */

    [Theory]
    [InlineData("ada@mailora.test")]
    [InlineData("ada.yilmaz+is@alt.ornek.com")]
    [InlineData("x@y.co")]
    public void IsValidAddress_WellFormedAddress_ReturnsTrue(string address)
    {
        Assert.True(RecipientList.IsValidAddress(address));
    }

    [Theory]
    [InlineData("")]                        // boş
    [InlineData("ada")]                     // @ yok
    [InlineData("ada@")]                    // alan adı yok
    [InlineData("@mailora.test")]           // kullanıcı kısmı yok
    [InlineData("ada@mailora")]             // noktalı alan adı yok
    [InlineData("ada@mailora.t")]           // uzantı tek harf
    [InlineData("ada @mailora.test")]       // boşluk
    [InlineData("ada@@mailora.test")]       // iki @
    [InlineData("a<b@mailora.test")]        // yasak karakter
    [InlineData("ada@mailora.test;")]       // sonda ayırıcı
    [InlineData("\"ada\"@mailora.test")]    // tırnak
    public void IsValidAddress_MalformedAddress_ReturnsFalse(string address)
    {
        Assert.False(RecipientList.IsValidAddress(address));
    }

    /* ---------- Kural 8: en fazla 256 karakter (sınır değerleri, Kavram 3) ---------- */

    [Fact]
    public void IsValidAddress_ExactlyMaxLength_ReturnsTrue()
    {
        const string domain = "@mailora.test";
        string address = new string('a', RecipientList.MaxAddressLength - domain.Length) + domain;

        Assert.Equal(RecipientList.MaxAddressLength, address.Length);   // testin kendi varsayımını da doğrula
        Assert.True(RecipientList.IsValidAddress(address));
    }

    [Fact]
    public void IsValidAddress_OneOverMaxLength_ReturnsFalse()
    {
        const string domain = "@mailora.test";
        string address = new string('a', RecipientList.MaxAddressLength - domain.Length + 1) + domain;

        Assert.False(RecipientList.IsValidAddress(address));
    }

    /* ---------- Join: formu yeniden doldururken kullanılan biçim ---------- */

    [Fact]
    public void Join_MultipleAddresses_UsesCommaAndSpace()
    {
        Assert.Equal("a@x.com, b@x.com", RecipientList.Join(new[] { "a@x.com", "b@x.com" }));
    }
}