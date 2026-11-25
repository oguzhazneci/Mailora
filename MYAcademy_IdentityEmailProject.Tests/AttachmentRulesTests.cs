using System.Text;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Tests;

// AttachmentRules (12.15): dosya adı temizleme, izin listesi ve dosya imzaları.
public class AttachmentRulesTests
{
    /* =====================================================================
       DOSYA ADI TEMİZLEME: ad ekranda ve indirmede kullanılır, diskte ASLA (12.15.a Kavram 2)
       ===================================================================== */

    [Theory]
    [InlineData(@"C:\Users\ada\Belgeler\rapor.pdf", "rapor.pdf")]   // bazı tarayıcılar tam yolu gönderir
    [InlineData("../../etc/rapor.txt", "rapor.txt")]                // Linux yol ayırıcısı
    [InlineData(@"..\..\appsettings.json", "appsettings.json")]     // yol manipülasyonu denemesi
    public void CleanFileName_PathInName_KeepsOnlyFileName(string raw, string expected)
    {
        Assert.Equal(expected, AttachmentRules.CleanFileName(raw));
    }

    [Fact]
    public void CleanFileName_WindowsInvalidCharacters_ReplacedWithUnderscore()
    {
        Assert.Equal("a_b_c_d_e_f_g_h.pdf", AttachmentRules.CleanFileName("a<b>c:d\"e|f?g*h.pdf"));
    }

    [Fact]
    public void CleanFileName_ControlCharacter_ReplacedWithUnderscore()
    {
        Assert.Equal("rap_or.pdf", AttachmentRules.CleanFileName("rap\u0001or.pdf"));
    }

    [Fact]
    public void CleanFileName_TrailingDotAndSpace_Removed()
    {
        // Windows "rapor.pdf." dosyasını "rapor.pdf" olarak kaydeder; uzantı kontrolü '.' görmemeli
        Assert.Equal("rapor.pdf", AttachmentRules.CleanFileName("rapor.pdf. "));
    }

    [Fact]
    public void CleanFileName_TurkishCharacters_Preserved()
    {
        Assert.Equal("Bütçe Özeti (v2).xlsx", AttachmentRules.CleanFileName("Bütçe Özeti (v2).xlsx"));
    }

    [Theory]
    [InlineData(".pdf", "ek.pdf")]   // adsız dosya
    [InlineData(null, "ek")]         // ad hiç gelmemiş
    public void CleanFileName_NoName_UsesDefaultName(string? raw, string expected)
    {
        Assert.Equal(expected, AttachmentRules.CleanFileName(raw));
    }

    [Fact]
    public void CleanFileName_TooLong_TruncatedKeepingExtension()
    {
        string cleaned = AttachmentRules.CleanFileName(new string('a', 300) + ".pdf");

        Assert.Equal(AttachmentRules.MaxFileNameLength, cleaned.Length);
        Assert.EndsWith(".pdf", cleaned);   // kısaltırken uzantı korunmalı, yoksa izin listesine takılır
    }

    /* =====================================================================
       İZİN LİSTESİ (karar 4-A: izin verileni say, yasaklananı değil)
       ===================================================================== */

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".PDF", "application/pdf")]   // büyük/küçük harf farksız
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".csv", "text/csv")]
    public void Find_AllowedExtension_ReturnsOurContentType(string extension, string expectedContentType)
    {
        var kind = AttachmentRules.Find(extension);

        Assert.NotNull(kind);
        Assert.Equal(expectedContentType, kind.ContentType);   // tür BİZİM tablomuzdan, tarayıcıdan değil (12.15.a Kavram 3)
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".html")]   // tarayıcıda açılırsa XSS
    [InlineData(".svg")]    // içinde script olabilen bir resim biçimi
    [InlineData(".docm")]   // makro içerebilen Office biçimi (12.15.c Kavram 2)
    [InlineData(".js")]
    [InlineData("")]        // uzantısız
    public void Find_NotAllowedExtension_ReturnsNull(string extension)
    {
        Assert.Null(AttachmentRules.Find(extension));
    }

    [Fact]
    public void AcceptAttribute_ListsAllowedAndOmitsDangerous()
    {
        Assert.Contains(".pdf", AttachmentRules.AcceptAttribute);
        Assert.DoesNotContain(".exe", AttachmentRules.AcceptAttribute);
    }

    /* =====================================================================
       DOSYA İMZALARI: içerik, uzantının iddia ettiği türde mi? (Kavram 1)
       ===================================================================== */

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    public static TheoryData<string, byte[], bool> Signatures => new()
    {
        { ".pdf",  Ascii("%PDF-1.7\n"), true },
        { ".pdf",  Ascii("<html><script>"), false },                          // adı .pdf yapılmış HTML
        { ".pdf",  Array.Empty<byte>(), false },                              // boş başlık
        { ".png",  new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, true },
        { ".png",  Ascii("%PDF-1.7"), false },                                // adı .png yapılmış PDF
        { ".jpg",  new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true },
        { ".gif",  Ascii("GIF89a"), true },
        { ".webp", Ascii("RIFF\0\0\0\0WEBPVP8 "), true },
        { ".webp", Ascii("RIFF\0\0\0\0AVI LIST"), false },                    // RIFF ama WebP değil
        { ".docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 }, true },  // ZIP kapsayıcısı
        { ".docx", Ascii("%PDF-1.7"), false },
        { ".zip",  new byte[] { 0x50, 0x4B, 0x05, 0x06 }, true },             // boş ZIP arşivi
        { ".txt",  Encoding.UTF8.GetBytes("Merhaba, Ğüşiöç"), true },
        { ".txt",  new byte[] { 0x48, 0x69, 0x00, 0x21 }, false },            // NUL byte: ikili içerik
        { ".csv",  new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x3B, 0x62 }, true },  // UTF-8 BOM + "a;b"
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Matches_HeaderBytes_AcceptsOnlyRealType(string extension, byte[] header, bool expected)
    {
        var kind = AttachmentRules.Find(extension);

        Assert.NotNull(kind);
        Assert.Equal(expected, kind.Matches(header));
    }

    /* =====================================================================
       DEĞİŞMEZLER (Kavram 2)
       ===================================================================== */

    [Fact]
    public void Limits_AreInternallyConsistent()
    {
        Assert.True(AttachmentRules.MaxFileBytes <= AttachmentRules.MaxTotalBytes,
            "Dosya başı sınır, toplam sınırdan büyük olamaz.");
        Assert.True(AttachmentRules.HeaderSize >= 12,
            "WebP imzası ilk 12 byte'a bakıyor; başlık daha kısa okunursa WebP dosyaları reddedilir.");
    }
}