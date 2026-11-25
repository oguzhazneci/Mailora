using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Giriş yapmış bir test istemcisi: token'ı her POST'a kendisi ekler (Kavram 2)
public sealed partial class SignedInClient(HttpClient http, TestUser user, string token)
{
    public HttpClient Http => http;
    public TestUser User => user;

    public Task<HttpResponseMessage> GetAsync(string url) =>
        http.GetAsync(url, TestContext.Current.CancellationToken);

    public Task<string> GetStringAsync(string url) =>
        http.GetStringAsync(url, TestContext.Current.CancellationToken);

    // Aynı anahtar birden çok kez olabilir (ids=1&ids=2): bu yüzden sözlük değil liste
    public Task<HttpResponseMessage> PostFormAsync(string url, params (string Key, string Value)[] fields)
    {
        var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                          .Append(new("__RequestVerificationToken", token));
        return http.PostAsync(url, new FormUrlEncodedContent(pairs), TestContext.Current.CancellationToken);
    }

    // Uygulamanın KENDİ gönderim ucuyla mesaj gönderir (Kavram 3). Gönderilen mesajın Id'sini döndürür.
    public async Task<int> SendMessageAsync(string subject, string to, string? cc = null, string? bcc = null,
                                            string body = "<p>Test mesajı</p>", int categoryId = 1)
    {
        var response = await PostFormAsync("/Message/CreateMessage",
            ("To", to), ("Cc", cc ?? ""), ("Bcc", bcc ?? ""),
            ("CategoryId", categoryId.ToString()), ("Subject", subject), ("Body", body),
            ("intent", "send"));

        // Başarılı gönderim → PRG ile Detay sayfasına yönlendirme (8.4)
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailUrl().Match(response.Headers.Location?.OriginalString ?? "");
        Assert.True(match.Success, $"Beklenmeyen yönlendirme: {response.Headers.Location}");

        return int.Parse(match.Groups[1].Value);
    }

    // 12.16'daki uçla ek yükler (taslak yoksa oluşturur). Taslak ve ek Id'lerini döndürür.
    public async Task<(int DraftId, int AttachmentId)> UploadAttachmentAsync(string fileName, byte[] content, int? draftId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        // İstemcinin söylediği tür önemsiz: sunucu türü kendi izin listesinden belirler (12.15.a Kavram 3)
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (draftId is int id)
        {
            form.Add(new StringContent(id.ToString()), "draftId");
        }
        form.Add(new StringContent(token), "__RequestVerificationToken");

        var response = await http.PostAsync("/Message/UploadAttachment", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (json.GetProperty("draftId").GetInt32(), json.GetProperty("attachment").GetProperty("id").GetInt32());
    }

    [GeneratedRegex(@"/Message/Detail/(\d+)")]
    private static partial Regex DetailUrl();
}