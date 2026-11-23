using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Bir alıcının ekranda gösterilecek hali. IsMe: bakan kişinin kendisi mi ("siz" etiketi için).
    public sealed record VisibleRecipient(string Email, bool IsMe);

    // Yanıtla / Tümünü Yanıtla formunun Kime ve Bilgi alanlarına yazılacak adresler (12.12)
    public sealed record ReplyTargets(IReadOnlyList<string> To, IReadOnlyList<string> Cc)
    {
        public int Count => To.Count + Cc.Count;
    }

    // Bir mesajın alıcılarını BAKAN KİŞİYE GÖRE süzer (12.11).
    // Kural tek yerde: Detay, İlet künyesi ve Yanıtla / Tümünü Yanıtla bunu kullanır.
    //
    //   Bakan kişi            | Kime  | Bilgi | Gizli
    //   ----------------------|-------|-------|------------------
    //   Gönderen              | hepsi | hepsi | hepsi
    //   Kime / Bilgi alıcısı  | hepsi | hepsi | hiç
    //   Gizli alıcı           | hepsi | hepsi | sadece kendisi
    public sealed class RecipientView
    {
        public bool ViewerIsSender { get; }
        public IReadOnlyList<VisibleRecipient> To { get; }
        public IReadOnlyList<VisibleRecipient> Cc { get; }
        public IReadOnlyList<VisibleRecipient> Bcc { get; }

        // Sayı da süzülmüş listeden: Gizli alıcıların VARLIĞI bile sızmasın
        public int Count => To.Count + Cc.Count + Bcc.Count;

        // Bakan kişi bu mesajın Gizli alıcısı mı? Gönderen değilse ve Gizli listesinde
        // bir satır görebiliyorsa, For() kuralı gereği o satır kendisidir.
        public bool ViewerIsBcc => !ViewerIsSender && Bcc.Count > 0;

        private RecipientView(bool viewerIsSender,
                              List<VisibleRecipient> to,
                              List<VisibleRecipient> cc,
                              List<VisibleRecipient> bcc)
        {
            ViewerIsSender = viewerIsSender;
            To = to;
            Cc = cc;
            Bcc = bcc;
        }

        // message.Recipients yüklenmiş olmalı (Include). viewerId: sayfaya bakan kullanıcının Id'si.
        public static RecipientView For(Message message, string viewerId)
        {
            bool isSender = message.SenderId == viewerId;

            List<VisibleRecipient> Pick(RecipientType type, bool onlyMine) =>
                message.Recipients
                    .Where(r => r.RecipientType == type)
                    .Where(r => !onlyMine || r.UserId == viewerId)
                    .Select(r => new VisibleRecipient(r.Email, r.UserId == viewerId))
                    .ToList();

            return new RecipientView(
                isSender,
                Pick(RecipientType.To, onlyMine: false),
                Pick(RecipientType.Cc, onlyMine: false),
                // Gönderen hepsini görür; diğerleri yalnızca kendi Gizli satırını (yoksa boş liste)
                Pick(RecipientType.Bcc, onlyMine: !isSender));
        }

        // Mesajı ALICI OLMAYAN biri okuyacaksa (İlet künyesi): Gizli liste boş, kimse "siz" değil.
        // Boş Id hiçbir SenderId / UserId ile eşleşmez → "gönderen değil, Gizli alıcı da değil" satırına düşer.
        public static RecipientView ForOutsider(Message message) => For(message, string.Empty);

        // Yanıtla / Tümünü Yanıtla hedefleri (12.12). Kurallar:
        //  - Gizli liste ASLA hedeflere girmez (gönderen bile olsa)
        //  - Bakan kişi kendine yanıt yazmaz (IsMe olanlar çıkarılır)
        //  - Gizli alıcı Tümünü Yanıtla yapamaz → düz Yanıtla'ya düşer (karar 4-A)
        //  - Aynı adres iki kez yazılmaz; Kime önceliklidir (RecipientList.Parse ile aynı sıra)
        public ReplyTargets GetReplyTargets(string? senderEmail, bool all)
        {
            all = all && !ViewerIsBcc;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var to = new List<string>();
            var cc = new List<string>();

            void Add(List<string> list, string? email)
            {
                if (!string.IsNullOrWhiteSpace(email) && seen.Add(email))
                {
                    list.Add(email);
                }
            }

            if (ViewerIsSender)
            {
                // Kendi gönderdiğim mesaj: Yanıtla → Kime alıcılarına
                foreach (var r in To.Where(r => !r.IsMe)) Add(to, r.Email);
            }
            else
            {
                // Bana gelen mesaj: Yanıtla → gönderene; Tümünü → + diğer Kime alıcıları
                Add(to, senderEmail);
                if (all)
                {
                    foreach (var r in To.Where(r => !r.IsMe)) Add(to, r.Email);
                }
            }

            if (all)
            {
                foreach (var r in Cc.Where(r => !r.IsMe)) Add(cc, r.Email);
            }

            return new ReplyTargets(to, cc);
        }

        // "Tümünü Yanıtla" butonu yalnızca düz Yanıtla'ya EK birini kapsıyorsa anlamlı
        public bool CanReplyAll(string? senderEmail) =>
            !ViewerIsBcc &&
            GetReplyTargets(senderEmail, all: true).Count > GetReplyTargets(senderEmail, all: false).Count;
    }
}