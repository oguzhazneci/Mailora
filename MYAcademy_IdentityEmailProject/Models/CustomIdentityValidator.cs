using Microsoft.AspNetCore.Identity;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class CustomIdentityValidator : IdentityErrorDescriber
    {
        public override IdentityError DuplicateUserName(string userName)
            => new() { Code = nameof(DuplicateUserName), Description = $"\"{userName}\" kullanıcı adı zaten alınmış." };

        public override IdentityError DuplicateEmail(string email)
            => new() { Code = nameof(DuplicateEmail), Description = $"\"{email}\" adresiyle kayıtlı bir hesap zaten var." };

        public override IdentityError InvalidUserName(string? userName)
            => new() { Code = nameof(InvalidUserName), Description = "Kullanıcı adı yalnızca harf, rakam ve . _ - karakterlerinden oluşabilir." };

        public override IdentityError InvalidEmail(string? email)
            => new() { Code = nameof(InvalidEmail), Description = "Geçerli bir e-posta adresi girin." };

        public override IdentityError PasswordTooShort(int length)
            => new() { Code = nameof(PasswordTooShort), Description = $"Şifre en az {length} karakter olmalıdır." };

        public override IdentityError PasswordRequiresDigit()
            => new() { Code = nameof(PasswordRequiresDigit), Description = "Şifre en az bir rakam içermelidir." };

        public override IdentityError PasswordRequiresLower()
            => new() { Code = nameof(PasswordRequiresLower), Description = "Şifre en az bir küçük harf içermelidir." };

        public override IdentityError PasswordRequiresUpper()
            => new() { Code = nameof(PasswordRequiresUpper), Description = "Şifre en az bir büyük harf içermelidir." };

        public override IdentityError PasswordRequiresNonAlphanumeric()
            => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Şifre en az bir özel karakter içermelidir." };

        public override IdentityError PasswordMismatch()
            => new() { Code = nameof(PasswordMismatch), Description = "Mevcut şifre hatalı." };

        public override IdentityError InvalidToken()
            => new() { Code = nameof(InvalidToken), Description = "Bağlantı geçersiz veya süresi dolmuş." };
    }
}