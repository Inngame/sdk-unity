using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InngameSDK.Tests
{
    /// <summary>
    /// Renders the chat UI into PNGs (phone portrait + desktop, dark + light) with sample messages, without network.
    /// Runs only when INNGAME_SCREENSHOT_DIR is set and a GPU is available (not with -nographics).
    /// </summary>
    public class ScreenshotTests
    {
        string dir;
        GameObject scene;

        [SetUp]
        public void SetUp()
        {
            dir = Environment.GetEnvironmentVariable("INNGAME_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("INNGAME_SCREENSHOT_DIR not set");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("No GPU (-nographics)");
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            scene = TestScene.Build();
        }

        [TearDown]
        public void TearDown()
        {
            Inngame.Shutdown();
            if (scene != null) UnityEngine.Object.Destroy(scene);
        }

        static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "phone-dark", 1170, 2532, true, true, false };
            yield return new object[] { "phone-light", 1170, 2532, true, false, false };
            yield return new object[] { "desktop-dark", 1920, 1080, false, true, false };
            yield return new object[] { "desktop-light", 1920, 1080, false, false, false };
            yield return new object[] { "phone-welcome", 1170, 2532, true, true, true };
            yield return new object[] { "steamdeck-dark", 1280, 800, false, true, false };
        }

        [UnityTest]
        public IEnumerator RenderChat([ValueSource(nameof(Cases))] object[] c)
        {
            string name = (string)c[0];
            int w = (int)c[1], h = (int)c[2];
            bool phone = (bool)c[3], dark = (bool)c[4], welcome = (bool)c[5];

            var theme = dark ? InngameTheme.CreateDark() : InngameTheme.CreateLight();
            theme.Title = "Acme Support";
            Inngame.Configure("app_screenshot", new InngameOptions { Theme = theme, Language = "en", TokenStore = new MemoryTokenStore(), UseWebSocket = false, ScreenshotHotkey = KeyCode.None, LogLevel = InngameLogLevel.None });
            var rt = Inngame.Runtime;
            rt.SuppressNetwork = true;

            var rtex = new RenderTexture(w, h, 24);
            var cam = Camera.main;
            cam.targetTexture = rtex;

            Texture2D shot = null;
            if (!welcome)
            {
                // a "screenshot" for the image bubble: the test scene rendered small
                var small = new RenderTexture(640, 360, 24);
                cam.targetTexture = small;
                cam.Render();
                RenderTexture.active = small;
                shot = new Texture2D(640, 360, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                cam.targetTexture = rtex;
                FillSample(rt, shot);
            }

            Inngame.OpenChat();
            var view = rt.View;
            view.RenderCamera = cam;
            view.ForcedSize = new Vector2(w, h);
            view.ForcedFullScreen = phone;
            view.ForcedSafeArea = phone ? new Rect(0, 102, w, h - 102 - 141) : new Rect(0, 0, w, h);
            view.ApplyTheme(rt.Theme);
            if (!welcome) rt.TeamTypingUntil = Time.realtimeSinceStartup + 1000;
            for (int i = 0; i < 6; i++) yield return null;
            view.FinishFade();
            Canvas.ForceUpdateCanvases();
            yield return null;
            view.FinishFade();

            cam.Render();
            RenderTexture.active = rtex;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[Screenshot] " + path);
            cam.targetTexture = null;
            Assert.IsTrue(File.Exists(path));
        }

        static void FillSample(InngameRuntime rt, Texture2D shot)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long t = now - 3600_000L * 26; // yesterday
            int seq = 0;
            InngameMessage M(InngameAuthor a, string body, long at, string author = null)
            {
                return new InngameMessage { Id = "msg_" + (++seq), Seq = seq, Author = a, Body = body, CreatedAt = at, AuthorName = author, State = InngameMessageState.Sent, Lang = "en" };
            }
            var list = new List<InngameMessage>
            {
                M(InngameAuthor.User, "Hi! I bought 500 gems yesterday but they never showed up in my inventory.", t),
                M(InngameAuthor.Team, "Sorry about that! I can see the purchase on our side, sending them now.", t + 240_000, "Alice"),
            };
            var card = M(InngameAuthor.Team, "Here you go:", t + 260_000, "Alice");
            card.Card = InngameCard.FromJson(InngameJson.ParseObject("{\"kind\":\"item\",\"label\":\"Claim\",\"item\":{\"id\":\"gems\",\"name\":\"Gems\",\"quantity\":500}}"));
            list.Add(card);
            var withShot = M(InngameAuthor.User, "Got them, thanks!! Also the boss in Dungeon 3 freezes the game:", now - 600_000);
            withShot.Attachments.Add(new InngameAttachment { Type = "image/jpeg", Width = shot.width, Height = shot.height });
            withShot.LocalPreviews = new List<Texture2D> { shot };
            list.Add(withShot);
            var tr = M(InngameAuthor.Ai, "Thanks for the screenshot! It's a known bug, fixed in 1.4.2. Restarting the level is a workaround until then.", now - 540_000);
            tr.Lang = "fr";
            tr.Body = "Merci pour la capture ! C'est un bug connu, corrigé en 1.4.2.";
            tr.Translations["en"] = "Thanks for the screenshot! It's a known bug, fixed in 1.4.2. Restarting the level is a workaround until then.";
            list.Add(tr);
            var review = M(InngameAuthor.Team, "If you enjoy the game, a review helps us a lot.", now - 300_000, "Alice");
            review.Card = InngameCard.FromJson(InngameJson.ParseObject("{\"kind\":\"review\",\"url\":\"https://example.com\"}"));
            list.Add(review);
            list.Add(new InngameMessage { ClientId = "c_1", Author = InngameAuthor.User, Body = "Will do!", CreatedAt = now - 5_000, State = InngameMessageState.Sending });
            var rating = M(InngameAuthor.System, "", now - 2_000);
            rating.Card = InngameCard.FromJson(InngameJson.ParseObject("{\"kind\":\"rating\",\"scale\":5}"));
            list.Add(rating);
            rt.Messages.AddRange(list);
            rt.Config.Appearance["announcement"] = "Login is down on Android since 18:00. A fix is on its way, no need to write about it.";
            rt.Config.Availability = new InngameAvailability { Online = true, ReplyTime = "hours" };
        }
    }
}
