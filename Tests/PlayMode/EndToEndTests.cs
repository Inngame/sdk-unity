using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace InngameSDK.Tests
{
    /// <summary>
    /// Runs the real SDK against a real Inngame server. Skipped unless these environment variables are set:
    /// INNGAME_E2E_BASE_URL, INNGAME_E2E_APP_KEY (app_…), INNGAME_E2E_COOKIE (team session cookie), INNGAME_E2E_ORG.
    /// </summary>
    public class EndToEndTests
    {
        string baseUrl, appKey, cookie, org;
        MemoryTokenStore store;
        GameObject scene;

        [SetUp]
        public void SetUp()
        {
            baseUrl = Environment.GetEnvironmentVariable("INNGAME_E2E_BASE_URL");
            appKey = Environment.GetEnvironmentVariable("INNGAME_E2E_APP_KEY");
            cookie = Environment.GetEnvironmentVariable("INNGAME_E2E_COOKIE");
            org = Environment.GetEnvironmentVariable("INNGAME_E2E_ORG");
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(cookie)) Assert.Ignore("E2E env vars not set");
            LogAssert.ignoreFailingMessages = true;
            store = new MemoryTokenStore();
            scene = TestScene.Build();
        }

        [TearDown]
        public void TearDown()
        {
            Inngame.Shutdown();
            Inngame.OnItemGranted = null;
            if (scene != null) UnityEngine.Object.Destroy(scene);
        }

        InngameOptions Options(bool ws) => new InngameOptions
        {
            BaseUrl = baseUrl, TokenStore = store, LogLevel = InngameLogLevel.Info, Language = "fr",
            UseWebSocket = ws, PollIntervalClosed = 2f, PollIntervalOpen = 2f, ScreenshotHotkey = KeyCode.None,
        };

        static IEnumerator Wait(Func<bool> cond, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static IEnumerator Await(System.Threading.Tasks.Task t, float timeout, string what)
        {
            yield return Wait(() => t.IsCompleted, timeout, what);
            if (t.IsFaulted) throw t.Exception;
        }

        // ---- team API (what the inbox sees) ----

        IEnumerator Team(string method, string path, object body, Action<Dictionary<string, object>> done)
        {
            using (var req = new UnityWebRequest(baseUrl + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(InngameJson.Serialize(body)));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.SetRequestHeader("Cookie", cookie);
                req.SetRequestHeader("X-Org", org);
                yield return req.SendWebRequest();
                Assert.IsTrue(req.responseCode >= 200 && req.responseCode < 300, $"{method} {path} → {req.responseCode} {req.downloadHandler.text}");
                done(InngameJson.ParseObject(req.downloadHandler.text));
            }
        }

        [UnityTest]
        public IEnumerator FullConversationAgainstRealServer()
        {
            Inngame.Configure(appKey, Options(true));
            var rt = Inngame.Runtime;
            Assert.NotNull(rt);
            string userId = "unity-e2e-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var identify = Inngame.Identify(userId, "Unity E2E");
            yield return Await(identify, 20, "identify");
            Assert.IsTrue(identify.Result, "identify ok");
            Assert.IsTrue(rt.SessionReady);
            Assert.IsTrue(Inngame.CurrentUser.Identified);

            Inngame.SetContext("level", 12);
            Inngame.ContextProvider = () => new Dictionary<string, object> { ["saveSlot"] = 2, ["hero"] = "Nath" };
            LogAssert.Expect(LogType.Error, "Simulated NullReferenceException in BossAI.Update for the E2E context");
            Debug.LogError("Simulated NullReferenceException in BossAI.Update for the E2E context");

            // Screenshot → upload → pending attachment
            Inngame.CaptureScreenshot(false);
            yield return Wait(() => rt.PendingAttachments.Count == 1, 30, "screenshot uploaded");
            var att = rt.PendingAttachments[0].Json;
            StringAssert.StartsWith("image/jpeg", att.GetString("type"));
            Assert.Greater(att.GetLong("width"), 0);

            Inngame.OpenChat();
            Assert.IsTrue(Inngame.IsChatOpen);
            Inngame.SendMessage("Bonjour, le boss du donjon 3 fige le jeu");
            yield return Wait(() => Inngame.Messages.Any(m => m.IsMine && m.Id != null && m.Body.StartsWith("Bonjour")), 20, "message sent");
            var mine = Inngame.Messages.Last(m => m.IsMine);
            Assert.AreEqual(InngameMessageState.Sent, mine.State);
            Assert.AreEqual(1, mine.Attachments.Count);
            string conv = rt.ConversationId;
            Assert.IsNotNull(conv);

            // What the team sees
            Dictionary<string, object> detail = null;
            yield return Team("GET", "/v1/conversations/" + conv, null, d => detail = d);
            var msgs = detail.GetList("messages");
            var sent = (Dictionary<string, object>)msgs.Last(o => ((Dictionary<string, object>)o).GetString("authorType") == "user");
            Assert.AreEqual("Bonjour, le boss du donjon 3 fige le jeu", sent.GetString("body"));
            Assert.AreEqual(mine.ClientId, sent.GetString("clientId"));
            var ctx = sent.GetDict("context");
            Assert.NotNull(ctx, "context attached");
            Assert.AreEqual(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, ctx.GetString("scene"));
            Assert.AreEqual(12L, ctx.GetDict("custom").GetLong("level"));
            Assert.AreEqual(2L, ctx.GetDict("game").GetLong("saveSlot"));
            Assert.IsTrue(ctx.GetList("logs").Any(l => ((Dictionary<string, object>)l).GetString("msg").Contains("BossAI")), "error log captured");
            Assert.IsNotNull(ctx.GetDict("gpu").GetString("name"));
            Assert.IsNotNull(ctx.GetDict("device").GetString("model"));
            var atts = sent.GetList("attachments");
            Assert.AreEqual(1, atts.Count);
            Assert.AreEqual("image/jpeg", ((Dictionary<string, object>)atts[0]).GetString("type"));
            Debug.Log("[E2E] context size: " + InngameJson.Serialize(ctx).Length + " chars, keys: " + string.Join(",", ctx.Keys));

            // Signed URL of the attachment downloads
            var url = ((Dictionary<string, object>)atts[0]).GetString("url");
            using (var dl = UnityWebRequestTexture.GetTexture(url))
            {
                yield return dl.SendWebRequest();
                // Known server issue (wrangler.jsonc run_worker_first lacks "/files/*": the SPA fallback answers with index.html).
                if (dl.result == UnityWebRequest.Result.Success) Assert.Greater(DownloadHandlerTexture.GetContent(dl).width, 0);
                else Debug.LogWarning("[E2E] signed image URL did not return an image: " + url + " → " + dl.error + " (server routing, see README › Known issues)");
            }

            // Team reply comes back through the WebSocket
            yield return Wait(() => rt.SocketConnected, 15, "websocket connected");
            int before = Inngame.Messages.Count;
            yield return Team("POST", $"/v1/conversations/{conv}/messages", new Dictionary<string, object> { ["body"] = "Reply via WebSocket" }, _ => { });
            float t0 = Time.realtimeSinceStartup;
            yield return Wait(() => Inngame.Messages.Any(m => m.Body == "Reply via WebSocket"), 10, "team reply over websocket");
            Debug.Log($"[E2E] websocket reply latency: {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
            Assert.IsTrue(rt.SocketConnected, "arrived while the socket was connected (polling is off when connected)");
            yield return Wait(() => Inngame.UnreadCount == 0, 5, "chat open → read");

            // Typing indicator team → player (hub relays with endUserId)
            // (Inbox side not simulated here; covered by the server's realtime test.)

            // Item card: claim → team sees lastAction + note
            bool granted = false;
            Inngame.OnItemGranted = item => granted = item.Id == "gems" && item.Quantity == 500;
            yield return Team("POST", $"/v1/conversations/{conv}/messages", new Dictionary<string, object>
            {
                ["body"] = "Sorry! Here are your gems.",
                ["card"] = new Dictionary<string, object> { ["kind"] = "item", ["label"] = "Claim gems", ["item"] = new Dictionary<string, object> { ["id"] = "gems", ["name"] = "Gems", ["quantity"] = 500 } },
            }, _ => { });
            yield return Wait(() => Inngame.Messages.Any(m => m.Card?.Kind == "item"), 10, "card received");
            var cardMsg = Inngame.Messages.Last(m => m.Card?.Kind == "item");
            rt.TapCard(cardMsg);
            Assert.IsTrue(granted, "OnItemGranted called");
            Assert.AreEqual("claimed", cardMsg.Card.LastAction);
            Dictionary<string, object> afterCard = null;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Team("GET", "/v1/conversations/" + conv, null, d => afterCard = d);
            var serverCard = afterCard.GetList("messages").Cast<Dictionary<string, object>>().Last(m => m.GetDict("card") != null).GetDict("card");
            Assert.AreEqual("claimed", serverCard.GetString("lastAction"));
            Assert.IsTrue(afterCard.GetList("messages").Cast<Dictionary<string, object>>().Any(m => m.GetString("authorType") == "note" && m.GetString("body").Contains("claimed")));

            // Track + push
            var track = Inngame.Track("level_failed", new Dictionary<string, object> { ["level"] = 3, ["pos"] = new Vector3(1, 2, 3) });
            yield return Await(track, 10, "track");
            Assert.IsTrue(track.Result);
            var push = Inngame.SetPushToken("fcm-fake-token-0123456789", PushKind.Fcm);
            yield return Await(push, 10, "push");
            Assert.IsTrue(push.Result);

            Inngame.CloseChat();
            Assert.IsFalse(Inngame.IsChatOpen);
        }

        [UnityTest]
        public IEnumerator PollingFallbackDeliversRepliesAndUnread()
        {
            Inngame.Configure(appKey, Options(false));
            var rt = Inngame.Runtime;
            yield return Wait(() => rt.SessionReady, 20, "session");
            Inngame.SendMessage("Polling test");
            yield return Wait(() => Inngame.Messages.Any(m => m.IsMine && m.Id != null), 20, "sent");
            string conv = rt.ConversationId;
            Assert.IsFalse(rt.SocketConnected);

            int unreadEvent = -1;
            Action<int> h = n => unreadEvent = n;
            Inngame.UnreadChanged += h;
            try
            {
                yield return Team("POST", $"/v1/conversations/{conv}/messages", new Dictionary<string, object> { ["body"] = "Reply via polling" }, _ => { });
                yield return Wait(() => unreadEvent >= 1, 15, "UnreadChanged from polling");
                Assert.AreEqual(1, Inngame.UnreadCount);
                Assert.IsTrue(Inngame.Messages.Any(m => m.Body == "Reply via polling"), "messages refreshed with unread");
                Inngame.OpenChat();
                yield return Wait(() => Inngame.UnreadCount == 0, 10, "read on open");
            }
            finally { Inngame.UnreadChanged -= h; }

            // The same device (token in the store) keeps its history after a restart.
            Inngame.Shutdown();
            Inngame.Configure(appKey, Options(false));
            yield return Wait(() => Inngame.Runtime.SessionReady && Inngame.Messages.Any(m => m.Body == "Reply via polling"), 20, "history after restart");

            // Logout → new anonymous player, empty history
            var oldUser = Inngame.CurrentUser.Id;
            var lo = Inngame.Logout();
            yield return Await(lo, 20, "logout");
            Assert.IsTrue(lo.Result);
            Assert.AreNotEqual(oldUser, Inngame.CurrentUser.Id);
            Assert.AreEqual(0, Inngame.Messages.Count);
        }

        [UnityTest]
        public IEnumerator OfflineQueueRetriesWithSameClientId()
        {
            // Start against a dead port: the message waits in the queue (state Sending), then goes out once the server is reachable.
            var o = Options(false);
            o.BaseUrl = "http://127.0.0.1:9";
            Inngame.Configure(appKey, o);
            var rt = Inngame.Runtime;
            Inngame.SendMessage("Queued while offline");
            yield return new WaitForSecondsRealtime(2f);
            var local = Inngame.Messages.Single();
            Assert.IsNull(local.Id);
            Assert.AreEqual(InngameMessageState.Sending, local.State);
            Assert.IsFalse(rt.Online);
            string clientId = local.ClientId;
            Assert.AreEqual(1, rt.Queue.Count);

            // "Network comes back": same store (queue persisted) on the real server.
            Inngame.Shutdown();
            Inngame.Configure(appKey, Options(false));
            rt = Inngame.Runtime;
            yield return Wait(() => Inngame.Messages.Any(m => m.Id != null && m.Body == "Queued while offline"), 25, "queued message delivered");
            Assert.AreEqual(clientId, Inngame.Messages.First(m => m.Body == "Queued while offline").ClientId);
            Assert.AreEqual(0, rt.Queue.Count);
            Assert.IsTrue(rt.Online);
        }
    }

    internal static class TestScene
    {
        /// <summary>A small colorful "game" so screenshots are not empty.</summary>
        public static GameObject Build()
        {
            var root = new GameObject("TestScene");
            var camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(root.transform);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.28f, 0.45f);
            camGo.transform.position = new Vector3(0, 3, -9);
            camGo.transform.LookAt(new Vector3(0, 0.5f, 0));
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(4, 1, 4);
            Tint(ground, new Color(0.25f, 0.55f, 0.3f));
            var colors = new[] { new Color(0.95f, 0.45f, 0.2f), new Color(0.9f, 0.8f, 0.2f), new Color(0.6f, 0.3f, 0.9f), new Color(0.2f, 0.7f, 0.9f) };
            for (int i = 0; i < 7; i++)
            {
                var c = GameObject.CreatePrimitive(i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                c.transform.SetParent(root.transform);
                c.transform.position = new Vector3(-4.5f + i * 1.5f, 0.5f + (i % 3) * 0.4f, (i % 2) * 1.5f);
                c.transform.rotation = Quaternion.Euler(0, i * 25, 0);
                Tint(c, colors[i % colors.Length]);
            }
            return root;
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mat = new Material(r.sharedMaterial) { color = c };
            r.sharedMaterial = mat;
        }
    }
}
