namespace MYAcademy_IdentityEmailProject.Services
{
    // 17.2: appsettings.json → "RateLimiting" bölümü. Değerler istek anında okunur (Kavram 1).
    public class RateLimitRule
    {
        public int PermitLimit { get; set; }     // pencere başına izin verilen istek
        public int WindowSeconds { get; set; }   // pencerenin uzunluğu

        public bool IsValid => PermitLimit > 0 && WindowSeconds > 0;
    }

    public class RateLimitSettings
    {
        public const string SectionName = "RateLimiting";

        public RateLimitRule Auth { get; set; } = new();
        public RateLimitRule AuthMail { get; set; } = new();
        public RateLimitRule Lookup { get; set; } = new();
        public RateLimitRule UserSend { get; set; } = new();
        public RateLimitRule UserUpload { get; set; } = new();

        public bool IsValid => Auth.IsValid && AuthMail.IsValid && Lookup.IsValid && UserSend.IsValid && UserUpload.IsValid;
    }
}