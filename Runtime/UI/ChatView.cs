using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InngameSDK
{
    /// <summary>
    /// The chat window, built entirely from code with uGUI on its own overlay canvas (no prefab, any render pipeline).
    /// Full screen on phones, a centered panel on desktop/console; safe-area aware; gamepad/keyboard navigable.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class ChatView : MonoBehaviour
    {
        InngameRuntime rt;
        InngameTheme theme;
        UiStyle style;
        string lang;

        Canvas canvas;
        CanvasScaler scaler;
        CanvasGroup group;
        RectTransform root, safe, panel, listContent, scrollArea, viewport;
        ScrollRect scroll;
        Image backdrop, fullBg, panelBg;
        UiText typingText, bannerText, subtitle, announcementText;
        GameObject announcement;
        GameObject banner, empty, unavailable, typingRow, attachRow, composerRow, blockedRow, newPill, viewer;
        Button closeBtn, sendBtn, shotBtn, photoBtn;
        LayoutElement inputPillLayout;
        UiInput input;
        RawImage viewerImage;
        AspectRatioFitter viewerFitter;
        Button viewerClose;
        GameObject ownEventSystem;
        readonly List<Selectable> listFocusables = new List<Selectable>();
        Selectable lastListFocus;

        bool dirty, statusDirty, scrollToBottom, wasAtBottom = true;
        int lastMessageCount;
        string lastSignature;
        Vector2 lastSize;
        Rect lastSafe;
        bool lastFull;
        float fadeT;
        bool hiding;

        // ---- test/screenshot hooks ----
        internal Vector2? ForcedSize;
        internal Rect? ForcedSafeArea;
        internal bool? ForcedFullScreen;
        internal Camera RenderCamera;

        public bool Visible => gameObject.activeSelf && !hiding;
        public bool CanvasVisible { get => canvas != null && canvas.enabled; set { if (canvas != null) canvas.enabled = value; } }

        int FontSize => theme.FontSize;

        public static ChatView Create(InngameRuntime rt)
        {
            var go = new GameObject("[Inngame Chat]", typeof(RectTransform));
            DontDestroyOnLoad(go);
            var v = go.AddComponent<ChatView>();
            v.rt = rt;
            v.Build(rt.Theme);
            rt.Changed += v.MarkDirty;
            rt.StatusChanged += v.MarkStatus;
            go.SetActive(false);
            return v;
        }

        void OnDestroy()
        {
            if (rt != null) { rt.Changed -= MarkDirty; rt.StatusChanged -= MarkStatus; }
            if (ownEventSystem != null) Destroy(ownEventSystem);
        }

        void MarkDirty() => dirty = true;
        void MarkStatus() => statusDirty = true;

        public void ApplyTheme(InngameTheme t)
        {
            string draft = input?.Text;
            Build(t);
            if (!string.IsNullOrEmpty(draft)) input.Text = draft;
            if (Visible) { Layout(true); Rebuild(true); Select(DefaultSelection()); }
        }

        // ==================== show / hide ====================

        public void Show()
        {
            gameObject.SetActive(true);
            hiding = false;
            if (EventSystem.current == null) ownEventSystem = InputCompat.EnsureEventSystem();
            fadeT = 0;
            group.alpha = 0;
            Layout(true);
            Rebuild(true);
            scrollToBottom = true;
            Select(DefaultSelection());
        }

        public void Hide()
        {
            if (viewer != null) viewer.SetActive(false);
            if (input != null && input.IsFocused) input.Unfocus();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
            gameObject.SetActive(false);
            if (ownEventSystem != null) { Destroy(ownEventSystem); ownEventSystem = null; }
        }

        Selectable DefaultSelection()
        {
            if (Application.isMobilePlatform) return null; // don't pop the keyboard on phones
            if (NoComposer) return closeBtn;
            return input?.Selectable;
        }

        /// <summary>Blocked player or support unavailable: no composer.</summary>
        bool NoComposer => rt.Blocked || rt.Unavailable;

        void Select(Selectable s)
        {
            if (EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(s != null ? s.gameObject : null);
        }

        /// <summary>Screenshots/tests: skip the fade-in.</summary>
        internal void FinishFade() { fadeT = 1; if (group != null) group.alpha = 1; }

        public void OnBack()
        {
            if (viewer != null && viewer.activeSelf) { viewer.SetActive(false); Select(lastListFocus != null ? lastListFocus : DefaultSelection()); return; }
            if (input != null && input.IsFocused)
            {
                input.Unfocus();
                Select(sendBtn);
                return;
            }
            rt.CloseChat();
        }

        // ==================== build ====================

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            listFocusables.Clear();
        }

        void Build(InngameTheme t)
        {
            theme = t;
            style = UiStyle.From(t);
            lang = rt.PlayerLanguage;
            Clear();
            lastSignature = null;

            root = (RectTransform)transform;
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                scaler = gameObject.AddComponent<CanvasScaler>();
                gameObject.AddComponent<GraphicRaycaster>();
                group = gameObject.AddComponent<CanvasGroup>();
            }
            canvas.renderMode = RenderCamera != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = RenderCamera;
            canvas.planeDistance = 1;
            canvas.sortingOrder = rt.Options.SortingOrder;
            canvas.pixelPerfect = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referencePixelsPerUnit = 100;

            backdrop = Img(root, "Backdrop", null, theme.Backdrop);
            Stretch(backdrop.rectTransform);
            var bdBtn = backdrop.gameObject.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            bdBtn.onClick.AddListener(() => { if (!lastFull) rt.CloseChat(); });

            fullBg = Img(root, "FullBackground", null, theme.Header); // notch / home-indicator strips match the header and composer
            Stretch(fullBg.rectTransform);

            safe = NewRect(root, "SafeArea");
            panel = NewRect(safe, "Panel");
            panelBg = panel.gameObject.AddComponent<Image>();
            Skin(panelBg, theme.PanelSprite, theme.Background, theme.CornerRadius + 4);
            panel.gameObject.AddComponent<RectMask2D>();
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            BuildHeader();
            BuildAnnouncement();
            BuildBanner();
            BuildList();
            BuildTyping();
            BuildAttachments();
            BuildComposer();
            BuildBlocked();
            BuildViewer();
            lastSize = Vector2.zero;
        }

        void BuildHeader()
        {
            var header = NewRect(panel, "Header");
            var bg = header.gameObject.AddComponent<Image>();
            Skin(bg, theme.HeaderSprite, theme.Header, 0);
            int sp = theme.SkinPadding;
            Le(header.gameObject, -1, 64 + sp * 2);
            var h = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(18 + sp, 10 + sp, 10 + sp, 10 + sp);
            h.spacing = 12;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var titleText = Title();
            var avatar = Img(header, "Avatar", UiSprites.Circle, theme.Accent);
            Le(avatar.gameObject, 36, 36);
            var initial = UiText.Create(avatar.transform, "Initial", style, FontSize, theme.AccentText, TextAnchor.MiddleCenter);
            initial.Text = titleText.Length > 0 ? char.ToUpperInvariant(titleText[0]).ToString() : "?";
            initial.Bold = true;
            Stretch(initial.Rect);
            // Le logo du jeu (dans le thème, ou envoyé depuis le dashboard) à la place de l'initiale.
            if (theme.Logo != null) ShowLogo(avatar, initial, theme.Logo);
            else if (!string.IsNullOrEmpty(theme.LogoUrl)) StartCoroutine(LoadLogo(theme.LogoUrl, avatar, initial));

            var col = NewRect(header, "TitleColumn");
            var cv = col.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.childControlWidth = true; cv.childControlHeight = true; cv.childForceExpandHeight = false; cv.spacing = 0;
            cv.childAlignment = TextAnchor.MiddleLeft;
            Le(col.gameObject, -1, -1, 1);
            var title = UiText.Create(col, "Title", style, FontSize + 2, theme.Text, TextAnchor.MiddleLeft);
            title.Text = titleText;
            title.Bold = true;
            title.SingleLine = true;
            subtitle = UiText.Create(col, "Availability", style, FontSize - 4, theme.MutedText, TextAnchor.MiddleLeft);
            subtitle.SingleLine = true;
            subtitle.GameObject.SetActive(false);

            closeBtn = IconButton(header, "Close", "close", Color.clear, theme.MutedText, 44, 24);
            closeBtn.onClick.AddListener(rt.CloseChat);
        }

        /// <summary>« Usually replies in a few hours » or « Away · back Mon 09:00 » (player's local time).</summary>
        internal static string AvailabilityText(InngameAvailability a, string lang, DateTime nowLocal)
        {
            if (a == null) return null;
            if (!a.Online)
            {
                if (a.NextOpenAt <= 0) return null;
                var at = DateTimeOffset.FromUnixTimeMilliseconds(a.NextOpenAt).ToLocalTime().DateTime;
                if (at <= nowLocal) return null; // the team is back; the next session says how fast it replies
                CultureInfo ci;
                try { ci = CultureInfo.GetCultureInfo(lang ?? "en"); } catch { ci = CultureInfo.InvariantCulture; }
                var time = at.ToString("t", ci);
                var when = at.Date == nowLocal.Date ? time
                    : ci.DateTimeFormat.GetAbbreviatedDayName(at.DayOfWeek) + " " + time;
                return string.Format(UiStrings.Get(UiStrings.K.AwayBack, lang), when);
            }
            switch (a.ReplyTime)
            {
                case "minutes": return UiStrings.Get(UiStrings.K.RepliesMinutes, lang);
                case "hours": return UiStrings.Get(UiStrings.K.RepliesHours, lang);
                case "day": return UiStrings.Get(UiStrings.K.RepliesDay, lang);
                default: return null;
            }
        }

        string Title() => !string.IsNullOrWhiteSpace(theme.Title) ? theme.Title : UiStrings.Get(UiStrings.K.Title, lang);

        /// <summary>The team's announcement (outage, maintenance, known bug), translated by the server, above the messages.</summary>
        void BuildAnnouncement()
        {
            var r = NewRect(panel, "Announcement");
            r.gameObject.AddComponent<Image>().color = theme.IncomingBubble;
            Le(r.gameObject, -1, -1, -1, 0); // its text height only: the message list takes the rest
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 14, 0, 0); h.spacing = 11; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var strip = Img(r, "Accent", null, theme.Accent);
            Le(strip.gameObject, 3, -1);
            var col = NewRect(r, "Text");
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(0, 0, 8, 8); v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            Le(col.gameObject, -1, -1, 1);
            announcementText = UiText.Create(col, "Label", style, FontSize - 2, theme.IncomingText, TextAnchor.MiddleLeft);
            announcement = r.gameObject;
            announcement.SetActive(false);
        }

        internal static string Announcement(Dictionary<string, object> appearance)
        {
            var s = appearance?.GetString("announcement");
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        void BuildBanner()
        {
            var b = NewRect(panel, "Banner");
            var img = b.gameObject.AddComponent<Image>();
            img.color = theme.Warning;
            Le(b.gameObject, -1, 34);
            bannerText = UiText.Create(b, "Text", style, FontSize - 3, InngameTheme.ReadableOn(theme.Warning), TextAnchor.MiddleCenter);
            Stretch(bannerText.Rect, 12, 0);
            banner = b.gameObject;
            banner.SetActive(false);
        }

        void BuildList()
        {
            scrollArea = NewRect(panel, "Messages");
            Le(scrollArea.gameObject, -1, 100, -1, 1);
            scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30;
            scroll.inertia = true;
            scroll.decelerationRate = 0.1f;

            viewport = NewRect(scrollArea, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0); // receives drags
            scroll.viewport = viewport;

            listContent = NewRect(viewport, "Content");
            listContent.anchorMin = new Vector2(0, 1); listContent.anchorMax = new Vector2(1, 1); listContent.pivot = new Vector2(0.5f, 1);
            listContent.offsetMin = Vector2.zero; listContent.offsetMax = Vector2.zero;
            var lv = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.padding = new RectOffset(14, 14, 14, 10);
            lv.spacing = 2;
            lv.childControlWidth = true; lv.childControlHeight = true; lv.childForceExpandWidth = true; lv.childForceExpandHeight = false;
            var fit = listContent.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = listContent;
            scroll.onValueChanged.AddListener(OnScroll);

            // Empty / welcome state
            var e = NewRect(scrollArea, "Welcome");
            e.anchorMin = new Vector2(0, 0.5f); e.anchorMax = new Vector2(1, 0.5f); e.pivot = new Vector2(0.5f, 0.5f);
            e.sizeDelta = new Vector2(-48, 0);
            var ev = e.gameObject.AddComponent<VerticalLayoutGroup>();
            ev.childAlignment = TextAnchor.MiddleCenter; ev.spacing = 10;
            ev.childControlWidth = true; ev.childControlHeight = true; ev.childForceExpandHeight = false;
            e.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var badgeRow = NewRect(e, "BadgeRow");
            var brh = badgeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            brh.childAlignment = TextAnchor.MiddleCenter; brh.childControlWidth = false; brh.childControlHeight = false;
            Le(badgeRow.gameObject, -1, 64);
            var badge = Img(badgeRow, "Badge", UiSprites.Circle, theme.Accent);
            badge.rectTransform.sizeDelta = new Vector2(64, 64);
            var bi = Img(badge.transform, "Icon", UiSprites.Icon("chat"), theme.AccentText);
            bi.rectTransform.sizeDelta = new Vector2(36, 36);
            var wt = UiText.Create(e, "Title", style, FontSize + 6, theme.Text, TextAnchor.MiddleCenter);
            wt.Bold = true;
            wt.Text = !string.IsNullOrWhiteSpace(theme.WelcomeTitle) ? theme.WelcomeTitle : UiStrings.Get(UiStrings.K.WelcomeTitle, lang);
            var wm = UiText.Create(e, "Message", style, FontSize, theme.MutedText, TextAnchor.MiddleCenter);
            wm.Text = !string.IsNullOrWhiteSpace(theme.WelcomeMessage) ? theme.WelcomeMessage : UiStrings.Get(UiStrings.K.WelcomeMessage, lang);
            empty = e.gameObject;

            // Support unavailable (workspace suspended): one centered line instead of the conversation.
            var u = NewRect(scrollArea, "Unavailable");
            u.anchorMin = new Vector2(0, 0.5f); u.anchorMax = new Vector2(1, 0.5f); u.pivot = new Vector2(0.5f, 0.5f);
            u.sizeDelta = new Vector2(-48, 0);
            var uv = u.gameObject.AddComponent<VerticalLayoutGroup>();
            uv.childAlignment = TextAnchor.MiddleCenter;
            uv.childControlWidth = true; uv.childControlHeight = true; uv.childForceExpandHeight = false;
            u.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ut = UiText.Create(u, "Message", style, FontSize, theme.MutedText, TextAnchor.MiddleCenter);
            ut.Text = UiStrings.Get(UiStrings.K.Unavailable, lang);
            unavailable = u.gameObject;
            unavailable.SetActive(false);

            // "New messages" pill
            var pill = NewRect(scrollArea, "NewMessages");
            pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0); pill.pivot = new Vector2(0.5f, 0);
            pill.anchoredPosition = new Vector2(0, 12);
            var pimg = pill.gameObject.AddComponent<Image>();
            SetRounded(pimg, 18);
            pimg.color = theme.Accent;
            var ph = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            ph.padding = new RectOffset(14, 16, 8, 8); ph.spacing = 6; ph.childAlignment = TextAnchor.MiddleCenter;
            ph.childControlWidth = true; ph.childControlHeight = true; ph.childForceExpandWidth = false;
            var pfit = pill.gameObject.AddComponent<ContentSizeFitter>();
            pfit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; pfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var chev = Img(pill, "Icon", UiSprites.Icon("chevron"), theme.AccentText);
            Le(chev.gameObject, 16, 16);
            var pt = UiText.Create(pill, "Text", style, FontSize - 3, theme.AccentText, TextAnchor.MiddleLeft);
            pt.Text = UiStrings.Get(UiStrings.K.NewMessages, lang);
            pt.Bold = true;
            var pb = pill.gameObject.AddComponent<Button>();
            pb.targetGraphic = pimg;
            pb.onClick.AddListener(() => { ScrollToBottom(); newPill.SetActive(false); });
            newPill = pill.gameObject;
            newPill.SetActive(false);
        }

        void BuildTyping()
        {
            var r = NewRect(panel, "Typing");
            Le(r.gameObject, -1, 22);
            typingText = UiText.Create(r, "Text", style, FontSize - 4, theme.MutedText, TextAnchor.MiddleLeft);
            Stretch(typingText.Rect, 20, 0);
            typingRow = r.gameObject;
            typingRow.SetActive(false);
        }

        void BuildAttachments()
        {
            var r = NewRect(panel, "Attachments");
            Le(r.gameObject, -1, 84);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 10, 6); h.spacing = 10; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false;
            attachRow = r.gameObject;
            attachRow.SetActive(false);
        }

        void BuildComposer()
        {
            var c = NewRect(panel, "Composer");
            var bg = c.gameObject.AddComponent<Image>();
            Skin(bg, theme.ComposerSprite != null ? theme.ComposerSprite : theme.HeaderSprite, theme.Header, 0);
            int sp = theme.SkinPadding;
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10 + sp, 12 + sp, 10 + sp, 12 + sp); h.spacing = 8; h.childAlignment = TextAnchor.LowerLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            composerRow = c.gameObject;

            shotBtn = IconButton(c, "Screenshot", "camera", Color.clear, theme.MutedText, 44, 26);
            shotBtn.onClick.AddListener(() => rt.CaptureScreenshot(false));
            if (rt.Options.ImagePicker != null)
            {
                photoBtn = IconButton(c, "Photo", "image", Color.clear, theme.MutedText, 44, 26);
                photoBtn.onClick.AddListener(rt.PickImage);
            }

            var pill = NewRect(c, "InputPill");
            var pimg = pill.gameObject.AddComponent<Image>();
            Skin(pimg, theme.InputSprite, theme.InputBackground, 22);
            inputPillLayout = Le(pill.gameObject, -1, 44, 1);
            input = new UiInput(pill, style, FontSize, theme.Text, theme.MutedText,
                !string.IsNullOrWhiteSpace(theme.Placeholder) ? theme.Placeholder : UiStrings.Get(UiStrings.K.Placeholder, lang));
            var ir = (RectTransform)input.GameObject.transform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(16, 10); ir.offsetMax = new Vector2(-14, -10);
            input.Submitted += _ => Send(true);
            input.Changed += OnInputChanged;
            AddFocusFrame(input.Selectable, pill, 22);
            var focus = input.GameObject.GetComponent<UiFocus>();
            focus.Selected += OnInputSelected;

            sendBtn = IconButton(c, "Send", "send", theme.Accent, theme.AccentText, 44, 22, true);
            if (theme.ButtonSprite != null) Skin(sendBtn.GetComponent<Image>(), theme.ButtonSprite, theme.Accent, 22);
            sendBtn.onClick.AddListener(() => Send(false));
        }

        void BuildBlocked()
        {
            var r = NewRect(panel, "Blocked");
            var bg = r.gameObject.AddComponent<Image>();
            Skin(bg, theme.ComposerSprite != null ? theme.ComposerSprite : theme.HeaderSprite, theme.Header, 0);
            Le(r.gameObject, -1, 64);
            var t = UiText.Create(r, "Text", style, FontSize - 2, theme.MutedText, TextAnchor.MiddleCenter);
            t.Text = UiStrings.Get(UiStrings.K.Blocked, lang);
            Stretch(t.Rect, 16, 0);
            blockedRow = r.gameObject;
            blockedRow.SetActive(false);
        }

        void BuildViewer()
        {
            var v = NewRect(root, "ImageViewer");
            Stretch(v);
            var bg = v.gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.94f);
            var b = v.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => OnBack());
            var holder = NewRect(v, "Holder");
            Stretch(holder, 24, 72);
            var img = NewRect(holder, "Image");
            img.anchorMin = img.anchorMax = new Vector2(0.5f, 0.5f);
            viewerImage = img.gameObject.AddComponent<RawImage>();
            viewerImage.raycastTarget = false;
            viewerFitter = img.gameObject.AddComponent<AspectRatioFitter>();
            viewerFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            viewerClose = IconButton(v, "Close", "close", new Color(1, 1, 1, 0.12f), Color.white, 48, 24, true);
            var cr = (RectTransform)viewerClose.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(1, 1);
            cr.anchoredPosition = new Vector2(-20, -20);
            cr.sizeDelta = new Vector2(48, 48);
            viewerClose.onClick.AddListener(() => OnBack());
            viewer = v.gameObject;
            viewer.SetActive(false);
        }

        void OpenViewer(Texture tex)
        {
            if (tex == null) return;
            lastListFocus = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null ? EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>() : null;
            viewerImage.texture = tex;
            viewerFitter.aspectRatio = tex.height > 0 ? (float)tex.width / tex.height : 1;
            viewer.SetActive(true);
            viewer.transform.SetAsLastSibling();
            Select(viewerClose);
        }

        // ==================== layout ====================

        Vector2 ScreenSize => ForcedSize ?? new Vector2(Screen.width, Screen.height);
        Rect SafeArea => ForcedSafeArea ?? Screen.safeArea;

        bool IsFullScreen(Vector2 size)
        {
            if (ForcedFullScreen.HasValue) return ForcedFullScreen.Value;
            if (rt.Options.FullScreen.HasValue) return rt.Options.FullScreen.Value;
            if (size.y > size.x * 1.15f) return true; // portrait
            if (Application.isMobilePlatform)
            {
                float dpi = Screen.dpi > 0 ? Screen.dpi : 160;
                return Mathf.Min(size.x, size.y) / dpi < 3.6f; // phones in landscape; tablets get the panel
            }
            return false;
        }

        internal static float ScaleFor(Vector2 size, bool full)
        {
            if (full) return Mathf.Max(0.5f, Mathf.Min(size.x, size.y) / 400f);
            return Mathf.Clamp(size.y / 800f, 0.6f, 4f);
        }

        void Layout(bool force)
        {
            var size = ScreenSize;
            var sa = SafeArea;
            bool full = IsFullScreen(size);
            if (!force && size == lastSize && sa == lastSafe && full == lastFull) return;
            lastSize = size; lastSafe = sa; lastFull = full;
            float k = ScaleFor(size, full);
            scaler.scaleFactor = k;

            backdrop.enabled = !full;
            fullBg.gameObject.SetActive(full);
            if (theme.PanelSprite == null) SetRounded(panelBg, full ? 0 : theme.CornerRadius + 4);
            if (size.x > 0 && size.y > 0)
            {
                safe.anchorMin = new Vector2(sa.xMin / size.x, sa.yMin / size.y);
                safe.anchorMax = new Vector2(sa.xMax / size.x, sa.yMax / size.y);
            }
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            Vector2 units = size / k;
            if (full)
            {
                Stretch(panel);
            }
            else
            {
                float w = Mathf.Min(720, units.x - 48), h = Mathf.Min(units.y - 64, 880);
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot = new Vector2(0.5f, 0.5f);
                panel.sizeDelta = new Vector2(w, h);
                panel.anchoredPosition = Vector2.zero;
            }
            dirty = true;
        }

        float ListWidth => Mathf.Max(200, (lastFull ? lastSize.x / scaler.scaleFactor : panel.rect.width) - 28);

        // ==================== update ====================

        void Update()
        {
            if (rt == null) return;
            Layout(false);
            if (fadeT < 1) { fadeT = Mathf.Min(1, fadeT + Time.unscaledDeltaTime / 0.15f); group.alpha = fadeT; }

            if (InputCompat.GamepadActive() || InputCompat.GetKeyDown(KeyCode.Tab) || InputCompat.GetKeyDown(KeyCode.DownArrow) || InputCompat.GetKeyDown(KeyCode.UpArrow)) UiFocus.NavigationMode = true;
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (Input.GetMouseButtonDown(0) || Input.touchCount > 0) UiFocus.NavigationMode = false; } catch (InvalidOperationException) { }
#endif
            if (InputCompat.FocusComposerPressed() && input != null && !NoComposer) Select(input.Selectable);

            if (dirty) { dirty = false; Rebuild(false); }
            if (statusDirty) { statusDirty = false; RefreshStatus(); }
            if (rt.TeamTypingUntil > 0)
            {
                typingRow.SetActive(true);
                int dots = 1 + (int)(Time.unscaledTime * 2.5f) % 3;
                typingText.Text = UiStrings.Get(UiStrings.K.Typing, lang).TrimEnd('…', '.') + new string('.', dots);
            }
            else if (typingRow.activeSelf) typingRow.SetActive(false);

            if (scrollToBottom)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0;
                scrollToBottom = false;
            }
            KeepSelectionVisible();
        }

        void OnScroll(Vector2 pos)
        {
            wasAtBottom = scroll.verticalNormalizedPosition <= 0.02f || listContent.rect.height <= viewport.rect.height;
            if (wasAtBottom && newPill.activeSelf) newPill.SetActive(false);
            if (pos.y >= 0.995f && listContent.rect.height > viewport.rect.height && rt.HasMore) LoadOlder();
        }

        async void LoadOlder()
        {
            float fromBottom = listContent.rect.height - (listContent.anchoredPosition.y);
            if (!await rt.LoadOlder()) return;
            Rebuild(true);
            Canvas.ForceUpdateCanvases();
            listContent.anchoredPosition = new Vector2(0, listContent.rect.height - fromBottom);
        }

        void ScrollToBottom() => scrollToBottom = true;

        void KeepSelectionVisible()
        {
            var es = EventSystem.current;
            if (es == null || !UiFocus.NavigationMode) return;
            var sel = es.currentSelectedGameObject;
            if (sel == null || !sel.transform.IsChildOf(listContent)) return;
            var r = (RectTransform)sel.transform;
            var corners = new Vector3[4];
            r.GetWorldCorners(corners);
            var vpCorners = new Vector3[4];
            viewport.GetWorldCorners(vpCorners);
            float k = root.lossyScale.y > 0 ? root.lossyScale.y : 1;
            if (corners[1].y > vpCorners[1].y) listContent.anchoredPosition -= new Vector2(0, (corners[1].y - vpCorners[1].y) / k + 12);
            else if (corners[0].y < vpCorners[0].y) listContent.anchoredPosition += new Vector2(0, (vpCorners[0].y - corners[0].y) / k + 12);
        }

        void OnInputChanged(string v)
        {
            rt.SendTyping();
            UpdateComposerHeight();
            sendBtn.interactable = !NoComposer && (v.Trim().Length > 0 || rt.PendingAttachments.Count > 0);
        }

        void UpdateComposerHeight()
        {
            float h = Mathf.Clamp(input.TextPreferredHeight + 20, 44, 44 + FontSize * 4f);
            if (Mathf.Abs(inputPillLayout.preferredHeight - h) > 0.5f) inputPillLayout.preferredHeight = h;
        }

        void OnInputSelected()
        {
            var hook = rt.Options.OnScreenKeyboard;
            if (hook == null || !UiFocus.NavigationMode) return;
            input.Unfocus();
            hook(input.Text, text =>
            {
                if (input == null) return;
                input.Text = text ?? "";
                Select(sendBtn);
            });
        }

        void Send(bool fromEnter)
        {
            if (NoComposer) return;
            var text = input.Text;
            if (string.IsNullOrWhiteSpace(text) && rt.PendingAttachments.Count == 0) return;
            rt.SendText(text);
            input.Text = "";
            UpdateComposerHeight();
            scrollToBottom = true;
            if (!Application.isMobilePlatform) input.Focus();
        }

        // ==================== status ====================

        void RefreshStatus()
        {
            if (banner == null) return;
            var note = Announcement(rt.Config.Appearance);
            announcement.SetActive(note != null);
            if (note != null) announcementText.Text = note;
            var avail = AvailabilityText(rt.Config.Availability, lang, DateTime.Now);
            subtitle.GameObject.SetActive(!string.IsNullOrEmpty(avail));
            if (!string.IsNullOrEmpty(avail)) subtitle.Text = avail;
            bool offline = !rt.Online && !rt.Unavailable;
            banner.SetActive(offline || (rt.Uploading && !rt.Unavailable));
            bannerText.Text = offline ? UiStrings.Get(UiStrings.K.Offline, lang) : UiStrings.Get(UiStrings.K.Uploading, lang);
            banner.GetComponent<Image>().color = offline ? theme.Warning : theme.Header;
            bannerText.Color = offline ? InngameTheme.ReadableOn(theme.Warning) : theme.MutedText;
            composerRow.SetActive(!NoComposer);
            blockedRow.SetActive(rt.Blocked && !rt.Unavailable);
            unavailable.SetActive(rt.Unavailable);
            viewport.gameObject.SetActive(!rt.Unavailable);
            empty.SetActive(!rt.Unavailable && rt.Messages.Count == 0);
            typingRow.SetActive(typingRow.activeSelf && !rt.Unavailable);
            if (rt.Unavailable) { newPill.SetActive(false); attachRow.SetActive(false); }
            bool photos = rt.Config.PhotosEnabled;
            shotBtn.gameObject.SetActive(photos);
            if (photoBtn != null) photoBtn.gameObject.SetActive(photos);
            sendBtn.interactable = !NoComposer && (input.Text.Trim().Length > 0 || rt.PendingAttachments.Count > 0);
            BuildNavigation();
        }

        // ==================== messages ====================

        string Signature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in rt.Messages) sb.Append(m.Id ?? m.ClientId).Append(m.State).Append(m.ShowOriginal).Append(m.Card?.LastAction).Append('|');
            sb.Append(rt.PendingAttachments.Count).Append(rt.Blocked).Append(rt.Unavailable).Append(lastSize);
            return sb.ToString();
        }

        void Rebuild(bool force)
        {
            RefreshStatus();
            var sig = Signature();
            if (!force && sig == lastSignature) return;
            lastSignature = sig;
            bool atBottom = wasAtBottom;
            int prevCount = lastMessageCount;

            for (int i = listContent.childCount - 1; i >= 0; i--) Destroy(listContent.GetChild(i).gameObject);
            listContent.DetachChildren();
            listFocusables.Clear();

            var msgs = rt.Messages;
            empty.SetActive(msgs.Count == 0 && !rt.Unavailable);
            InngameMessage prev = null;
            float maxW = ListWidth * 0.8f;
            foreach (var m in msgs)
            {
                if (prev == null || prev.CreatedAtLocal.Date != m.CreatedAtLocal.Date) DaySeparator(m.CreatedAtLocal);
                bool grouped = prev != null && prev.Author == m.Author && prev.AuthorName == m.AuthorName
                               && Math.Abs(m.CreatedAt - prev.CreatedAt) < 3 * 60 * 1000 && prev.CreatedAtLocal.Date == m.CreatedAtLocal.Date;
                MessageRow(m, grouped, maxW);
                prev = m;
            }
            RebuildAttachments();
            BuildNavigation();

            lastMessageCount = msgs.Count;
            bool newIncoming = msgs.Count > prevCount && msgs.Count > 0 && !msgs[msgs.Count - 1].IsMine;
            bool newMine = msgs.Count > prevCount && msgs.Count > 0 && msgs[msgs.Count - 1].IsMine;
            if (force || atBottom || newMine) scrollToBottom = true;
            else if (newIncoming && !rt.Unavailable) newPill.SetActive(true);
        }

        void DaySeparator(DateTime day)
        {
            var r = NewRect(listContent, "Day");
            Le(r.gameObject, -1, 34);
            var t = UiText.Create(r, "Text", style, FontSize - 5, theme.MutedText, TextAnchor.MiddleCenter);
            Stretch(t.Rect);
            t.Bold = true;
            var today = DateTime.Now.Date;
            t.Text = day.Date == today ? UiStrings.Get(UiStrings.K.Today, lang)
                : day.Date == today.AddDays(-1) ? UiStrings.Get(UiStrings.K.Yesterday, lang)
                : day.ToString(day.Year == today.Year ? "d MMM" : "d MMM yyyy", Culture());
        }

        CultureInfo culture;
        CultureInfo Culture()
        {
            if (culture != null) return culture;
            try { culture = new CultureInfo(lang); } catch (Exception) { culture = CultureInfo.InvariantCulture; }
            return culture;
        }

        string TimeLabel(DateTime t)
        {
            try { return t.ToString("t", Culture()); } catch (Exception) { return t.ToString("HH:mm", CultureInfo.InvariantCulture); }
        }

        void MessageRow(InngameMessage m, bool grouped, float maxW)
        {
            if (m.Author == InngameAuthor.System && m.Card == null && m.Attachments.Count == 0)
            {
                var sr = NewRect(listContent, "System");
                var st = UiText.Create(sr, "Text", style, FontSize - 4, theme.MutedText, TextAnchor.MiddleCenter);
                st.Text = InngameTranslation.DisplayBody(m, lang, out _);
                var sh = sr.gameObject.AddComponent<HorizontalLayoutGroup>();
                sh.padding = new RectOffset(24, 24, 6, 6); sh.childControlWidth = true; sh.childControlHeight = true;
                return;
            }

            bool mine = m.IsMine;
            var row = NewRect(listContent, mine ? "Out" : "In");
            var rh = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rh.childAlignment = mine ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            rh.childControlWidth = true; rh.childControlHeight = true; rh.childForceExpandWidth = false; rh.childForceExpandHeight = false;
            rh.padding = new RectOffset(0, 0, grouped ? 1 : 8, 1);

            var bubble = NewRect(row, "Bubble");
            var bimg = bubble.gameObject.AddComponent<Image>();
            Color bubbleColor = mine ? theme.OutgoingBubble : theme.IncomingBubble;
            Color textColor = mine ? theme.OutgoingText : theme.IncomingText;
            var bubbleSprite = mine ? theme.OutgoingBubbleSprite : theme.IncomingBubbleSprite;
            Skin(bimg, bubbleSprite, bubbleColor, theme.CornerRadius);
            if (mine && m.State == InngameMessageState.Failed) bimg.color = Color.Lerp(bimg.color, theme.Danger, 0.55f);
            // Bulle en image du jeu : le texte reste à l'intérieur de ses bords (ornements, pointes).
            int px = 14, py = 9;
            if (bubbleSprite != null)
            {
                float k = 0.75f / Mathf.Max(0.01f, theme.SpriteScale);
                px = Mathf.Max(px, Mathf.RoundToInt(Mathf.Max(bubbleSprite.border.x, bubbleSprite.border.z) * k)) + theme.SkinPadding;
                py = Mathf.Max(py, Mathf.RoundToInt(Mathf.Max(bubbleSprite.border.y, bubbleSprite.border.w) * k)) + theme.SkinPadding;
            }
            var bv = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.padding = new RectOffset(px, px, py, py - 1); bv.spacing = 5;
            bv.childControlWidth = true; bv.childControlHeight = true; bv.childForceExpandWidth = false; bv.childForceExpandHeight = false;
            bv.childAlignment = TextAnchor.UpperLeft;
            bubble.gameObject.AddComponent<ClampedWidth>().MaxWidth = maxW;

            if (!mine && !grouped)
            {
                var a = UiText.Create(bubble, "Author", style, FontSize - 4, AuthorColor(), TextAnchor.UpperLeft);
                a.Bold = true;
                a.Text = !string.IsNullOrEmpty(m.AuthorName) ? m.AuthorName
                    : m.Author == InngameAuthor.Ai ? UiStrings.Get(UiStrings.K.Ai, lang) : Title();
            }

            var body = InngameTranslation.DisplayBody(m, lang, out bool translated);
            if (!string.IsNullOrEmpty(body))
            {
                var bt = UiText.Create(bubble, "Body", style, FontSize, textColor, TextAnchor.UpperLeft);
                bt.Text = body;
            }

            int imgIndex = 0;
            foreach (var att in m.Attachments)
            {
                if (!att.IsImage) continue;
                Texture2D local = m.LocalPreviews != null && imgIndex < m.LocalPreviews.Count ? m.LocalPreviews[imgIndex] : null;
                ImageTile(bubble, att, local, maxW - 28);
                imgIndex++;
            }
            if (m.Attachments.Count == 0 && m.LocalPreviews != null)
                foreach (var p in m.LocalPreviews) ImageTile(bubble, new InngameAttachment { Type = "image/jpeg", Width = p.width, Height = p.height }, p, maxW - 28);

            if (m.Card != null && m.Card.Kind == "rating") RatingCard(bubble, m);
            else if (m.Card != null) CardButton(bubble, m, mine);

            // footer: time · state · translation toggle
            var footer = NewRect(bubble, "Footer");
            Le(footer.gameObject, -1, -1, 1); // spans the bubble so the time sits on the right edge of outgoing bubbles
            var fh = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            fh.spacing = 6; fh.childAlignment = mine ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            fh.childControlWidth = true; fh.childControlHeight = true; fh.childForceExpandWidth = false;
            Color meta = mine ? new Color(textColor.r, textColor.g, textColor.b, 0.72f) : theme.MutedText;
            if (translated)
            {
                var tb = TextButton(footer, (UiStrings.Get(UiStrings.K.Translated, lang) + " · " + UiStrings.Get(m.ShowOriginal ? UiStrings.K.ShowTranslation : UiStrings.K.ShowOriginal, lang)), FontSize - 5, theme.Accent);
                tb.onClick.AddListener(() => { m.ShowOriginal = !m.ShowOriginal; dirty = true; });
                listFocusables.Add(tb);
            }
            string stateText = TimeLabel(m.CreatedAtLocal);
            if (mine)
            {
                if (m.State == InngameMessageState.Sending) stateText = UiStrings.Get(UiStrings.K.Sending, lang);
                else if (m.State == InngameMessageState.Failed) stateText = UiStrings.Get(UiStrings.K.Failed, lang);
            }
            var tt = UiText.Create(footer, "Time", style, FontSize - 5, meta, TextAnchor.MiddleRight);
            tt.Text = stateText;
            tt.SingleLine = true;
            if (mine && m.State == InngameMessageState.Sent)
            {
                var ck = Img(footer, "Sent", UiSprites.Icon("check"), meta);
                Le(ck.gameObject, 14, 14);
            }
            if (mine && m.State == InngameMessageState.Failed)
            {
                var al = Img(footer, "Failed", UiSprites.Icon("alert"), textColor);
                Le(al.gameObject, 14, 14);
                var retry = bubble.gameObject.AddComponent<Button>();
                retry.targetGraphic = bimg;
                retry.onClick.AddListener(() => rt.Retry(m));
                AddFocusFrame(retry, bubble, theme.CornerRadius);
                listFocusables.Add(retry);
            }
        }

        Color AuthorColor() => InngameTheme.Luminance(theme.Accent) < 0.25f && theme.IsDark ? Color.Lerp(theme.Accent, Color.white, 0.45f) : theme.Accent;

        void ImageTile(Transform parent, InngameAttachment att, Texture2D local, float maxW)
        {
            float w = att.Width > 0 ? att.Width : 16, h = att.Height > 0 ? att.Height : 9;
            float box = Mathf.Min(260, maxW);
            float k = Mathf.Min(box / w, box / h);
            var size = new Vector2(Mathf.Max(80, w * k), Mathf.Max(60, h * k));
            var holder = NewRect(parent, "Image");
            var frame = holder.gameObject.AddComponent<Image>();
            SetRounded(frame, Mathf.Max(6, theme.CornerRadius - 6));
            frame.color = new Color(0, 0, 0, 0.18f);
            holder.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Le(holder.gameObject, size.x, size.y);
            var ri = NewRect(holder, "Texture").gameObject.AddComponent<RawImage>();
            Stretch(ri.rectTransform);
            ri.raycastTarget = false;
            ri.color = new Color(1, 1, 1, 0);
            var btn = holder.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            Texture loaded = null;
            btn.onClick.AddListener(() => OpenViewer(loaded));
            AddFocusFrame(btn, holder, Mathf.Max(6, theme.CornerRadius - 6));
            listFocusables.Add(btn);
            void SetTex(Texture2D t)
            {
                if (ri == null || t == null) return;
                loaded = t; ri.texture = t; ri.color = Color.white;
            }
            if (local != null) SetTex(local);
            else ImageCache.Get(this, att.Url, SetTex);
        }

        /// <summary>Satisfaction: the question in the player's language and five buttons (1 to 5), reachable with a gamepad.</summary>
        void RatingCard(Transform parent, InngameMessage m)
        {
            var c = m.Card;
            var q = UiText.Create(parent, "Question", style, FontSize, theme.IncomingText, TextAnchor.UpperLeft);
            if (c.Rating.HasValue)
            {
                q.Text = $"{UiStrings.Get(UiStrings.K.RateThanks, lang)} ({c.Rating.Value}/5)";
                return;
            }
            q.Text = UiStrings.Get(UiStrings.K.RateQuestion, lang);
            var row = NewRect(parent, "Scores");
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            for (int i = 1; i <= 5; i++)
            {
                int score = i;
                var b = NewRect(row, "Score" + score);
                var img = b.gameObject.AddComponent<Image>();
                SetRounded(img, Mathf.Max(8, theme.CornerRadius - 6));
                // Du plus froid au plus chaud : la couleur d'accent monte avec la note.
                img.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, 0.35f + 0.13f * score);
                var bh = b.gameObject.AddComponent<HorizontalLayoutGroup>();
                bh.childAlignment = TextAnchor.MiddleCenter; bh.childControlWidth = true; bh.childControlHeight = true;
                Le(b.gameObject, 44, 44);
                var t = UiText.Create(b, "Label", style, FontSize, InngameTheme.ReadableOn(theme.Accent), TextAnchor.MiddleCenter);
                t.Text = score.ToString();
                t.Bold = true;
                t.SingleLine = true;
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.interactable = m.Id != null;
                btn.onClick.AddListener(() => rt.RateCard(m, score));
                AddFocusFrame(btn, b, Mathf.Max(8, theme.CornerRadius - 6));
                listFocusables.Add(btn);
            }
        }

        void CardButton(Transform parent, InngameMessage m, bool mine)
        {
            var c = m.Card;
            bool claimed = c.Kind == "item" && c.LastAction == "claimed";
            string label = c.CustomLabel;
            if (label == null)
                label = c.Kind switch
                {
                    "review" => UiStrings.Get(UiStrings.K.Review, lang),
                    "refund" => UiStrings.Get(UiStrings.K.Refund, lang),
                    "item" => UiStrings.Get(UiStrings.K.Claim, lang) + (c.Item != null ? $" · {c.Item.Name}{(c.Item.Quantity > 1 ? " ×" + c.Item.Quantity : "")}" : ""),
                    _ => UiStrings.Get(UiStrings.K.Open, lang),
                };
            if (claimed) label = UiStrings.Get(UiStrings.K.Claimed, lang) + (c.Item != null ? " · " + c.Item.Name : "");

            if (c.Kind == "item" && c.Item != null && !string.IsNullOrEmpty(c.Label))
            {
                var info = UiText.Create(parent, "Item", style, FontSize - 3, mine ? theme.OutgoingText : theme.MutedText, TextAnchor.UpperLeft);
                info.Text = c.Item.Quantity > 1 ? $"{c.Item.Name} ×{c.Item.Quantity}" : c.Item.Name;
            }

            var b = NewRect(parent, "Card");
            var img = b.gameObject.AddComponent<Image>();
            SetRounded(img, Mathf.Max(8, theme.CornerRadius - 4));
            Color bg = mine ? new Color(1, 1, 1, 0.18f) : (claimed ? new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, 0.25f) : theme.Accent);
            img.color = bg;
            if (theme.ButtonSprite != null && !mine) { Skin(img, theme.ButtonSprite, bg, 0); if (claimed) img.color *= new Color(1, 1, 1, 0.5f); }
            var h = b.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 10, 10); h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            Le(b.gameObject, Mathf.Min(260, ListWidth * 0.6f), 44);
            var t = UiText.Create(b, "Label", style, FontSize - 1, claimed ? theme.Text : InngameTheme.ReadableOn(bg.a < 0.5f ? theme.IncomingBubble : bg), TextAnchor.MiddleCenter);
            t.Text = label;
            t.Bold = true;
            t.SingleLine = true;
            var btn = b.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = !claimed && m.Id != null;
            btn.onClick.AddListener(() => rt.TapCard(m));
            AddFocusFrame(btn, b, Mathf.Max(8, theme.CornerRadius - 4));
            listFocusables.Add(btn);
        }

        void RebuildAttachments()
        {
            for (int i = attachRow.transform.childCount - 1; i >= 0; i--) Destroy(attachRow.transform.GetChild(i).gameObject);
            var list = rt.PendingAttachments;
            attachRow.SetActive(list.Count > 0 && !rt.Unavailable);
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                var th = NewRect(attachRow.transform, "Thumb");
                th.sizeDelta = new Vector2(64, 64);
                var frame = th.gameObject.AddComponent<Image>();
                SetRounded(frame, 10);
                frame.color = theme.InputBackground;
                th.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var ri = NewRect(th, "Texture").gameObject.AddComponent<RawImage>();
                Stretch(ri.rectTransform);
                ri.raycastTarget = false;
                ri.texture = list[i].Preview;
                if (list[i].Preview != null)
                {
                    // crop to square
                    float a = (float)list[i].Preview.width / Mathf.Max(1, list[i].Preview.height);
                    ri.uvRect = a > 1 ? new Rect((1 - 1 / a) / 2, 0, 1 / a, 1) : new Rect(0, (1 - a) / 2, 1, a);
                }
                var x = IconButton(attachRow.transform, "Remove", "close", new Color(0, 0, 0, 0.65f), Color.white, 24, 12, true);
                var xr = (RectTransform)x.transform;
                xr.SetParent(th, false);
                xr.anchorMin = xr.anchorMax = new Vector2(1, 1); xr.pivot = new Vector2(1, 1);
                xr.anchoredPosition = new Vector2(-3, -3);
                xr.sizeDelta = new Vector2(24, 24);
                Destroy(xr.GetComponent<LayoutElement>());
                x.onClick.AddListener(() => rt.RemovePendingAttachment(index));
            }
        }

        // ==================== navigation ====================

        void BuildNavigation()
        {
            if (closeBtn == null) return;
            var composer = new List<Selectable>();
            if (!NoComposer)
            {
                if (shotBtn.gameObject.activeSelf) composer.Add(shotBtn);
                if (photoBtn != null && photoBtn.gameObject.activeSelf) composer.Add(photoBtn);
                composer.Add(input.Selectable);
                composer.Add(sendBtn);
            }
            listFocusables.RemoveAll(s => s == null);
            if (rt.Unavailable) listFocusables.Clear(); // the conversation is hidden: only the close button is reachable
            Selectable firstList = listFocusables.Count > 0 ? listFocusables[0] : null;
            Selectable lastList = listFocusables.Count > 0 ? listFocusables[listFocusables.Count - 1] : null;
            Selectable composerMain = NoComposer ? null : input.Selectable;

            SetNav(closeBtn, up: null, down: firstList ?? composerMain, left: null, right: null);
            for (int i = 0; i < listFocusables.Count; i++)
                SetNav(listFocusables[i], i > 0 ? listFocusables[i - 1] : closeBtn, i < listFocusables.Count - 1 ? listFocusables[i + 1] : composerMain, null, null);
            for (int i = 0; i < composer.Count; i++)
                SetNav(composer[i], lastList ?? closeBtn, null, i > 0 ? composer[i - 1] : null, i < composer.Count - 1 ? composer[i + 1] : null);
            foreach (var t in attachRow.GetComponentsInChildren<Button>()) t.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        static void SetNav(Selectable s, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            if (s == null) return;
            s.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
        }

        // ==================== helpers ====================

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform r, float x = 0, float y = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(x, y); r.offsetMax = new Vector2(-x, -y);
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var img = NewRect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static LayoutElement Le(GameObject go, float w, float h, float flexW = -1, float flexH = -1)
        {
            var le = GetOrAdd<LayoutElement>(go);
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
            return le;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        /// <summary>Sprite du jeu (9-slice) s'il y en a un, sinon le rectangle arrondi aux couleurs du thème.</summary>
        void Skin(Image img, Sprite sprite, Color color, float radius)
        {
            if (sprite == null) { SetRounded(img, radius); img.color = color; return; }
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = theme.SpriteScale;
            img.color = theme.TintSprites ? color : Color.white;
        }

        static readonly Dictionary<string, Sprite> logos = new Dictionary<string, Sprite>();

        static void ShowLogo(Image avatar, UiText initial, Sprite logo)
        {
            if (avatar == null) return;
            avatar.sprite = logo;
            avatar.type = Image.Type.Simple;
            avatar.preserveAspect = true;
            avatar.color = Color.white;
            initial.GameObject.SetActive(false);
        }

        System.Collections.IEnumerator LoadLogo(string url, Image avatar, UiText initial)
        {
            if (logos.TryGetValue(url, out var cached)) { ShowLogo(avatar, initial, cached); yield break; }
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) yield break;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Inngame Logo" };
                if (!tex.LoadImage(req.downloadHandler.data, true)) { Destroy(tex); yield break; }
                tex.filterMode = FilterMode.Bilinear;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                logos[url] = sprite;
                ShowLogo(avatar, initial, sprite);
            }
        }

        static void SetRounded(Image img, float radius)
        {
            if (radius <= 0.5f) { img.sprite = null; img.type = Image.Type.Simple; return; }
            img.sprite = UiSprites.Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = UiSprites.RoundedRadiusPx / radius;
        }

        void AddFocusFrame(Selectable s, Transform frameParent, float radius)
        {
            var f = Img(frameParent, "Focus", UiSprites.RoundedOutline, theme.FocusOutline);
            f.type = Image.Type.Sliced;
            f.pixelsPerUnitMultiplier = UiSprites.RoundedRadiusPx / Mathf.Max(4, radius + 3);
            var r = f.rectTransform;
            Stretch(r, -3, -3);
            f.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            f.gameObject.SetActive(false);
            var focus = GetOrAdd<UiFocus>(s.gameObject);
            focus.Frame = f.gameObject;
            var colors = s.colors;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1);
            colors.disabledColor = new Color(1, 1, 1, 0.45f);
            colors.fadeDuration = 0.08f;
            s.colors = colors;
        }

        Button IconButton(Transform parent, string name, string icon, Color bg, Color fg, float size, float iconSize, bool circle = false)
        {
            var r = NewRect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = circle ? UiSprites.Circle : UiSprites.Rounded;
            if (!circle) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = UiSprites.RoundedRadiusPx / (size / 2); }
            img.color = bg.a > 0 ? bg : new Color(fg.r, fg.g, fg.b, 0.0001f);
            Le(r.gameObject, size, size);
            r.sizeDelta = new Vector2(size, size);
            var ic = Img(r, "Icon", UiSprites.Icon(icon), fg);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ic.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            if (bg.a <= 0)
            {
                // Transparent buttons: highlight with a faint fill instead of tinting an invisible image.
                b.transition = Selectable.Transition.SpriteSwap;
                var hover = r.gameObject.AddComponent<HoverFill>();
                hover.Target = img;
                hover.Fill = new Color(fg.r, fg.g, fg.b, 0.14f);
            }
            AddFocusFrame(b, r, circle ? size / 2 : size / 2);
            return b;
        }

        Button TextButton(Transform parent, string label, int size, Color color)
        {
            var r = NewRect(parent, "Link");
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.0001f);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true; h.childControlHeight = true;
            var t = UiText.Create(r, "Text", style, size, color, TextAnchor.MiddleLeft);
            t.Text = label;
            t.SingleLine = true;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            AddFocusFrame(b, r, 6);
            return b;
        }
    }

    /// <summary>Faint background on hover/selection for icon buttons without a background.</summary>
    [AddComponentMenu("")]
    internal sealed class HoverFill : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Image Target;
        public Color Fill;
        bool hover, selected;
        public void OnPointerEnter(PointerEventData e) { hover = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { hover = false; Apply(); }
        public void OnSelect(BaseEventData e) { selected = true; Apply(); }
        public void OnDeselect(BaseEventData e) { selected = false; Apply(); }
        void Apply() { if (Target != null) Target.color = hover || selected ? Fill : new Color(Fill.r, Fill.g, Fill.b, 0.0001f); }
    }
}
