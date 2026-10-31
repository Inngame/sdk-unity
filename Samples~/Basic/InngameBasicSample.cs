using System.Collections.Generic;
using InngameSDK;
using UnityEngine;
using UnityEngine.UI;

namespace InngameSamples
{
    /// <summary>
    /// Basic integration: a pause-menu "Help" button with an unread badge, Track() for automations,
    /// a screen that "screen" cards can open, item/offer callbacks and extra context.
    /// Drop it on any GameObject, fill the App Key (or use Tools › Inngame › Settings) and press Play.
    /// </summary>
    public sealed class InngameBasicSample : MonoBehaviour
    {
        [Tooltip("Leave empty to use Tools › Inngame › Settings")]
        public string appKey = "";
        public string playerId = "player-42";
        public bool pauseGameWhileOpen = true;

        Text badge;
        int level = 3;

        void Start()
        {
            var options = new InngameOptions
            {
                PauseGameWhileOpen = pauseGameWhileOpen,
                // Values computed when the player sends a message (shown to your team, not to the player).
                ContextProvider = () => new Dictionary<string, object> { ["level"] = level, ["saveSlot"] = 1, ["coins"] = 1250 },
            };
            if (!string.IsNullOrEmpty(appKey)) Inngame.Configure(appKey, options);
            else if (!Inngame.IsConfigured && !Inngame.ConfigureFromSettings(o => { o.PauseGameWhileOpen = pauseGameWhileOpen; o.ContextProvider = options.ContextProvider; })) return;

            // Your game's account (use IdentifyWithToken with a JWT from your server for verified identities).
            _ = Inngame.Identify(playerId, "Sample Player");
            _ = Inngame.SetMetadata(new Dictionary<string, object> { ["plan"] = "free", ["levelReached"] = level });
            Inngame.SetContext("build", "sample");

            // Cards sent by the team / AI.
            Inngame.RegisterScreen("shop", value => Debug.Log("Open the shop on tab " + value));
            Inngame.RegisterScreen("settings", _ => Debug.Log("Open the settings screen"));
            Inngame.OnItemGranted = item => Debug.Log($"Claimed {item.Quantity} × {item.Name} (grant it server-side, message {item.MessageId})");
            Inngame.OnOffer = offerId => Debug.Log("Show the paywall for offer " + offerId);

            Inngame.UnreadChanged += OnUnread;
            BuildPauseMenuButton();
            OnUnread(Inngame.UnreadCount);
        }

        void OnDestroy() => Inngame.UnreadChanged -= OnUnread;

        void OnUnread(int n)
        {
            if (badge == null) return;
            badge.transform.parent.gameObject.SetActive(n > 0);
            badge.text = n > 9 ? "9+" : n.ToString();
        }

        /// <summary>Example: the player failed a level; an automation may decide to open the chat.</summary>
        public void OnLevelFailed()
        {
            _ = Inngame.Track("level_failed", new Dictionary<string, object> { ["level"] = level, ["attempts"] = 5 });
        }

        void BuildPauseMenuButton()
        {
            // A tiny uGUI menu built in code so the sample needs no prefab. In your game, call Inngame.OpenChat from your own button.
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            var canvasGo = new GameObject("Sample Pause Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Button MakeButton(string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
            {
                var go = new GameObject(label, typeof(Image), typeof(Button));
                go.transform.SetParent(canvasGo.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(200, 48);
                go.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.9f);
                go.GetComponent<Button>().onClick.AddListener(onClick);
                var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
                t.transform.SetParent(go.transform, false);
                t.font = font; t.text = label; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.fontSize = 20;
                var tr = (RectTransform)t.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                return go.GetComponent<Button>();
            }

            var help = MakeButton("Help & Support", new Vector2(24, -24), Inngame.OpenChat);
            MakeButton("Report a bug (F8)", new Vector2(24, -84), () => Inngame.CaptureScreenshot());
            MakeButton("Fail level", new Vector2(24, -144), OnLevelFailed);

            // Unread badge on the Help button
            var b = new GameObject("Badge", typeof(Image));
            b.transform.SetParent(help.transform, false);
            var br = (RectTransform)b.transform;
            br.anchorMin = br.anchorMax = new Vector2(1, 1);
            br.sizeDelta = new Vector2(28, 28);
            b.GetComponent<Image>().color = new Color(1f, 0.25f, 0.3f);
            badge = new GameObject("Count", typeof(Text)).GetComponent<Text>();
            badge.transform.SetParent(b.transform, false);
            badge.font = font; badge.alignment = TextAnchor.MiddleCenter; badge.color = Color.white; badge.fontSize = 16; badge.fontStyle = FontStyle.Bold;
            var cr = (RectTransform)badge.transform; cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
        }
    }
}
