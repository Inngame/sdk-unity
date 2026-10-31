using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace InngameSDK
{
    /// <summary>
    /// Inngame in-game support. Call from the main thread.
    /// <code>
    /// Inngame.Configure("app_…");
    /// Inngame.Identify(player.Id, player.Name);
    /// helpButton.onClick.AddListener(Inngame.OpenChat);
    /// </code>
    /// </summary>
    public static class Inngame
    {
        public const string SdkVersion = "0.1.0";
        public const string DefaultBaseUrl = "https://inngame.meffysto.workers.dev";

        static InngameRuntime rt;
        internal static InngameRuntime Runtime => rt;
        static InngameLogLevel logLevel = InngameLogLevel.Warning;

        // ---------- events ----------

        /// <summary>Unread messages from the team changed (use it for your own badge).</summary>
        public static event Action<int> UnreadChanged;
        public static event Action ChatOpened;
        public static event Action ChatClosed;
        /// <summary>A new message from the team / AI arrived (also while the chat is closed).</summary>
        public static event Action<InngameMessage> MessageReceived;
        /// <summary>An "item" card was claimed. Grant the item <b>server-side</b> (the SDK only reports the claim).</summary>
        public static Action<InngameItem> OnItemGranted;
        /// <summary>An "offer" card was tapped: show your own store / paywall for this offer id.</summary>
        public static Action<string> OnOffer;
        /// <summary>A "restore" card was tapped: restore purchases with your IAP library.</summary>
        public static Action OnRestorePurchases;

        internal static void RaiseUnread(int n) => SafeInvoke(() => UnreadChanged?.Invoke(n));
        internal static void RaiseOpened() => SafeInvoke(() => ChatOpened?.Invoke());
        internal static void RaiseClosed() => SafeInvoke(() => ChatClosed?.Invoke());
        internal static void RaiseMessage(InngameMessage m) => SafeInvoke(() => MessageReceived?.Invoke(m));

        static void SafeInvoke(Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ---------- setup ----------

        public static bool IsConfigured => rt != null;

        /// <summary>Starts the SDK with the public app key (app_…). Safe to call again to change options.</summary>
        public static void Configure(string appKey, InngameOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(appKey)) throw new ArgumentException("Inngame: appKey is required (app_…)", nameof(appKey));
            options = options?.Clone() ?? new InngameOptions();
            logLevel = options.LogLevel;
            if (!appKey.StartsWith("app_", StringComparison.Ordinal)) Log(InngameLogLevel.Warning, "App keys start with \"app_\". Check Tools › Inngame › Settings.");
            if (rt != null)
            {
                if (rt.AppKey == appKey) { rt.UpdateOptions(options); return; }
                Shutdown();
            }
            rt = InngameRuntime.Create(appKey.Trim(), options);
        }

        /// <summary>Configures the SDK from Assets/Resources/InngameSettings (Tools › Inngame › Settings). Returns false if missing.</summary>
        public static bool ConfigureFromSettings(Action<InngameOptions> customize = null)
        {
            var s = InngameSettings.Load();
            if (s == null || string.IsNullOrWhiteSpace(s.AppKey))
            {
                Log(InngameLogLevel.Error, "No app key: open Tools › Inngame › Settings.");
                return false;
            }
            var o = s.ToOptions();
            customize?.Invoke(o);
            Configure(s.AppKey.Trim(), o);
            return true;
        }

        /// <summary>Stops the SDK, closes the chat and the connection (the device token is kept).</summary>
        public static void Shutdown()
        {
            if (rt == null) return;
            var r = rt;
            rt = null;
            r.Dispose();
        }

        static InngameRuntime Rt
        {
            get
            {
                if (rt == null) Log(InngameLogLevel.Error, "Call Inngame.Configure(appKey) first.");
                return rt;
            }
        }

        // ---------- identity ----------

        /// <summary>
        /// Links the device to your game's account (history follows the player across devices).
        /// Unverified: if the account was ever verified (JWT / Steam), the server refuses — use <see cref="IdentifyWithToken"/>.
        /// </summary>
        public static Task<bool> Identify(string userId, string name = null, string email = null)
        {
            var d = new Dictionary<string, object> { ["userId"] = userId };
            if (!string.IsNullOrEmpty(name)) d["name"] = name;
            if (!string.IsNullOrEmpty(email)) d["email"] = email;
            return Rt?.Identify(d) ?? Task.FromResult(false);
        }

        /// <summary>Verified identity: a JWT (HS256) signed by your server with the app's identity secret ({ user_id, name?, email?, exp }).</summary>
        public static Task<bool> IdentifyWithToken(string jwt) =>
            Rt?.Identify(new Dictionary<string, object> { ["token"] = jwt }) ?? Task.FromResult(false);

        /// <summary>
        /// Verified Steam identity. <paramref name="getTicketHex"/> must return a Web API ticket for identity "inngame"
        /// (SteamUser.GetAuthTicketForWebApi("inngame")) as a hex string. See the README for Steamworks.NET / Facepunch snippets.
        /// </summary>
        public static Task<bool> IdentifySteam(Func<Task<string>> getTicketHex) => Rt?.IdentifySteam(getTicketHex) ?? Task.FromResult(false);

        /// <summary>Player attributes shown to the team (max 50 keys; values: string ≤ 500 chars, number, bool, null to delete).</summary>
        public static Task<bool> SetMetadata(IDictionary<string, object> metadata) => Rt?.SetMetadata(metadata) ?? Task.FromResult(false);

        /// <summary>The player logged out of your game: the device continues as a new anonymous player.</summary>
        public static Task<bool> Logout() => Rt?.Logout() ?? Task.FromResult(false);

        /// <summary>Right to erasure: deletes the player, their conversations and photos on the server, then local data.</summary>
        public static Task<bool> DeleteData() => Rt?.DeleteData() ?? Task.FromResult(false);

        public static InngameUser CurrentUser => rt?.User;

        /// <summary>Inngame's purchase token for this player (null before the first session). See <see cref="InngameUser.PurchaseToken"/>.</summary>
        public static string PurchaseToken => rt?.User?.PurchaseToken;

        // ---------- chat ----------

        public static void OpenChat() => Rt?.OpenChat();
        public static void CloseChat() => rt?.CloseChat();
        public static bool IsChatOpen => rt != null && rt.IsOpen;
        public static int UnreadCount => rt?.Unread ?? 0;

        /// <summary>Settings from the inbox (photos, translation, availability…), updated at each session. Null before Configure.</summary>
        public static InngameRemoteConfig Config => rt?.Config;
        public static IReadOnlyList<InngameMessage> Messages => rt?.Messages ?? (IReadOnlyList<InngameMessage>)Array.Empty<InngameMessage>();

        /// <summary>Sends a message from code (with context and any pending screenshot), as if the player typed it.</summary>
        public static void SendMessage(string text) => Rt?.SendText(text);

        // ---------- events / automations ----------

        /// <summary>Game event for automations (e.g. "level_failed"). If an automation answers openChat, the chat opens.</summary>
        public static Task<bool> Track(string name, IDictionary<string, object> props = null, bool opensChat = false) =>
            Rt?.Track(name, props, opensChat) ?? Task.FromResult(false);

        // ---------- context ----------

        /// <summary>Custom value attached to every message (null removes it).</summary>
        public static void SetContext(string key, object value) => Rt?.SetContext(key, value);

        /// <summary>Shortcut for InngameOptions.ContextProvider.</summary>
        public static Func<Dictionary<string, object>> ContextProvider
        {
            get => rt?.Options.ContextProvider;
            set { if (Rt != null) rt.Options.ContextProvider = value; }
        }

        /// <summary>The context that would be attached to a message right now (for debugging).</summary>
        public static Dictionary<string, object> CurrentContext() => ContextCollector.Collect(rt);

        // ---------- screenshots / photos ----------

        /// <summary>Captures the game frame (without the chat), uploads it and attaches it to the next message.</summary>
        public static void CaptureScreenshot(bool openChat = true) => Rt?.CaptureScreenshot(openChat);

        /// <summary>Attaches an image you already have (JPEG/PNG bytes) to the next message.</summary>
        public static Task<bool> AttachImage(byte[] bytes, string mimeType = "image/jpeg") => Rt?.AttachImage(bytes, mimeType, null, null) ?? Task.FromResult(false);

        // ---------- push ----------

        /// <summary>
        /// Forwards the push token obtained by your game (Unity Mobile Notifications: iOSNotificationCenter / Firebase Messaging)
        /// so the team's replies reach the player when the game is closed. Pass null to unregister.
        /// </summary>
        public static Task<bool> SetPushToken(string token, PushKind kind) => Rt?.SetPushToken(token, kind) ?? Task.FromResult(false);

        // ---------- cards ----------

        /// <summary>Registers a game screen that "screen" cards can open (the chat closes first). The argument is the card's value.</summary>
        public static void RegisterScreen(string id, Action<string> open)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (open == null) screens.Remove(id); else screens[id] = open;
        }

        internal static readonly Dictionary<string, Action<string>> screens = new Dictionary<string, Action<string>>();

        // ---------- language ----------

        /// <summary>Language used for the UI and translations ("fr", "pt-BR"…).</summary>
        public static string Language => rt?.PlayerLanguage ?? DetectLanguage();

        internal static string DetectLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified: return "zh";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Swedish: return "sv";
                case SystemLanguage.Norwegian: return "no";
                case SystemLanguage.Danish: return "da";
                case SystemLanguage.Finnish: return "fi";
                case SystemLanguage.Czech: return "cs";
                case SystemLanguage.Greek: return "el";
                case SystemLanguage.Hungarian: return "hu";
                case SystemLanguage.Romanian: return "ro";
                case SystemLanguage.Ukrainian: return "uk";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Vietnamese: return "vi";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Bulgarian: return "bg";
                case SystemLanguage.Catalan: return "ca";
                default: return "en";
            }
        }

        // ---------- logging ----------

        internal static void Log(InngameLogLevel level, string msg)
        {
            if (level == InngameLogLevel.None || level > logLevel) return;
            msg = "[Inngame] " + msg;
            if (level == InngameLogLevel.Error) Debug.LogError(msg);
            else if (level == InngameLogLevel.Warning) Debug.LogWarning(msg);
            else Debug.Log(msg);
        }

        /// <summary>Logging from background threads (Debug.Log is thread-safe).</summary>
        internal static void LogThreaded(InngameLogLevel level, string msg) => Log(level, msg);
    }
}
