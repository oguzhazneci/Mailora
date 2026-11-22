namespace MYAcademy_IdentityEmailProject.Models.Layout
{
    // _Avatar partial'ının girdisi (10.4).
    // Name: baş harfler için, Key: renk için (genelde e-posta), PhotoFile: {guid}.jpg ya da null,
    // CssClass: boyut gibi ek sınıflar (örn. "pf-avatar").
    public record AvatarViewModel(string? Name, string? Key, string? PhotoFile, string CssClass = "");
}