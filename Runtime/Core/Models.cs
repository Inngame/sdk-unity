using System;
using System.Collections.Generic;

namespace InngameSDK
{
    public enum InngameAuthor { User, Team, Ai, System }

    /// <summary>Delivery state of a message on this device.</summary>
    public enum InngameMessageState { Sending, Sent, Failed }

    public enum PushKind { Apns, ApnsSandbox, Fcm }

    public enum InngameLogLevel { None, Error, Warning, Info, Verbose }

    public sealed class InngameAttachment
    {
        public string Type;
        public long Size;
        public int Width;
        public int Height;
        public string Name;
        public string Url;
        public bool IsImage => Type != null && Type.StartsWith("image/", StringComparison.Ordinal);

        internal static InngameAttachment FromJson(Dictionary<string, object> d) => new InngameAttachment
        {
            Type = d.GetString("type"),
            Size = d.GetLong("size"),
            Width = (int)d.GetLong("width"),
            Height = (int)d.GetLong("height"),
            Name = d.GetString("name"),
            Url = d.GetString("url"),
        };

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["type"] = Type, ["size"] = Size, ["width"] = Width, ["height"] = Height, ["name"] = Name, ["url"] = Url,
        };
    }

    /// <summary>An item granted by a card (the game grants it server-side; the SDK only notifies the game).</summary>
    public sealed class InngameItem
    {
        public string Id;
        public string Name;
        public int Quantity = 1;
        public string MessageId;
    }

    /// <summary>Interactive card sent by the team or the AI (review, link, screen, item, offer…).</summary>
    public sealed class InngameCard
    {
        public string Kind;
        public string Label;
        public string Url;
        public string Screen;
        public string Value;
        public string OfferId;
        public InngameItem Item;
        public string LastAction;
        /// <summary>Satisfaction cards ("rating"): the player's score from 1 to 5, once rated.</summary>
        public int? Rating;
        /// <summary>The full card object as sent by the server (unknown keys included).</summary>
        public Dictionary<string, object> Raw;
        /// <summary>True when <see cref="Label"/> is the server's own English default ("defaultLabel": true, or a known default
        /// from before that flag): the chat then shows its translated text instead.</summary>
        public bool UsesDefaultLabel;

        internal static string ServerDefaultLabel(string kind) => kind == "review" ? "Rate the game" : kind == "refund" ? "Request a refund" : null;

        /// <summary>The team's label, or null when the chat should use its own translated text for this kind of card.</summary>
        public string CustomLabel => string.IsNullOrEmpty(Label) || UsesDefaultLabel ? null : Label;

        internal static InngameCard FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var c = new InngameCard
            {
                Kind = d.GetString("kind"),
                Label = d.GetString("label"),
                Url = d.GetString("url"),
                Screen = d.GetString("screen"),
                Value = d.GetString("value"),
                OfferId = d.GetString("offerId"),
                LastAction = d.GetString("lastAction"),
                Raw = d,
            };
            c.UsesDefaultLabel = d.GetBool("defaultLabel", false) || (c.Label != null && ServerDefaultLabel(c.Kind) == c.Label);
            var rating = d.GetLong("rating", 0);
            if (rating >= 1 && rating <= 5) c.Rating = (int)rating;
            var it = d.GetDict("item");
            if (it != null) c.Item = new InngameItem { Id = it.GetString("id"), Name = it.GetString("name"), Quantity = (int)Math.Max(1, it.GetLong("quantity", 1)) };
            return c;
        }
    }

    public sealed class InngameMessage
    {
        public string Id;
        public string ConversationId;
        public long Seq;
        public InngameAuthor Author;
        public string Body = "";
        public string Lang;
        /// <summary>Translations keyed by base language ("fr", "ja"…).</summary>
        public Dictionary<string, string> Translations = new Dictionary<string, string>();
        public List<InngameAttachment> Attachments = new List<InngameAttachment>();
        public InngameCard Card;
        public string Status;
        /// <summary>Unix time in milliseconds.</summary>
        public long CreatedAt;
        public long? ReadAt;
        public string AuthorName;
        public string AuthorAvatar;

        // ---- local only ----
        public string ClientId;
        public InngameMessageState State = InngameMessageState.Sent;
        /// <summary>UI toggle: show the original text instead of the translation.</summary>
        public bool ShowOriginal;
        /// <summary>Local preview textures of attachments being sent.</summary>
        internal List<UnityEngine.Texture2D> LocalPreviews;

        public bool IsMine => Author == InngameAuthor.User;
        public DateTime CreatedAtLocal => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime;

        internal static InngameAuthor ParseAuthor(string s)
        {
            switch (s)
            {
                case "user": return InngameAuthor.User;
                case "ai": return InngameAuthor.Ai;
                case "system": return InngameAuthor.System;
                default: return InngameAuthor.Team;
            }
        }

        static string AuthorString(InngameAuthor a) => a switch
        {
            InngameAuthor.User => "user",
            InngameAuthor.Ai => "ai",
            InngameAuthor.System => "system",
            _ => "team",
        };

        public static InngameMessage FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var m = new InngameMessage
            {
                Id = d.GetString("id"),
                ConversationId = d.GetString("conversationId"),
                Seq = d.GetLong("seq"),
                Author = ParseAuthor(d.GetString("authorType")),
                Body = d.GetString("body") ?? "",
                Lang = d.GetString("lang"),
                Status = d.GetString("status"),
                CreatedAt = d.GetLong("createdAt"),
                ReadAt = d.GetNullableLong("readAt"),
                Card = InngameCard.FromJson(d.GetDict("card")),
                ClientId = d.GetString("clientId"),
            };
            var tr = d.GetDict("translations");
            if (tr != null) foreach (var kv in tr) if (kv.Value is string s && !string.IsNullOrEmpty(s)) m.Translations[kv.Key] = s;
            var atts = d.GetList("attachments");
            if (atts != null) foreach (var a in atts) if (a is Dictionary<string, object> ad) m.Attachments.Add(InngameAttachment.FromJson(ad));
            var author = d.GetDict("author");
            if (author != null) { m.AuthorName = author.GetString("name"); m.AuthorAvatar = author.GetString("avatar"); }
            if (d.TryGetValue("state", out var st) && st is string ss && Enum.TryParse(ss, out InngameMessageState parsed)) m.State = parsed;
            return m;
        }

        /// <summary>Serialized form used by the offline cache (same shape as the server, plus local state).</summary>
        internal Dictionary<string, object> ToJson()
        {
            var atts = new List<object>();
            foreach (var a in Attachments) atts.Add(a.ToJson());
            var d = new Dictionary<string, object>
            {
                ["id"] = Id, ["conversationId"] = ConversationId, ["seq"] = Seq, ["authorType"] = AuthorString(Author),
                ["body"] = Body, ["lang"] = Lang, ["translations"] = Translations, ["attachments"] = atts,
                ["card"] = Card?.Raw, ["status"] = Status, ["createdAt"] = CreatedAt, ["readAt"] = ReadAt,
                ["clientId"] = ClientId, ["state"] = State.ToString(),
            };
            if (AuthorName != null || AuthorAvatar != null) d["author"] = new Dictionary<string, object> { ["name"] = AuthorName, ["avatar"] = AuthorAvatar };
            return d;
        }
    }

    /// <summary>Remote configuration returned by the server (<c>config</c> of POST /v1/sdk/session).</summary>
    public sealed class InngameRemoteConfig
    {
        public Dictionary<string, object> Appearance = new Dictionary<string, object>();
        public bool PhotosEnabled = true;
        public bool VerifiedOnly;
        public bool Translation = true;
        public bool Faq;
        /// <summary>Whether the team is around right now and how fast it usually replies (office hours set in the inbox).</summary>
        public InngameAvailability Availability = new InngameAvailability();

        internal static InngameRemoteConfig FromJson(Dictionary<string, object> d)
        {
            var c = new InngameRemoteConfig();
            if (d == null) return c;
            c.Appearance = d.GetDict("appearance") ?? new Dictionary<string, object>();
            c.PhotosEnabled = d.GetBool("photosEnabled", true);
            c.VerifiedOnly = d.GetBool("verifiedOnly");
            c.Translation = d.GetBool("translation", true);
            c.Faq = d.GetBool("faq");
            var av = d.GetDict("availability");
            if (av != null)
                c.Availability = new InngameAvailability { Online = av.GetBool("online", true), ReplyTime = av.GetString("replyTime"), NextOpenAt = av.GetLong("nextOpenAt", 0) };
            return c;
        }
    }

    public sealed class InngameAvailability
    {
        public bool Online = true;
        /// <summary>"minutes", "hours", "day", or null when there is too little history (or the studio hides it).</summary>
        public string ReplyTime;
        /// <summary>When the team is away: next opening, Unix ms (0 if unknown).</summary>
        public long NextOpenAt;
    }

    /// <summary>The player as seen by the server.</summary>
    public sealed class InngameUser
    {
        public string Id;
        public string Name;
        public bool Verified;
        public bool Identified;
        public bool Blocked;
        public string Language;
        /// <summary>
        /// UUID issued by Inngame for this player. Pass it to the store so subscription events find the player:
        /// StoreKit <c>appAccountToken</c>, Google Play <c>obfuscatedAccountId</c>, or the RevenueCat / Adapty / Superwall app user id.
        /// </summary>
        public string PurchaseToken;

        internal static InngameUser FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new InngameUser
            {
                Id = d.GetString("id"), Name = d.GetString("name"), Verified = d.GetBool("verified"),
                Identified = d.GetBool("identified"), Blocked = d.GetBool("blocked"), Language = d.GetString("language"),
                PurchaseToken = d.GetString("purchaseToken"),
            };
        }
    }
}
