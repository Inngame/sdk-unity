using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InngameSDK
{
    /// <summary>The SDK's state machine: session, messages, queue, realtime, polling. One hidden DontDestroyOnLoad object.</summary>
    [AddComponentMenu("")]
    internal sealed class InngameRuntime : MonoBehaviour
    {
        public string AppKey { get; private set; }
        public InngameOptions Options { get; private set; }
        internal ApiClient Api;
        internal ITokenStore Store;
        string keyPrefix;

        // ---- server state ----
        public InngameUser User;
        public string ConversationId;
        public int Unread { get; private set; }
        public InngameRemoteConfig Config = new InngameRemoteConfig();
        public bool Blocked;
        public bool Online = true;
        public bool HasMore;
        /// <summary>The studio's workspace is suspended (403 org_suspended): the chat says support is unavailable.</summary>
        public bool Unavailable => suspension.Unavailable;
        internal readonly Suspension suspension = new Suspension();
        public bool SessionReady => !string.IsNullOrEmpty(Api.DeviceToken) && sessionOk;
        bool sessionOk;

        // ---- local state ----
        public readonly List<InngameMessage> Messages = new List<InngameMessage>();
        internal readonly OutgoingQueue Queue = new OutgoingQueue();
        internal readonly List<PendingAttachment> PendingAttachments = new List<PendingAttachment>();
        internal readonly LogBuffer Logs = new LogBuffer(50);
        internal readonly FpsMeter Fps = new FpsMeter();
        internal readonly List<object> RecentScenes = new List<object>();
        internal readonly Dictionary<string, object> CustomContext = new Dictionary<string, object>();
        internal float? SavedTimeScale;
        internal float TeamTypingUntil;
        internal bool Uploading;

        internal sealed class PendingAttachment
        {
            public Dictionary<string, object> Json;
            public Texture2D Preview;
        }

        // ---- UI ----
        internal ChatView View;
        public bool IsOpen { get; private set; }
        internal InngameTheme BaseTheme;
        internal InngameTheme Theme;

        internal event Action Changed;          // messages / state changed: UI re-renders
        internal event Action StatusChanged;    // online, blocked, typing, uploading

        RealtimeSocket socket;
        Task<bool> sessionTask;
        Dictionary<string, object> pendingIdentity;
        float nextSessionTry;
        int sessionFailures;
        /// <summary>Tests/screenshots: never touch the network.</summary>
        internal bool SuppressNetwork;
        float nextPoll, nextPump, lastTypingSent;
        bool pumping, refreshing;
        CursorLockMode savedLock; bool savedCursorVisible;
        string playerLanguage;
        Vector3 lastAccel; float shakeCooldown;

        public string PlayerLanguage => playerLanguage;
        static double Now => Time.realtimeSinceStartupAsDouble;
        static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ==================== lifecycle ====================

        public static InngameRuntime Create(string appKey, InngameOptions options)
        {
            var go = new GameObject("[Inngame]");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            if (Application.isPlaying) DontDestroyOnLoad(go);
            var rt = go.AddComponent<InngameRuntime>();
            rt.Init(appKey, options);
            return rt;
        }

        void Init(string appKey, InngameOptions options)
        {
            AppKey = appKey;
            Options = options;
            Store = options.TokenStore ?? new PlayerPrefsTokenStore();
            keyPrefix = "inngame." + ShortHash(appKey) + ".";
            playerLanguage = string.IsNullOrEmpty(options.Language) ? Inngame.DetectLanguage() : options.Language;
            Api = new ApiClient(options.BaseUrl, appKey) { DeviceToken = Store.Load(keyPrefix + "token"), Delay = DelayUnscaled };
            Logs.Capacity = options.LogLines;
            BaseTheme = options.Theme != null ? options.Theme : InngameTheme.CreateDark();
            Theme = BaseTheme.WithAppearance(LoadCachedAppearance());
            Application.logMessageReceivedThreaded += OnLog;
            SceneManager.activeSceneChanged += OnSceneChanged;
            OnSceneChanged(default, SceneManager.GetActiveScene());
            LoadCache();
            Queue.Load(Store.Load(keyPrefix + "queue"), Now);
            foreach (var it in Queue.Items) Messages.Add(LocalMessageFor(it));
            socket = new RealtimeSocket(SocketUrl);
            nextSessionTry = 0; // the session starts next frame, so an Identify() made right after Configure() rides along
        }

        public void UpdateOptions(InngameOptions o)
        {
            o.TokenStore ??= Options.TokenStore;
            Options = o;
            Logs.Capacity = o.LogLines;
            if (!string.IsNullOrEmpty(o.Language) && o.Language != playerLanguage) { playerLanguage = o.Language; Changed?.Invoke(); }
            BaseTheme = o.Theme != null ? o.Theme : BaseTheme;
            ApplyTheme();
        }

        public void Dispose()
        {
            if (IsOpen) CloseChat();
            Application.logMessageReceivedThreaded -= OnLog;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            socket?.Stop();
            if (View != null) Destroy(View.gameObject);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            socket?.Stop();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) socket?.Stop();
            else if (SessionReady && !Unavailable) { StartRealtime(); _ = RefreshMessages(); }
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (msg != null && msg.StartsWith("[Inngame]", StringComparison.Ordinal) && type == LogType.Log) return;
            Logs.Add(msg, stack, type);
        }

        void OnSceneChanged(Scene from, Scene to)
        {
            RecentScenes.Add(new Dictionary<string, object> { ["name"] = to.name, ["t"] = NowMs });
            while (RecentScenes.Count > 10) RecentScenes.RemoveAt(0);
        }

        static string ShortHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h.ToString("x8");
            }
        }

        Task DelayUnscaled(float seconds)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (this == null || !isActiveAndEnabled || !Application.isPlaying) return Task.Delay(TimeSpan.FromSeconds(seconds));
            StartCoroutine(DelayRoutine(seconds, tcs));
            return tcs.Task;
        }

        static IEnumerator DelayRoutine(float s, TaskCompletionSource<bool> tcs)
        {
            yield return new WaitForSecondsRealtime(s);
            tcs.TrySetResult(true);
        }

        // ==================== session ====================

        internal Dictionary<string, object> DeviceInfo()
        {
            string platform;
            switch (Application.platform)
            {
                case RuntimePlatform.IPhonePlayer: platform = "ios"; break;
                case RuntimePlatform.Android: platform = "android"; break;
                case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: platform = "windows"; break;
                case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: platform = "macos"; break;
                case RuntimePlatform.LinuxPlayer: case RuntimePlatform.LinuxEditor: platform = "linux"; break;
                case RuntimePlatform.WebGLPlayer: platform = "web"; break;
                default: platform = "other"; break;
            }
            string locale = null;
            try { locale = System.Globalization.CultureInfo.CurrentCulture.Name; } catch (Exception) { }
            var d = new Dictionary<string, object>
            {
                ["platform"] = platform,
                ["model"] = Clip(SystemInfo.deviceModel, 100),
                ["os"] = Clip(SystemInfo.operatingSystem, 100),
                ["appVersion"] = Clip(Application.version, 50),
                ["sdkVersion"] = "unity-" + Inngame.SdkVersion,
                ["language"] = Clip(playerLanguage, 35),
            };
            if (!string.IsNullOrEmpty(locale)) d["locale"] = Clip(locale, 35);
            return d;
        }

        static string Clip(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        public Task<bool> EnsureSession()
        {
            if (SuppressNetwork) return Task.FromResult(false);
            if (Unavailable && (sessionTask == null || sessionTask.IsCompleted)) return Task.FromResult(false); // only Recheck() asks again
            if (sessionTask == null || (sessionTask.IsCompleted && !sessionTask.Result)) sessionTask = StartSession();
            return sessionTask;
        }

        async Task<bool> StartSession()
        {
            var body = new Dictionary<string, object> { ["device"] = DeviceInfo() };
            var identity = pendingIdentity;
            if (identity != null) body["identity"] = identity;
            var r = await Api.Post("/session", body, 2);
            if (this == null) return false;
            if (NoteSuspension(r)) return false;
            if (!r.Ok && r.Status == 401 && identity != null && r.ErrorCode != "invalid_app_key")
            {
                // The identity was refused: keep a working anonymous session and report the error.
                Inngame.Log(InngameLogLevel.Error, "Identity refused: " + r);
                pendingIdentity = null;
                body.Remove("identity");
                r = await Api.Post("/session", body, 2);
            }
            if (!r.Ok)
            {
                sessionFailures++;
                nextSessionTry = Time.realtimeSinceStartup + Mathf.Min(60f, Mathf.Pow(2, sessionFailures));
                SetOnline(!r.Transient);
                Inngame.Log(r.ErrorCode == "invalid_app_key" ? InngameLogLevel.Error : InngameLogLevel.Warning, "Session failed: " + r);
                return false;
            }
            if (ReferenceEquals(pendingIdentity, identity)) pendingIdentity = null;
            SetOnline(true);
            if (suspension.Observe(r, Now)) OnAvailabilityChanged(); // support is back
            var token = r.Json.GetString("token");
            if (!string.IsNullOrEmpty(token) && token != Api.DeviceToken)
            {
                bool hadToken = !string.IsNullOrEmpty(Api.DeviceToken);
                Api.DeviceToken = token;
                Store.Save(keyPrefix + "token", token);
                if (hadToken) ClearServerMessages(); // the old token was invalid: this is a new player
            }
            sessionOk = true;
            sessionFailures = 0;
            ApplyUserPayload(r.Json);
            Config = InngameRemoteConfig.FromJson(r.Json.GetDict("config"));
            Store.Save(keyPrefix + "appearance", InngameJson.Serialize(Config.Appearance));
            ApplyTheme();
            StartRealtime();
            await RefreshMessages();
            PumpQueue();
            if (pendingIdentity != null) { var p = pendingIdentity; pendingIdentity = null; await Identify(p); }
            return true;
        }

        /// <summary>The studio changed a setting (announcement, appearance, office hours): reload it, a few seconds apart for each player.</summary>
        async Task RefreshConfig()
        {
            await DelayUnscaled(UnityEngine.Random.Range(0f, 5f));
            var r = await Call(() => Api.Get("/config?lang=" + Uri.EscapeDataString(playerLanguage ?? ""), 1));
            if (!r.Ok || r.Json == null) return;
            Config = InngameRemoteConfig.FromJson(r.Json);
            Store.Save(keyPrefix + "appearance", InngameJson.Serialize(Config.Appearance));
            ApplyTheme();
            StatusChanged?.Invoke();
            Changed?.Invoke();
        }

        void ApplyUserPayload(Dictionary<string, object> j)
        {
            var oldUser = User?.Id;
            var u = InngameUser.FromJson(j.GetDict("user"));
            if (u != null) { User = u; SetBlocked(u.Blocked); }
            var conv = j.GetString("conversationId");
            if (oldUser != null && u != null && oldUser != u.Id) { ClearServerMessages(); socket?.Restart(); }
            ConversationId = conv ?? ConversationId;
            SetUnread((int)j.GetLong("unread", Unread));
        }

        /// <summary>Calls the API; on an unknown device token, starts a new session once and retries.</summary>
        async Task<ApiResponse> Call(Func<Task<ApiResponse>> f)
        {
            if (Unavailable) return Suspension.Refusal(); // no network until a recheck says support is back
            if (!sessionOk && !await EnsureSession()) return Unavailable ? Suspension.Refusal() : new ApiResponse { Status = 0, NetworkError = "no session" };
            var r = await f();
            if (NoteSuspension(r)) return r;
            if (r.Status == 401 && r.ErrorCode == "invalid_device_token")
            {
                Api.DeviceToken = null;
                Store.Delete(keyPrefix + "token");
                sessionOk = false;
                sessionTask = null;
                if (!await EnsureSession()) return r;
                r = await f();
            }
            if (r.Ok) SetOnline(true);
            else if (r.Status == 0) SetOnline(false);
            return r;
        }

        public async Task<bool> Identify(Dictionary<string, object> identity)
        {
            if (Unavailable)
            {
                // Kept for the session that runs when support is back.
                var keep = pendingIdentity != null ? new Dictionary<string, object>(pendingIdentity) : new Dictionary<string, object>();
                foreach (var kv in identity) keep[kv.Key] = kv.Value;
                pendingIdentity = keep;
                return false;
            }
            if (!sessionOk && (sessionTask == null || !sessionTask.IsCompleted))
            {
                // Not started yet: merge into the session request (a new object, so a request already in flight applies it afterwards).
                var merged = pendingIdentity != null ? new Dictionary<string, object>(pendingIdentity) : new Dictionary<string, object>();
                foreach (var kv in identity) merged[kv.Key] = kv.Value;
                pendingIdentity = merged;
                var ok = await EnsureSession();
                return ok && User != null && (User.Identified || identity.ContainsKey("metadata"));
            }
            var r = await Call(() => Api.Post("/identify", identity, 2));
            if (Suspension.IsSuspended(r)) { pendingIdentity = identity; return false; }
            if (!r.Ok)
            {
                Inngame.Log(InngameLogLevel.Error, "Identify failed: " + r);
                return false;
            }
            var before = ConversationId;
            ApplyUserPayload(r.Json);
            if (before != ConversationId) { socket?.Restart(); await RefreshMessages(); }
            return true;
        }

        public async Task<bool> IdentifySteam(Func<Task<string>> getTicketHex)
        {
            if (getTicketHex == null) return false;
            string ticket;
            try { ticket = await getTicketHex(); }
            catch (Exception e) { Inngame.Log(InngameLogLevel.Error, "Steam ticket: " + e.Message); return false; }
            if (string.IsNullOrEmpty(ticket)) { Inngame.Log(InngameLogLevel.Error, "Steam ticket is empty"); return false; }
            return await Identify(new Dictionary<string, object> { ["steamTicket"] = ticket });
        }

        public Task<bool> SetMetadata(IDictionary<string, object> metadata)
        {
            var m = new Dictionary<string, object>();
            if (metadata != null)
                foreach (var kv in metadata)
                {
                    object v = kv.Value;
                    if (v != null && !(v is string || v is bool || v is int || v is long || v is float || v is double)) v = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                    if (v is string s && s.Length > 500) v = s.Substring(0, 500);
                    m[kv.Key] = v;
                }
            return Identify(new Dictionary<string, object> { ["metadata"] = m });
        }

        public async Task<bool> Logout()
        {
            var r = await Call(() => Api.Post("/logout", new Dictionary<string, object>(), 1));
            if (!r.Ok) return false;
            ClearServerMessages();
            Queue.Clear(); SaveQueue();
            Messages.Clear();
            ConversationId = null;
            User = null;
            SetUnread(0);
            SetBlocked(false);
            sessionTask = null; sessionOk = false;
            await EnsureSession(); // fetch the new anonymous user
            socket?.Restart();
            Changed?.Invoke();
            return true;
        }

        public async Task<bool> DeleteData()
        {
            var r = await Call(() => Api.Post("/delete-data", new Dictionary<string, object>(), 1));
            if (!r.Ok) { Inngame.Log(InngameLogLevel.Error, "DeleteData failed: " + r); return false; }
            socket?.Stop();
            Store.Delete(keyPrefix + "token");
            Store.Delete(keyPrefix + "cache");
            Store.Delete(keyPrefix + "queue");
            Api.DeviceToken = null;
            sessionOk = false; sessionTask = null;
            Queue.Clear();
            Messages.Clear();
            ConversationId = null; User = null;
            SetUnread(0);
            Changed?.Invoke();
            return true;
        }

        // ==================== messages ====================

        public async Task RefreshMessages()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var r = await Call(() => Api.Get("/messages", 1));
                if (!r.Ok || r.Json == null) return;
                ConversationId = r.Json.GetString("conversationId") ?? ConversationId;
                HasMore = r.Json.GetBool("hasMore");
                var list = ParseMessages(r.Json.GetList("messages"));
                RemoveRetracted(r.Json.GetList("retracted"));
                var fresh = MergeServer(list, true);
                SetUnread((int)r.Json.GetLong("unread", Unread));
                if (IsOpen && Unread > 0) _ = MarkRead();
                foreach (var m in fresh) if (!m.IsMine) Inngame.RaiseMessage(m);
                SaveCache();
                Changed?.Invoke();
            }
            finally { refreshing = false; }
        }

        public async Task<bool> LoadOlder()
        {
            if (!HasMore || refreshing) return false;
            long minSeq = long.MaxValue;
            foreach (var m in Messages) if (m.Id != null && m.Seq < minSeq) minSeq = m.Seq;
            if (minSeq == long.MaxValue) return false;
            refreshing = true;
            try
            {
                var r = await Call(() => Api.Get("/messages?before=" + minSeq, 1));
                if (!r.Ok) return false;
                HasMore = r.Json.GetBool("hasMore");
                MergeServer(ParseMessages(r.Json.GetList("messages")), false);
                Changed?.Invoke();
                return true;
            }
            finally { refreshing = false; }
        }

        /// <summary>Removes messages the team unsent (ids from the server). Returns how many were on screen.</summary>
        internal int RemoveRetracted(List<object> ids)
        {
            if (ids == null || ids.Count == 0) return 0;
            var set = new HashSet<string>();
            foreach (var o in ids) if (o is string id) set.Add(id);
            return Messages.RemoveAll(m => m.Id != null && set.Contains(m.Id));
        }

        static List<InngameMessage> ParseMessages(List<object> raw)
        {
            var list = new List<InngameMessage>();
            if (raw == null) return list;
            foreach (var o in raw)
            {
                var m = InngameMessage.FromJson(o as Dictionary<string, object>);
                if (m != null && m.Id != null && m.Status != "draft") list.Add(m);
            }
            return list;
        }

        /// <summary>Merges server messages (dedupe by id; adopt matching pending local messages). Returns the newly added ones.</summary>
        internal List<InngameMessage> MergeServer(List<InngameMessage> incoming, bool markNew)
        {
            var added = new List<InngameMessage>();
            foreach (var m in incoming)
            {
                int idx = Messages.FindIndex(x => x.Id == m.Id);
                if (idx < 0 && m.IsMine)
                {
                    // Echo of a message still in flight from this device: adopt it instead of showing it twice.
                    idx = Messages.FindIndex(x => x.Id == null && x.IsMine && x.ClientId != null && (x.ClientId == m.ClientId || (x.Body == m.Body && x.State == InngameMessageState.Sending)));
                    if (idx >= 0) { m.ClientId = Messages[idx].ClientId; m.LocalPreviews = Messages[idx].LocalPreviews; }
                }
                if (idx >= 0)
                {
                    m.ShowOriginal = Messages[idx].ShowOriginal;
                    m.ClientId ??= Messages[idx].ClientId; // the user view of the server doesn't echo clientId
                    m.LocalPreviews ??= Messages[idx].LocalPreviews;
                    Messages[idx] = m;
                }
                else { Messages.Add(m); if (markNew) added.Add(m); }
            }
            SortMessages();
            return added;
        }

        void SortMessages()
        {
            Messages.Sort((a, b) =>
            {
                bool ap = a.Id == null, bp = b.Id == null;
                if (ap != bp) return ap ? 1 : -1;
                if (!ap) return a.Seq.CompareTo(b.Seq);
                return a.CreatedAt.CompareTo(b.CreatedAt);
            });
        }

        void ClearServerMessages()
        {
            Messages.RemoveAll(m => m.Id != null);
            Store.Delete(keyPrefix + "cache");
            HasMore = false;
        }

        public void SendText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length > 4000) text = text.Substring(0, 4000);
            if (text.Length == 0 && PendingAttachments.Count == 0) return;
            if (Blocked) { Inngame.Log(InngameLogLevel.Warning, "Player is blocked: message not sent"); return; }
            var atts = new List<object>();
            var previews = new List<Texture2D>();
            foreach (var p in PendingAttachments) { atts.Add(p.Json); if (p.Preview != null) previews.Add(p.Preview); }
            PendingAttachments.Clear();
            var ctx = ContextCollector.Collect(this);
            var it = Queue.Enqueue(text, atts, ctx, NowMs, Now);
            var local = LocalMessageFor(it);
            local.LocalPreviews = previews;
            Messages.Add(local);
            SaveQueue();
            Changed?.Invoke();
            PumpQueue();
        }

        InngameMessage LocalMessageFor(OutgoingQueue.Item it)
        {
            var m = new InngameMessage
            {
                ClientId = it.ClientId, Author = InngameAuthor.User, Body = it.Body, CreatedAt = it.CreatedAt,
                State = it.Failed ? InngameMessageState.Failed : InngameMessageState.Sending,
            };
            foreach (var a in it.Attachments)
                if (a is Dictionary<string, object> d)
                    m.Attachments.Add(new InngameAttachment { Type = d.GetString("type"), Size = d.GetLong("size"), Width = (int)d.GetLong("width"), Height = (int)d.GetLong("height"), Url = d.GetString("url") });
            return m;
        }

        public void Retry(InngameMessage m)
        {
            if (m?.ClientId == null || !Queue.Retry(m.ClientId, Now)) return;
            m.State = InngameMessageState.Sending;
            SaveQueue();
            Changed?.Invoke();
            PumpQueue();
        }

        public void Discard(InngameMessage m)
        {
            if (m?.ClientId == null || m.Id != null) return;
            Queue.Remove(m.ClientId);
            Messages.Remove(m);
            SaveQueue();
            Changed?.Invoke();
        }

        async void PumpQueue()
        {
            if (pumping) return;
            pumping = true;
            try
            {
                while (this != null)
                {
                    var it = Queue.NextDue(Now);
                    if (it == null) break;
                    if (!sessionOk || Unavailable) break; // the session loop (Update) retries with backoff and pumps on success
                    Queue.MarkInFlight(it.ClientId);
                    var body = new Dictionary<string, object> { ["body"] = it.Body, ["clientId"] = it.ClientId, ["attachments"] = StripUrls(it.Attachments) };
                    if (it.Context != null) body["context"] = it.Context;
                    var r = await Call(() => Api.Post("/messages", body, 0));
                    var local = Messages.Find(x => x.ClientId == it.ClientId && x.Id == null);
                    if (r.Ok)
                    {
                        Queue.MarkSent(it.ClientId);
                        ConversationId = r.Json.GetString("conversationId") ?? ConversationId;
                        var sent = InngameMessage.FromJson(r.Json.GetDict("message"));
                        if (sent != null)
                        {
                            sent.ClientId = it.ClientId;
                            sent.LocalPreviews = local?.LocalPreviews;
                            if (local != null) Messages.Remove(local);
                            MergeServer(new List<InngameMessage> { sent }, false);
                        }
                        SaveCache();
                    }
                    else if (Suspension.IsSuspended(r))
                    {
                        // Support is unavailable: the message waits, untouched, and goes when support is back.
                        Queue.MarkWaiting(it.ClientId);
                        if (local != null) local.State = InngameMessageState.Sending;
                        SaveQueue();
                        Changed?.Invoke();
                        break;
                    }
                    else if (r.Transient)
                    {
                        Queue.MarkRetry(it.ClientId, Now);
                        if (local != null) local.State = Queue.Find(it.ClientId)?.Failed == true ? InngameMessageState.Failed : InngameMessageState.Sending;
                        SaveQueue();
                        Changed?.Invoke();
                        break;
                    }
                    else
                    {
                        Inngame.Log(InngameLogLevel.Warning, "Message refused: " + r);
                        if (r.ErrorCode == "blocked") SetBlocked(true);
                        Queue.MarkFailed(it.ClientId);
                        if (local != null) local.State = InngameMessageState.Failed;
                    }
                    SaveQueue();
                    Changed?.Invoke();
                }
            }
            finally { pumping = false; }
        }

        static List<object> StripUrls(List<object> atts)
        {
            var l = new List<object>();
            foreach (var a in atts)
                if (a is Dictionary<string, object> d)
                {
                    var c = new Dictionary<string, object>(d);
                    c.Remove("url");
                    l.Add(c);
                }
            return l;
        }

        public async Task MarkRead()
        {
            if (Unread == 0 && ConversationId == null) return;
            SetUnread(0);
            await Call(() => Api.Post("/read", new Dictionary<string, object>(), 1));
        }

        void SetUnread(int n)
        {
            n = Math.Max(0, n);
            if (n == Unread) return;
            Unread = n;
            Inngame.RaiseUnread(n);
            StatusChanged?.Invoke();
        }

        void SetOnline(bool on)
        {
            if (Online == on) return;
            Online = on;
            if (on) { Queue.WakeUp(Now); PumpQueue(); }
            StatusChanged?.Invoke();
        }

        /// <summary>Returns true (and switches to the unavailable state) when the server says the workspace is suspended.</summary>
        bool NoteSuspension(ApiResponse r)
        {
            if (!Suspension.IsSuspended(r)) return false;
            SetOnline(true); // the server answered
            if (suspension.Observe(r, Now)) OnAvailabilityChanged();
            return true;
        }

        void OnAvailabilityChanged()
        {
            if (Unavailable)
            {
                Inngame.Log(InngameLogLevel.Warning, "Support is unavailable (the studio's workspace is suspended); checking again later");
                socket?.Stop();
                foreach (var it in Queue.Items) if (it.InFlight) Queue.MarkWaiting(it.ClientId);
            }
            else
            {
                Inngame.Log(InngameLogLevel.Info, "Support is available again");
                Queue.WakeUp(Now);
            }
            StatusChanged?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>Asks the server again whether support is back (a new session; on success the queue is sent).</summary>
        internal Task<bool> Recheck()
        {
            suspension.MarkChecking(Now);
            if (sessionTask != null && !sessionTask.IsCompleted) return sessionTask;
            sessionOk = false;
            sessionTask = StartSession();
            return sessionTask;
        }

        void SetBlocked(bool b)
        {
            if (Blocked == b) return;
            Blocked = b;
            StatusChanged?.Invoke();
        }

        // ==================== cache ====================

        void SaveCache()
        {
            var list = new List<object>();
            int start = 0, count = 0;
            for (int i = Messages.Count - 1; i >= 0 && count < 50; i--) if (Messages[i].Id != null) { start = i; count++; }
            for (int i = start; i < Messages.Count; i++) if (Messages[i].Id != null) list.Add(Messages[i].ToJson());
            var d = new Dictionary<string, object> { ["user"] = User?.Id, ["conversationId"] = ConversationId, ["messages"] = list };
            Store.Save(keyPrefix + "cache", InngameJson.Serialize(d));
        }

        void LoadCache()
        {
            var d = InngameJson.ParseObject(Store.Load(keyPrefix + "cache"));
            if (d == null) return;
            ConversationId = d.GetString("conversationId");
            Messages.AddRange(ParseMessages(d.GetList("messages")));
            SortMessages();
        }

        /// <summary>Card links: http(s) or an app deep link, never javascript:, data:, file:…</summary>
        internal static bool SafeUrl(string url) =>
            !string.IsNullOrWhiteSpace(url) && !System.Text.RegularExpressions.Regex.IsMatch(url, @"^\s*(javascript|data|file|vbscript|blob|about):", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        void SaveQueue() => Store.Save(keyPrefix + "queue", Queue.Serialize());

        Dictionary<string, object> LoadCachedAppearance() => InngameJson.ParseObject(Store.Load(keyPrefix + "appearance"));

        void ApplyTheme()
        {
            Theme = BaseTheme.WithAppearance(Config.Appearance);
            if (View != null) View.ApplyTheme(Theme);
        }

        // ==================== realtime ====================

        Uri SocketUrl()
        {
            var b = Api.BaseUrl;
            if (b.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) b = "wss://" + b.Substring(8);
            else if (b.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) b = "ws://" + b.Substring(7);
            return new Uri($"{b}/v1/sdk/ws?app_key={Uri.EscapeDataString(AppKey)}&token={Uri.EscapeDataString(Api.DeviceToken ?? "")}");
        }

        void StartRealtime()
        {
            if (Options.UseWebSocket && RealtimeSocket.Supported && SessionReady && !Unavailable && Application.isPlaying) socket.Start();
        }

        public bool SocketConnected => socket != null && socket.Connected;

        void DrainSocket()
        {
            while (socket.Inbox.TryDequeue(out var raw))
            {
                if (raw == RealtimeSocket.OpenEvent) { SetOnline(true); _ = RefreshMessages(); StatusChanged?.Invoke(); continue; }
                if (raw == RealtimeSocket.ClosedEvent) { StatusChanged?.Invoke(); continue; }
                HandleEvent(InngameJson.ParseObject(raw));
            }
        }

        internal void HandleEvent(Dictionary<string, object> e)
        {
            if (e == null) return;
            switch (e.GetString("type"))
            {
                case "message":
                {
                    var m = InngameMessage.FromJson(e.GetDict("message"));
                    if (m == null || m.Status == "draft") return;
                    ConversationId = e.GetString("conversationId") ?? ConversationId;
                    var added = MergeServer(new List<InngameMessage> { m }, true);
                    if (!m.IsMine && added.Count > 0)
                    {
                        TeamTypingUntil = 0;
                        if (IsOpen) _ = MarkRead(); else SetUnread(Unread + 1);
                        Inngame.RaiseMessage(m);
                    }
                    SaveCache();
                    Changed?.Invoke();
                    break;
                }
                case "message.retracted":
                {
                    // The team unsent a message: it leaves the chat (and the cache) at once.
                    var id = e.GetString("messageId");
                    if (id != null && RemoveRetracted(new List<object> { id }) > 0)
                    {
                        if (e.ContainsKey("unread")) SetUnread((int)e.GetLong("unread", Unread));
                        SaveCache();
                        Changed?.Invoke();
                    }
                    break;
                }
                case "typing":
                    if (e.GetString("from") == "team") { TeamTypingUntil = Time.realtimeSinceStartup + 6f; StatusChanged?.Invoke(); }
                    break;
                case "blocked":
                    SetBlocked(e.GetBool("blocked"));
                    break;
                case "config":
                    _ = RefreshConfig();
                    break;
            }
        }

        public void SendTyping()
        {
            if (!SocketConnected || ConversationId == null || Time.realtimeSinceStartup - lastTypingSent < 3f) return;
            lastTypingSent = Time.realtimeSinceStartup;
            socket.Send(InngameJson.Serialize(new Dictionary<string, object> { ["type"] = "typing", ["conversationId"] = ConversationId }));
        }

        // ==================== loop ====================

        void Update()
        {
            Fps.Tick(Time.unscaledDeltaTime);
            if (!sessionOk && !SuppressNetwork && !Unavailable && Time.realtimeSinceStartup >= nextSessionTry && (sessionTask == null || sessionTask.IsCompleted))
            {
                nextSessionTry = Time.realtimeSinceStartup + 1f;
                _ = EnsureSession();
            }
            if (socket != null) DrainSocket();

            float t = Time.realtimeSinceStartup;
            if (!SuppressNetwork && suspension.RecheckDue(Now, IsOpen)) _ = Recheck();
            if (SessionReady && !Unavailable && !SocketConnected && t >= nextPoll)
            {
                nextPoll = t + (IsOpen ? Options.PollIntervalOpen : Options.PollIntervalClosed);
                _ = Poll();
            }
            if (Queue.Count > 0 && !Unavailable && t >= nextPump) { nextPump = t + 1f; PumpQueue(); }
            if (TeamTypingUntil > 0 && t > TeamTypingUntil) { TeamTypingUntil = 0; StatusChanged?.Invoke(); }

            HandleInput();
        }

        async Task Poll()
        {
            if (IsOpen) { await RefreshMessages(); return; }
            var r = await Call(() => Api.Get("/unread", 0));
            if (!r.Ok) return;
            int n = (int)r.Json.GetLong("unread");
            if (n != Unread) { await RefreshMessages(); SetUnread(n); }
        }

        void HandleInput()
        {
            if (IsOpen && View != null)
            {
                if (InputCompat.BackPressed()) View.OnBack();
                return;
            }
            if (Options.ScreenshotHotkey != KeyCode.None && !Application.isMobilePlatform && InputCompat.GetKeyDown(Options.ScreenshotHotkey))
                CaptureScreenshot(true);
            if (Options.ShakeToReport && Application.isMobilePlatform)
            {
                var a = InputCompat.Acceleration();
                shakeCooldown -= Time.unscaledDeltaTime;
                if ((a - lastAccel).sqrMagnitude > 6f && shakeCooldown <= 0) { shakeCooldown = 3f; CaptureScreenshot(true); }
                lastAccel = a;
            }
        }

        // ==================== chat UI ====================

        public void OpenChat()
        {
            if (IsOpen) return;
            if (View == null) View = ChatView.Create(this);
            IsOpen = true;
            if (Options.PauseGameWhileOpen && !SavedTimeScale.HasValue) { SavedTimeScale = Time.timeScale; Time.timeScale = 0f; }
            if (Options.UnlockCursorWhileOpen)
            {
                savedLock = Cursor.lockState; savedCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            View.Show();
            nextPoll = 0;
            if (Unavailable) { if (!SuppressNetwork && suspension.RecheckOnOpen(Now)) _ = Recheck(); }
            else
            {
                _ = RefreshMessages();
                if (Unread > 0) _ = MarkRead();
            }
            Inngame.RaiseOpened();
        }

        public void CloseChat()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (View != null) View.Hide();
            if (SavedTimeScale.HasValue) { Time.timeScale = SavedTimeScale.Value; SavedTimeScale = null; }
            if (Options.UnlockCursorWhileOpen) { Cursor.lockState = savedLock; Cursor.visible = savedCursorVisible; }
            Inngame.RaiseClosed();
        }

        // ==================== screenshots & images ====================

        public void CaptureScreenshot(bool openAfter)
        {
            if (!Config.PhotosEnabled) { Inngame.Log(InngameLogLevel.Warning, "Photos are disabled for this app (Settings › Moderation)."); if (openAfter) OpenChat(); return; }
            StartCoroutine(CaptureRoutine(openAfter));
        }

        IEnumerator CaptureRoutine(bool openAfter)
        {
            bool hidden = View != null && View.CanvasVisible;
            if (hidden) View.CanvasVisible = false; // capture the game, not the chat
            Texture2D tex = null;
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                yield return null;
                tex = RenderCameras();
            }
            else
            {
                yield return new WaitForEndOfFrame();
                try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
                catch (Exception e) { Inngame.Log(InngameLogLevel.Warning, "ScreenCapture failed: " + e.Message); }
                if (tex == null) tex = RenderCameras();
            }
            if (hidden && View != null) View.CanvasVisible = true;
            if (tex == null) { Inngame.Log(InngameLogLevel.Warning, "Screenshot failed"); yield break; }
            tex = ImageUtil.Downscale(tex, Options.ScreenshotMaxSize);
            byte[] jpg = tex.EncodeToJPG(Mathf.Clamp(Options.ScreenshotQuality, 1, 100));
            if (openAfter) OpenChat();
            _ = AttachImage(jpg, "image/jpeg", tex, "screenshot.jpg");
        }

        /// <summary>Fallback capture (batch mode, or if ScreenCapture is unavailable): renders the scene cameras into a texture.</summary>
        static Texture2D RenderCameras()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // No GPU (-nographics): produce a placeholder so the flow can still be exercised.
                var ph = new Texture2D(320, 180, TextureFormat.RGB24, false);
                var px = new Color32[320 * 180];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(40, 44, 52, 255);
                ph.SetPixels32(px); ph.Apply();
                return ph;
            }
            int w = Screen.width > 16 ? Screen.width : 1280, h = Screen.height > 16 ? Screen.height : 720;
            var rtex = RenderTexture.GetTemporary(w, h, 24);
            var cams = new List<Camera>(Camera.allCameras);
            cams.Sort((a, b) => a.depth.CompareTo(b.depth));
            foreach (var c in cams)
            {
                var prev = c.targetTexture;
                c.targetTexture = rtex;
                try { c.Render(); } finally { c.targetTexture = prev; }
            }
            var active = RenderTexture.active;
            RenderTexture.active = rtex;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rtex);
            return tex;
        }

        public async Task<bool> AttachImage(byte[] bytes, string mime, Texture2D preview, string fileName)
        {
            if (bytes == null || bytes.Length == 0) return false;
            if (!Config.PhotosEnabled) { Inngame.Log(InngameLogLevel.Warning, "Photos are disabled for this app."); return false; }
            if (PendingAttachments.Count >= 5) { Inngame.Log(InngameLogLevel.Warning, "Max 5 attachments per message."); return false; }
            if (preview == null)
            {
                preview = new Texture2D(2, 2);
                if (!preview.LoadImage(bytes)) { Destroy(preview); preview = null; }
            }
            Uploading = true; StatusChanged?.Invoke();
            try
            {
                fileName ??= mime == "image/png" ? "image.png" : "image.jpg";
                var r = await Call(() => Api.Upload("/uploads", bytes, fileName, mime, 2));
                if (!r.Ok) { Inngame.Log(InngameLogLevel.Warning, "Upload failed: " + r); return false; }
                var att = r.Json.GetDict("attachment");
                if (att == null) return false;
                if (preview != null) { att["width"] = preview.width; att["height"] = preview.height; }
                att["url"] = r.Json.GetString("url");
                PendingAttachments.Add(new PendingAttachment { Json = att, Preview = preview });
                Changed?.Invoke();
                return true;
            }
            finally { Uploading = false; StatusChanged?.Invoke(); }
        }

        public void RemovePendingAttachment(int index)
        {
            if (index < 0 || index >= PendingAttachments.Count) return;
            PendingAttachments.RemoveAt(index);
            Changed?.Invoke();
        }

        public void PickImage()
        {
            var picker = Options.ImagePicker;
            if (picker == null || !Config.PhotosEnabled) return;
            picker.PickImage((bytes, mime) => { if (bytes != null) _ = AttachImage(bytes, mime ?? "image/jpeg", null, null); });
        }

        // ==================== other API ====================

        public async Task<bool> Track(string name, IDictionary<string, object> props, bool opensChat)
        {
            var body = new Dictionary<string, object> { ["name"] = name, ["opensChat"] = opensChat };
            if (props != null) body["props"] = ContextCollector.Sanitize(props);
            var r = await Call(() => Api.Post("/events", body, 2));
            if (Suspension.IsSuspended(r)) return false; // support unavailable: said once, not at every event
            if (!r.Ok) { Inngame.Log(InngameLogLevel.Warning, $"Track({name}) failed: {r}"); return false; }
            if (r.Json.GetBool("openChat")) OpenChat();
            return true;
        }

        public void SetContext(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (value == null) CustomContext.Remove(key); else CustomContext[key] = value;
        }

        public async Task<bool> SetPushToken(string token, PushKind kind)
        {
            var k = kind == PushKind.Fcm ? "fcm" : kind == PushKind.ApnsSandbox ? "apns_sandbox" : "apns";
            var r = await Call(() => Api.Post("/push", new Dictionary<string, object> { ["token"] = string.IsNullOrEmpty(token) ? null : token, ["kind"] = k }, 2));
            if (!r.Ok) Inngame.Log(InngameLogLevel.Warning, "SetPushToken failed: " + r);
            return r.Ok;
        }

        // ==================== cards ====================

        public void TapCard(InngameMessage m)
        {
            var c = m?.Card;
            if (c == null || m.Id == null) return;
            string action = "tapped";
            switch (c.Kind)
            {
                case "review":
                {
                    var url = CardUrls.ReviewUrl(c, Options, Application.platform, Application.identifier);
                    if (SafeUrl(url)) Application.OpenURL(url); else action = "failed";
                    break;
                }
                case "link":
                case "refund":
                    if (SafeUrl(c.Url)) Application.OpenURL(c.Url); else action = "failed";
                    break;
                case "screen":
                    if (c.Screen != null && Inngame.screens.TryGetValue(c.Screen, out var open))
                    {
                        CloseChat();
                        try { open(c.Value); } catch (Exception e) { Debug.LogException(e); action = "failed"; }
                    }
                    else { Inngame.Log(InngameLogLevel.Warning, $"No screen registered for \"{c.Screen}\" (Inngame.RegisterScreen)."); action = "failed"; }
                    break;
                case "item":
                    if (Inngame.OnItemGranted != null && c.Item != null && c.LastAction != "claimed") { _ = ClaimItem(m); return; }
                    action = c.LastAction == "claimed" ? null : "failed";
                    break;
                case "offer":
                    if (Inngame.OnOffer != null)
                    {
                        CloseChat();
                        try { Inngame.OnOffer(c.OfferId ?? c.Value); } catch (Exception e) { Debug.LogException(e); action = "failed"; }
                    }
                    else action = "failed";
                    break;
                case "restore":
                    if (Inngame.OnRestorePurchases != null) { try { Inngame.OnRestorePurchases(); } catch (Exception e) { Debug.LogException(e); action = "failed"; } }
                    else action = "failed";
                    break;
                default:
                    if (SafeUrl(c.Url)) Application.OpenURL(c.Url); else action = "failed";
                    break;
            }
            if (action == null) return;
            c.LastAction = action;
            if (c.Raw != null) c.Raw["lastAction"] = action;
            Changed?.Invoke();
            _ = Call(() => Api.Post($"/cards/{Uri.EscapeDataString(m.Id)}/action", new Dictionary<string, object> { ["action"] = action }, 2));
        }

        /// <summary>Sends the player's satisfaction score (1 to 5). Shown as rated right away; the server keeps the first score only.</summary>
        public void RateCard(InngameMessage m, int score)
        {
            var c = m?.Card;
            if (c == null || m.Id == null || c.Kind != "rating" || c.Rating.HasValue || score < 1 || score > 5) return;
            c.Rating = score;
            c.LastAction = "rated";
            if (c.Raw != null) { c.Raw["rating"] = (long)score; c.Raw["lastAction"] = "rated"; }
            Changed?.Invoke();
            _ = Call(() => Api.Post($"/cards/{Uri.EscapeDataString(m.Id)}/action", new Dictionary<string, object> { ["action"] = "rated", ["rating"] = score }, 2));
        }

        bool claiming;

        /// <summary>
        /// The item is granted only once the server confirms this device won the claim (the team may have unsent it,
        /// or another device claimed it). The nonce is kept with the card, so a retry after a lost response still grants.
        /// </summary>
        async Task ClaimItem(InngameMessage m)
        {
            var c = m.Card;
            if (claiming) return;
            claiming = true;
            try
            {
                var nonce = c.Raw.GetString("claimNonce");
                if (string.IsNullOrEmpty(nonce)) { nonce = Guid.NewGuid().ToString("N"); c.Raw["claimNonce"] = nonce; SaveCache(); }
                var r = await Call(() => Api.Post($"/cards/{Uri.EscapeDataString(m.Id)}/action", new Dictionary<string, object> { ["action"] = "claimed", ["nonce"] = nonce }, 2));
                if (r.Ok && r.Json != null && r.Json.GetBool("granted"))
                {
                    c.LastAction = "claimed";
                    c.Raw["lastAction"] = "claimed";
                    c.Item.MessageId = m.Id;
                    try { Inngame.OnItemGranted(c.Item); } catch (Exception e) { Debug.LogException(e); }
                }
                else if (r.Ok || r.Status == 404)
                {
                    // Already claimed elsewhere, or unsent by the team: never granted twice.
                    c.LastAction = "claimed";
                    c.Raw["lastAction"] = "claimed";
                    if (r.Status == 404) Messages.Remove(m);
                    Inngame.Log(InngameLogLevel.Warning, "Item not granted: it was already claimed or is no longer available.");
                }
                else Inngame.Log(InngameLogLevel.Warning, "Item claim failed, the player can try again: " + r);
                SaveCache();
                Changed?.Invoke();
            }
            finally { claiming = false; }
        }
    }

    internal static class CardUrls
    {
        public static string ReviewUrl(InngameCard c, InngameOptions o, RuntimePlatform platform, string bundleId)
        {
            string appStoreId = c.Raw.GetString("appStoreId") ?? o?.AppStoreId;
            string playPackage = c.Raw.GetString("playPackage") ?? o?.PlayStorePackage ?? bundleId;
            string steamAppId = c.Raw.GetString("steamAppId") ?? o?.SteamAppId;
            switch (platform)
            {
                case RuntimePlatform.IPhonePlayer:
                    if (!string.IsNullOrEmpty(appStoreId)) return $"itms-apps://itunes.apple.com/app/id{appStoreId}?action=write-review";
                    break;
                case RuntimePlatform.Android:
                    if (!string.IsNullOrEmpty(playPackage)) return $"market://details?id={playPackage}";
                    break;
                case RuntimePlatform.WindowsPlayer: case RuntimePlatform.OSXPlayer: case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.WindowsEditor: case RuntimePlatform.OSXEditor: case RuntimePlatform.LinuxEditor:
                    if (!string.IsNullOrEmpty(steamAppId)) return $"https://store.steampowered.com/recommended/recommendgame/{steamAppId}";
                    if (!string.IsNullOrEmpty(appStoreId) && (platform == RuntimePlatform.OSXPlayer)) return $"macappstore://apps.apple.com/app/id{appStoreId}?action=write-review";
                    break;
            }
            return string.IsNullOrEmpty(c.Url) ? null : c.Url;
        }
    }
}
