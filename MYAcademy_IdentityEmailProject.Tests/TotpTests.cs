namespace MYAcademy_IdentityEmailProject.Tests;

// Test yardımcısının kendisi doğru mu? (16.7 Kavram 2) Yanlışsa 2FA testleri yanlış yerde kırmızı olur.
public class TotpTests
{
    // RFC 6238 Ek B'deki SHA-1 anahtarı: ASCII "12345678901234567890", Base32 hali aşağıda
    private const string RfcKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public void Base32Decode_ProducesTheRfcKey()
    {
        Assert.Equal("12345678901234567890"u8.ToArray(), Totp.Base32Decode(RfcKey));
    }

    // RFC 8 haneli kod veriyor; 6 haneli kod, onun son 6 hanesi
    [Theory]
    [InlineData(59L, "287082")]           // RFC: 94287082
    [InlineData(1111111109L, "081804")]   // RFC: 07081804 → baştaki sıfır korunmalı
    [InlineData(2000000000L, "279037")]   // RFC: 69279037
    public void Compute_MatchesRfc6238Vectors(long unixSeconds, string expected)
    {
        Assert.Equal(expected, Totp.Compute(RfcKey, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)));
    }
}