using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MYAcademy_IdentityEmailProject.Tests;

// 16.7: Testte authenticator uygulamasının yaptığı hesabı yapar (RFC 6238: HMAC-SHA1, 30 saniye, 6 hane).
// Identity'deki hesaplayıcı internal; bu yüzden testte kendi kısa sürümümüz. Doğruluğu TotpTests'te RFC vektörleriyle.
public static class Totp
{
    // Uygulamanın verdiği Base32 anahtar + an → o anın kodu
    public static string Compute(string base32Key, DateTimeOffset time) =>
        Compute(Base32Decode(base32Key), time.ToUnixTimeSeconds() / 30);

    public static string Compute(byte[] key, long timestep)
    {
        // Zaman diliminin numarası 8 bayt, büyük uçtan (big-endian): RFC 4226'daki sayaç biçimi
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, timestep);
        byte[] hash = HMACSHA1.HashData(key, counter);

        // "Dinamik kesme": özetin son 4 bitinin gösterdiği yerden 4 bayt al, en üst biti sıfırla
        int offset = hash[^1] & 0x0F;
        int binary = (hash[offset] & 0x7F) << 24
                   | hash[offset + 1] << 16
                   | hash[offset + 2] << 8
                   | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6");   // baştaki sıfırlar korunur: 81804 değil "081804"
    }

    // RFC 4648 Base32 (A–Z ve 2–7). Identity anahtarı bu biçimde verir.
    public static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        string clean = input.Replace(" ", string.Empty).TrimEnd('=').ToUpperInvariant();

        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (char c in clean)
        {
            int value = alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException($"Geçersiz Base32 karakteri: {c}");
            }

            // Her karakter 5 bit; 8 bit biriktikçe bir bayt çıkar
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        return output.ToArray();
    }
}