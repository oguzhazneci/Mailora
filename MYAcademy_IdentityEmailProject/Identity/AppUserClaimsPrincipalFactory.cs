using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Identity
{
    // Giriş anında çereze konacak claim'leri üretir.
    // Identity'nin standart claim'lerine ad ve soyadı ekler.
    public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, IdentityRole>
    {
        public AppUserClaimsPrincipalFactory(UserManager<AppUser> userManager,
                                             RoleManager<IdentityRole> roleManager,
                                             IOptions<IdentityOptions> options)
            : base(userManager, roleManager, options)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
        {
            // Önce Identity'nin kendi claim'leri (Id, kullanıcı adı, e-posta, security stamp...)
            var identity = await base.GenerateClaimsAsync(user);

            if (!string.IsNullOrWhiteSpace(user.Name))
                identity.AddClaim(new Claim(ClaimTypes.GivenName, user.Name));

            if (!string.IsNullOrWhiteSpace(user.Surname))
                identity.AddClaim(new Claim(ClaimTypes.Surname, user.Surname));

            return identity;
        }
    }
}