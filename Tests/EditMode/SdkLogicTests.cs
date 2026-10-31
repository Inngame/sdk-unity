using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace InngameSDK.Tests
{
    public class JsonTests
    {
        // Shape produced by serializeMessage(env, m, "user") in server/src/services/messages.ts
        const string ServerMessage = @"{
          ""id"": ""msg_abc"", ""conversationId"": ""cnv_1"", ""seq"": 7, ""authorType"": ""team"",
          ""body"": ""We are looking into it!"", ""lang"": ""en"",
          ""translations"": { ""fr"": ""On regarde ça !"", ""ja"": ""確認中です！"" },
          ""attachments"": [ { ""type"": ""image/jpeg"", ""size"": 12345, ""width"": 1280, ""height"": 720, ""name"": ""shot.jpg"",
                               ""url"": ""http://localhost:8787/files/org_1/img/f_x.jpg?e=1730000000&s=abc"" } ],
          ""card"": { ""kind"": ""item"", ""label"": ""Your gems"", ""item"": { ""id"": ""gems"", ""name"": ""Gems"", ""quantity"": 500 }, ""appStoreId"": ""123"" },
          ""status"": ""sent"", ""createdAt"": 1761926400123, ""readAt"": null,
          ""author"": { ""name"": ""Alice"", ""avatar"": null }
        }";

        [Test]
        public void ParsesServerMessage()
        {
            var m = InngameMessage.FromJson(InngameJson.ParseObject(ServerMessage));
            Assert.AreEqual("msg_abc", m.Id);
            Assert.AreEqual(7, m.Seq);
            Assert.AreEqual(InngameAuthor.Team, m.Author);
            Assert.AreEqual("On regarde ça !", m.Translations["fr"]);
            Assert.AreEqual("確認中です！", m.Translations["ja"]);
            Assert.AreEqual(1, m.Attachments.Count);
            Assert.IsTrue(m.Attachments[0].IsImage);
            Assert.AreEqual(1280, m.Attachments[0].Width);
            StringAssert.Contains("?e=1730000000&s=abc", m.Attachments[0].Url);
            Assert.AreEqual("item", m.Card.Kind);
            Assert.AreEqual(500, m.Card.Item.Quantity);
            Assert.AreEqual("123", m.Card.Raw.GetString("appStoreId"));
            Assert.AreEqual(1761926400123L, m.CreatedAt);
            Assert.IsNull(m.ReadAt);
            Assert.AreEqual("Alice", m.AuthorName);
        }

        [Test]
        public void ParsesSessionPayload()
        {
            var j = InngameJson.ParseObject(@"{""token"":""dev_x"",""user"":{""id"":""eu_1"",""name"":null,""verified"":false,""language"":""fr"",""blocked"":false,""identified"":false},
              ""conversationId"":null,""unread"":2,""config"":{""appearance"":{""title"":""Help"",""dark"":{""accent"":""#FF0066""}},""photosEnabled"":false,""verifiedOnly"":false,""translation"":true,""faq"":false}}");
            Assert.AreEqual("dev_x", j.GetString("token"));
            Assert.IsNull(j.GetString("conversationId"));
            Assert.AreEqual(2, j.GetLong("unread"));
            var u = InngameUser.FromJson(j.GetDict("user"));
            Assert.AreEqual("fr", u.Language);
            var c = InngameRemoteConfig.FromJson(j.GetDict("config"));
            Assert.IsFalse(c.PhotosEnabled);
            Assert.AreEqual("Help", c.Appearance.GetString("title"));
        }

        [Test]
        public void ParsesRatingCard()
        {
            var pending = InngameCard.FromJson(InngameJson.ParseObject("{\"kind\":\"rating\",\"scale\":5}"));
            Assert.AreEqual("rating", pending.Kind);
            Assert.IsFalse(pending.Rating.HasValue);
            var rated = InngameCard.FromJson(InngameJson.ParseObject("{\"kind\":\"rating\",\"rating\":4,\"lastAction\":\"rated\"}"));
            Assert.AreEqual(4, rated.Rating);
            Assert.AreEqual("rated", rated.LastAction);
            Assert.IsFalse(InngameCard.FromJson(InngameJson.ParseObject("{\"kind\":\"rating\",\"rating\":9}")).Rating.HasValue);
            Assert.AreEqual("Comment s'est passée votre demande ?", UiStrings.Get(UiStrings.K.RateQuestion, "fr"));
        }

        [Test]
        public void ParsesAvailabilityAndFormatsIt()
        {
            var c = InngameRemoteConfig.FromJson(InngameJson.ParseObject("{\"availability\":{\"online\":true,\"replyTime\":\"hours\",\"nextOpenAt\":null}}"));
            Assert.AreEqual("Répond en général en quelques heures", ChatView.AvailabilityText(c.Availability, "fr", DateTime.Now));
            Assert.IsTrue(InngameRemoteConfig.FromJson(InngameJson.ParseObject("{}")).Availability.Online); // old servers: no line
            Assert.IsNull(ChatView.AvailabilityText(new InngameAvailability(), "en", DateTime.Now));
            var now = new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Local);
            long At(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();
            var later = ChatView.AvailabilityText(new InngameAvailability { Online = false, NextOpenAt = At(now.AddHours(1)) }, "en", now);
            StringAssert.StartsWith("Away · back ", later);
            StringAssert.DoesNotContain("Sat", later); // same day: time only
            StringAssert.Contains("Mon", ChatView.AvailabilityText(new InngameAvailability { Online = false, NextOpenAt = At(new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Local)) }, "en", now));
            Assert.IsNull(ChatView.AvailabilityText(new InngameAvailability { Online = false, NextOpenAt = At(now.AddMinutes(-1)) }, "en", now)); // already back
            Assert.AreEqual("Login down", ChatView.Announcement(InngameJson.ParseObject("{\"announcement\":\" Login down \"}")));
            Assert.IsNull(ChatView.Announcement(InngameJson.ParseObject("{\"announcement\":\"  \"}")));
            foreach (var lang in UiStrings.Languages) StringAssert.Contains("{0}", UiStrings.Get(UiStrings.K.AwayBack, lang));
        }

        [Test]
        public void RoundTripsEscapesAndUnicode()
        {
            var src = new Dictionary<string, object>
            {
                ["s"] = "quote \" backslash \\ newline \n tab \t emoji 😀 accents éà",
                ["n"] = 3.5, ["i"] = 42L, ["b"] = true, ["z"] = null,
                ["list"] = new List<object> { 1, "two", new Dictionary<string, object> { ["x"] = -1e-3 } },
            };
            var json = InngameJson.Serialize(src);
            var back = InngameJson.ParseObject(json);
            Assert.AreEqual(src["s"], back["s"]);
            Assert.AreEqual(3.5, back.GetDouble("n"));
            Assert.AreEqual(42L, back["i"]);
            Assert.AreEqual(true, back["b"]);
            Assert.IsNull(back["z"]);
            Assert.AreEqual(-1e-3, ((Dictionary<string, object>)back.GetList("list")[2]).GetDouble("x"), 1e-12);
            Assert.AreEqual("A", InngameJson.ParseObject("{\"u\":\"\\u0041\"}")["u"]);
        }

        [Test]
        public void InvalidJsonReturnsNull()
        {
            Assert.IsNull(InngameJson.ParseObject("{\"a\":"));
            Assert.IsNull(InngameJson.ParseObject("<html>502</html>"));
        }

        [Test]
        public void MessageCacheRoundTrip()
        {
            var m = InngameMessage.FromJson(InngameJson.ParseObject(ServerMessage));
            m.State = InngameMessageState.Sent;
            var back = InngameMessage.FromJson(InngameJson.ParseObject(InngameJson.Serialize(m.ToJson())));
            Assert.AreEqual(m.Id, back.Id);
            Assert.AreEqual(m.Translations["fr"], back.Translations["fr"]);
            Assert.AreEqual(m.Card.Item.Name, back.Card.Item.Name);
            Assert.AreEqual(m.Attachments[0].Url, back.Attachments[0].Url);
        }
    }

    public class TranslationTests
    {
        static InngameMessage Team(string body, string lang, Dictionary<string, string> tr) =>
            new InngameMessage { Author = InngameAuthor.Team, Body = body, Lang = lang, Translations = tr };

        [Test]
        public void BaseLang()
        {
            Assert.AreEqual("pt", InngameTranslation.BaseLang("pt-BR"));
            Assert.AreEqual("zh", InngameTranslation.BaseLang("zh_Hant"));
            Assert.AreEqual("fr", InngameTranslation.BaseLang("FR"));
            Assert.AreEqual("", InngameTranslation.BaseLang(null));
        }

        [Test]
        public void ShowsTranslationForPlayerBaseLanguage()
        {
            var m = Team("We are on it", "en", new Dictionary<string, string> { ["pt"] = "Estamos cuidando disso" });
            Assert.AreEqual("Estamos cuidando disso", InngameTranslation.DisplayBody(m, "pt-BR", out bool tr));
            Assert.IsTrue(tr);
            m.ShowOriginal = true;
            Assert.AreEqual("We are on it", InngameTranslation.DisplayBody(m, "pt-BR", out tr));
            Assert.IsTrue(tr, "toggle stays visible while showing the original");
        }

        [Test]
        public void FallsBackToBody()
        {
            var m = Team("Hello", "en", new Dictionary<string, string> { ["de"] = "Hallo" });
            Assert.AreEqual("Hello", InngameTranslation.DisplayBody(m, "fr", out bool tr));
            Assert.IsFalse(tr);
            Assert.AreEqual("Hello", InngameTranslation.DisplayBody(Team("Hello", "en", new Dictionary<string, string>()), "fr", out tr));
        }

        [Test]
        public void PlayerMessagesShowWhatTheyTyped()
        {
            // The server stores the team-language translation of player messages; the player must never see it.
            var m = new InngameMessage { Author = InngameAuthor.User, Body = "Bonjour", Lang = "fr", Translations = new Dictionary<string, string> { ["en"] = "Hello" } };
            Assert.AreEqual("Bonjour", InngameTranslation.DisplayBody(m, "en", out bool tr));
            Assert.IsFalse(tr);
        }
    }

    public class ContextTests
    {
        [Test]
        public void CapsContextUnder64KbDroppingOldestLogsFirst()
        {
            var logs = new List<object>();
            for (int i = 0; i < 400; i++) logs.Add(new Dictionary<string, object> { ["t"] = i, ["type"] = "error", ["msg"] = "line " + i + new string('x', 300) });
            var d = new Dictionary<string, object> { ["scene"] = "Dungeon3", ["logs"] = logs, ["custom"] = new Dictionary<string, object> { ["level"] = 12 } };
            var capped = ContextCollector.EnforceCap(d, ContextCollector.MaxBytes);
            Assert.LessOrEqual(ContextCollector.SerializedSize(capped), ContextCollector.MaxBytes);
            var kept = capped.GetList("logs");
            Assert.NotNull(kept);
            Assert.Greater(kept.Count, 50);
            Assert.AreEqual(399L, ((Dictionary<string, object>)kept[kept.Count - 1]).GetLong("t"), "newest line kept");
            Assert.AreEqual("Dungeon3", capped.GetString("scene"));
            Assert.AreEqual(12, capped.GetDict("custom")["level"]);
        }

        [Test]
        public void DropsHugeCustomSections()
        {
            var big = new Dictionary<string, object>();
            for (int i = 0; i < 200; i++) big["k" + i] = new List<object> { new string('y', 600) };
            var d = new Dictionary<string, object> { ["scene"] = "Menu", ["game"] = big };
            var capped = ContextCollector.EnforceCap(d, ContextCollector.MaxBytes);
            Assert.LessOrEqual(ContextCollector.SerializedSize(capped), ContextCollector.MaxBytes);
            Assert.AreEqual("Menu", capped.GetString("scene"));
            Assert.IsTrue(capped.GetBool("truncated"));
        }

        [Test]
        public void SmallContextUntouched()
        {
            var d = new Dictionary<string, object> { ["scene"] = "A", ["fps"] = 60 };
            Assert.AreSame(d, ContextCollector.EnforceCap(d, ContextCollector.MaxBytes));
            Assert.IsFalse(d.ContainsKey("truncated"));
        }

        [Test]
        public void LogBufferKeepsLastLinesAndStacksForErrorsOnly()
        {
            var b = new LogBuffer(3);
            b.Add("a", "stackA", LogType.Log);
            b.Add("b", "stackB", LogType.Warning);
            b.Add("c", "stackC", LogType.Error);
            b.Add("d", "stackD", LogType.Exception);
            var s = b.Snapshot();
            Assert.AreEqual(3, s.Count);
            var first = (Dictionary<string, object>)s[0];
            Assert.AreEqual("b", first["msg"]);
            Assert.IsFalse(first.ContainsKey("stack"));
            Assert.AreEqual("stackD", ((Dictionary<string, object>)s[2])["stack"]);
        }

        [Test]
        public void SanitizesUnityTypes()
        {
            var s = ContextCollector.Sanitize(new Dictionary<string, object> { ["pos"] = new Vector3(1, 2, 3), ["mode"] = LogType.Error, ["n"] = 3 });
            Assert.AreEqual(typeof(string), s["pos"].GetType());
            Assert.AreEqual("Error", s["mode"]);
            Assert.AreEqual(3, s["n"]);
            StringAssert.StartsWith("{", InngameJson.Serialize(s));
        }

        [Test]
        public void CollectWithoutRuntimeProducesDeviceInfo()
        {
            var c = ContextCollector.Collect(null);
            Assert.IsTrue(c.ContainsKey("unityVersion"));
            Assert.IsTrue(c.ContainsKey("device"));
            Assert.IsTrue(c.ContainsKey("gpu"));
            Assert.LessOrEqual(ContextCollector.SerializedSize(c), ContextCollector.MaxBytes);
        }
    }

    public class OutgoingQueueTests
    {
        [Test]
        public void SameClientIdAcrossRetries()
        {
            var q = new OutgoingQueue();
            var it = q.Enqueue("hello", null, null, 1000, 0);
            var id = it.ClientId;
            StringAssert.StartsWith("c_", id);
            Assert.LessOrEqual(id.Length, 64, "server: clientId max 64 chars");

            Assert.AreSame(it, q.NextDue(0));
            q.MarkInFlight(id);
            Assert.IsNull(q.NextDue(0), "nothing else while in flight");
            q.MarkRetry(id, 0);
            Assert.IsNull(q.NextDue(1), "backoff");
            var again = q.NextDue(100);
            Assert.AreSame(it, again);
            Assert.AreEqual(id, again.ClientId);
        }

        [Test]
        public void GivesUpAfterMaxAttemptsThenManualRetry()
        {
            var q = new OutgoingQueue();
            var it = q.Enqueue("x", null, null, 0, 0);
            double now = 0;
            for (int i = 0; i < OutgoingQueue.MaxAutoAttempts; i++)
            {
                var due = q.NextDue(now);
                Assert.NotNull(due, "attempt " + i);
                q.MarkInFlight(due.ClientId);
                q.MarkRetry(due.ClientId, now);
                now += 1000;
            }
            Assert.IsTrue(it.Failed);
            Assert.IsNull(q.NextDue(now + 1e6));
            Assert.IsTrue(q.Retry(it.ClientId, now));
            Assert.AreSame(it, q.NextDue(now));
            Assert.AreEqual(0, it.Attempts);
        }

        [Test]
        public void KeepsOrderAndSkipsFailed()
        {
            var q = new OutgoingQueue();
            var a = q.Enqueue("a", null, null, 0, 0);
            var b = q.Enqueue("b", null, null, 0, 0);
            q.MarkInFlight(a.ClientId);
            q.MarkFailed(a.ClientId); // e.g. 400 validation error
            Assert.AreSame(b, q.NextDue(0));
            q.MarkInFlight(b.ClientId);
            q.MarkSent(b.ClientId);
            Assert.AreEqual(1, q.Count);
            Assert.IsNull(q.NextDue(0));
        }

        [Test]
        public void PersistsAndReloads()
        {
            var q = new OutgoingQueue();
            var a = q.Enqueue("persist me", new List<object> { new Dictionary<string, object> { ["key"] = "org/img/f.jpg", ["type"] = "image/jpeg", ["size"] = 10 } },
                new Dictionary<string, object> { ["scene"] = "S" }, 123, 0);
            var json = q.Serialize();
            var q2 = new OutgoingQueue();
            q2.Load(json, 50);
            Assert.AreEqual(1, q2.Count);
            var b = q2.Items[0];
            Assert.AreEqual(a.ClientId, b.ClientId);
            Assert.AreEqual("persist me", b.Body);
            Assert.AreEqual("S", b.Context.GetString("scene"));
            Assert.AreEqual(1, b.Attachments.Count);
            Assert.AreSame(b, q2.NextDue(50));
        }

        [Test]
        public void WakeUpSkipsBackoff()
        {
            var q = new OutgoingQueue();
            var a = q.Enqueue("a", null, null, 0, 0);
            q.MarkInFlight(a.ClientId);
            q.MarkRetry(a.ClientId, 0);
            Assert.IsNull(q.NextDue(0.1));
            q.WakeUp(0.1);
            Assert.AreSame(a, q.NextDue(0.1));
        }
    }

    public class ThemeTests
    {
        [Test]
        public void ParsesHexFormats()
        {
            Assert.IsTrue(InngameTheme.TryHex("#FF0000", out var c)); Assert.AreEqual(Color.red, c);
            Assert.IsTrue(InngameTheme.TryHex("0f0", out c)); Assert.AreEqual(Color.green, c);
            Assert.IsTrue(InngameTheme.TryHex("#0000FF80", out c)); Assert.AreEqual(128, ((Color32)c).a);
            Assert.IsTrue(InngameTheme.TryHex(" #fff8 ", out c)); Assert.AreEqual(0x88, ((Color32)c).a);
            Assert.IsFalse(InngameTheme.TryHex("#12345", out _));
            Assert.IsFalse(InngameTheme.TryHex("purple", out _));
            Assert.IsFalse(InngameTheme.TryHex("", out _));
        }

        [Test]
        public void ServerAppearanceOverridesLocalTheme()
        {
            var dark = InngameTheme.CreateDark();
            var appearance = InngameJson.ParseObject(@"{""title"":""Acme Help"",""welcomeTitle"":"""",""dark"":{""accent"":""#22C55E"",""background"":""#000000""},""light"":{""accent"":""#FF0000""}}");
            var t = dark.WithAppearance(appearance);
            Assert.AreEqual("Acme Help", t.Title);
            Assert.AreEqual(dark.WelcomeTitle, t.WelcomeTitle, "empty server text keeps the local one");
            Assert.AreEqual((Color)new Color32(0x22, 0xC5, 0x5E, 255), t.Accent);
            Assert.AreEqual(t.Accent, t.OutgoingBubble);
            Assert.AreEqual((Color)new Color32(0, 0, 0, 255), t.Background);
            Assert.AreNotEqual(t.Background, t.IncomingBubble, "bubbles derived from the background stay visible");
            Assert.AreNotSame(dark, t, "the original asset is untouched");
            Assert.AreEqual(InngameTheme.Hex("#7C5CFF"), dark.Accent);

            var light = InngameTheme.CreateLight().WithAppearance(appearance);
            Assert.AreEqual(Color.red, light.Accent);
        }

        [Test]
        public void ReadableTextOnAccent()
        {
            Assert.AreEqual(Color.white, InngameTheme.ReadableOn(InngameTheme.Hex("#5B3DF5")));
            Assert.AreNotEqual(Color.white, InngameTheme.ReadableOn(InngameTheme.Hex("#FFE600")));
        }
    }

    public class CardAndStringTests
    {
        static InngameCard Card(string json) => InngameCard.FromJson(InngameJson.ParseObject(json));

        [Test]
        public void ReviewUrlsPerPlatform()
        {
            var c = Card(@"{""kind"":""review"",""url"":""https://example.com/review""}");
            var o = new InngameOptions { AppStoreId = "1234567", SteamAppId = "480" };
            Assert.AreEqual("itms-apps://itunes.apple.com/app/id1234567?action=write-review", CardUrls.ReviewUrl(c, o, RuntimePlatform.IPhonePlayer, "gg.studio.game"));
            Assert.AreEqual("market://details?id=gg.studio.game", CardUrls.ReviewUrl(c, o, RuntimePlatform.Android, "gg.studio.game"));
            Assert.AreEqual("https://store.steampowered.com/recommended/recommendgame/480", CardUrls.ReviewUrl(c, o, RuntimePlatform.WindowsPlayer, "x"));
            Assert.AreEqual("https://example.com/review", CardUrls.ReviewUrl(c, new InngameOptions(), RuntimePlatform.WindowsPlayer, "x"));
            var withId = Card(@"{""kind"":""review"",""appStoreId"":""999""}");
            StringAssert.Contains("id999", CardUrls.ReviewUrl(withId, o, RuntimePlatform.IPhonePlayer, "x"), "card id wins over options");
            Assert.IsNull(CardUrls.ReviewUrl(Card(@"{""kind"":""review""}"), new InngameOptions(), RuntimePlatform.IPhonePlayer, "x"));
        }

        [Test]
        public void EveryLanguageHasEveryString()
        {
            int n = System.Enum.GetValues(typeof(UiStrings.K)).Length;
            foreach (var l in UiStrings.Languages) Assert.AreEqual(n, UiStrings.Count(l), l);
            Assert.AreEqual("Envoyer", UiStrings.Get(UiStrings.K.Send, "fr-CA"));
            Assert.AreEqual("Send", UiStrings.Get(UiStrings.K.Send, "xx"));
        }

        [Test]
        public void LayoutScale()
        {
            Assert.AreEqual(1170f / 400f, ChatView.ScaleFor(new Vector2(1170, 2532), true), 1e-3);
            Assert.AreEqual(1.35f, ChatView.ScaleFor(new Vector2(1920, 1080), false), 1e-3);
        }
    }

    public class CardUrlTests
    {
        [Test]
        public void OnlyWebLinksAndDeepLinksOpen()
        {
            Assert.IsTrue(InngameRuntime.SafeUrl("https://example.com/patch"));
            Assert.IsTrue(InngameRuntime.SafeUrl("mygame://screen/shop"));
            Assert.IsFalse(InngameRuntime.SafeUrl("javascript:alert(1)"));
            Assert.IsFalse(InngameRuntime.SafeUrl("  JavaScript:alert(1)"));
            Assert.IsFalse(InngameRuntime.SafeUrl("file:///etc/passwd"));
            Assert.IsFalse(InngameRuntime.SafeUrl(null));
        }
    }
}
