using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace InngameSDK
{
    public enum InngameThemeMode { Dark, Light }
    public enum InngameTextBackend { Auto, Legacy, TextMeshPro }

    /// <summary>
    /// Look of the chat. Create one with Assets › Create › Inngame › Theme, or in code with <see cref="CreateDark"/> / <see cref="CreateLight"/>.
    /// Server appearance settings override colors and texts at runtime (see <see cref="WithAppearance"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Inngame/Theme", fileName = "InngameTheme")]
    public sealed class InngameTheme : ScriptableObject
    {
        public InngameThemeMode Mode = InngameThemeMode.Dark;

        [Header("Colors")]
        public Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
        public Color Background = Hex("#15171C");
        public Color Header = Hex("#1D2027");
        public Color Text = Hex("#F2F3F5");
        public Color MutedText = Hex("#9AA0AB");
        public Color Accent = Hex("#7C5CFF");
        public Color AccentText = Color.white;
        public Color IncomingBubble = Hex("#262A33");
        public Color IncomingText = Hex("#F2F3F5");
        public Color OutgoingBubble = Hex("#7C5CFF");
        public Color OutgoingText = Color.white;
        public Color InputBackground = Hex("#22252D");
        public Color Danger = Hex("#FF5A5F");
        public Color Warning = Hex("#E8A33D");
        public Color FocusOutline = Hex("#FFFFFF");

        [Header("Shape & text")]
        [Range(0, 40)] public float CornerRadius = 18f;
        [Range(10, 40)] public int FontSize = 17;
        public InngameTextBackend TextBackend = InngameTextBackend.Auto;
        /// <summary>Legacy UnityEngine.UI.Text font. Null: Unity's built-in font (uses OS fallback fonts, so CJK works).</summary>
        public Font LegacyFont;
        /// <summary>TextMeshPro font asset (TMP_FontAsset). Used when TextMeshPro is installed; add fallback fonts for CJK.</summary>
        public Object TmpFont;

        [Header("Logo")]
        /// <summary>Shown in the chat header instead of the initial. Null: the logo uploaded in the dashboard (Appearance), if any.</summary>
        public Sprite Logo;
        /// <summary>Logo uploaded in the dashboard (set from the server's <c>appearance.logoUrl</c>).</summary>
        [HideInInspector] public string LogoUrl = "";

        [Header("Skin (optional 9-sliced sprites from your game's UI)")]
        /// <summary>Panel frame (the whole chat window).</summary>
        public Sprite PanelSprite;
        public Sprite HeaderSprite;
        /// <summary>Bar at the bottom around the text field. Null: <see cref="HeaderSprite"/>.</summary>
        public Sprite ComposerSprite;
        public Sprite IncomingBubbleSprite;
        public Sprite OutgoingBubbleSprite;
        public Sprite InputSprite;
        /// <summary>Send button and card buttons.</summary>
        public Sprite ButtonSprite;
        /// <summary>Multiplies the colors above with the sprites. Off: sprites keep their own colors (only text colors apply).</summary>
        public bool TintSprites;
        /// <summary>Image.pixelsPerUnitMultiplier for the sprites: higher = thinner borders.</summary>
        [Range(0.25f, 8f)] public float SpriteScale = 1f;
        /// <summary>Extra inner padding (px) for bubbles and bars, for ornate frames.</summary>
        [Range(0, 40)] public int SkinPadding;

        [Header("Texts (empty = localized default)")]
        public string Title = "";
        public string WelcomeTitle = "";
        public string WelcomeMessage = "";
        public string Placeholder = "";

        public bool IsDark => Mode == InngameThemeMode.Dark;

        public static InngameTheme CreateDark()
        {
            var t = CreateInstance<InngameTheme>();
            t.name = "Inngame Dark";
            return t;
        }

        public static InngameTheme CreateLight()
        {
            var t = CreateInstance<InngameTheme>();
            t.name = "Inngame Light";
            t.Mode = InngameThemeMode.Light;
            t.Backdrop = new Color(0f, 0f, 0f, 0.35f);
            t.Background = Hex("#F6F7F9");
            t.Header = Hex("#FFFFFF");
            t.Text = Hex("#16181D");
            t.MutedText = Hex("#6B7280");
            t.Accent = Hex("#5B3DF5");
            t.IncomingBubble = Hex("#FFFFFF");
            t.IncomingText = Hex("#16181D");
            t.OutgoingBubble = Hex("#5B3DF5");
            t.OutgoingText = Color.white;
            t.InputBackground = Hex("#EEF0F3");
            t.FocusOutline = Hex("#16181D");
            return t;
        }

        /// <summary>
        /// Copy of this theme with the server's <c>config.appearance</c> applied:
        /// <c>{ title, welcomeTitle, welcomeMessage, placeholder, light:{accent,background,text}, dark:{…} }</c>
        /// (flat keys such as <c>accent</c>, <c>background</c>, <c>incoming</c>, <c>outgoing</c> and <c>mode</c> are accepted too).
        /// </summary>
        public InngameTheme WithAppearance(IDictionary<string, object> appearance)
        {
            var t = Instantiate(this);
            t.name = name;
            if (appearance == null || appearance.Count == 0) return t;
            var mode = appearance.GetString("mode");
            if (mode == "light") t.Mode = InngameThemeMode.Light;
            else if (mode == "dark") t.Mode = InngameThemeMode.Dark;

            t.Title = NonEmpty(appearance.GetString("title"), t.Title);
            t.WelcomeTitle = NonEmpty(appearance.GetString("welcomeTitle"), t.WelcomeTitle);
            t.WelcomeMessage = NonEmpty(appearance.GetString("welcomeMessage"), t.WelcomeMessage);
            t.Placeholder = NonEmpty(appearance.GetString("placeholder"), t.Placeholder);
            t.LogoUrl = appearance.GetString("logoUrl") ?? "";

            t.ApplyColors(appearance);
            if (appearance is Dictionary<string, object> d) t.ApplyColors(d.GetDict(t.IsDark ? "dark" : "light"));
            return t;
        }

        void ApplyColors(IDictionary<string, object> c)
        {
            if (c == null) return;
            if (TryHex(c.GetString("accent"), out var accent))
            {
                Accent = accent;
                OutgoingBubble = accent;
                AccentText = OutgoingText = ReadableOn(accent);
            }
            if (TryHex(c.GetString("background"), out var bg))
            {
                Background = bg;
                bool darkBg = Luminance(bg) < 0.4f;
                Header = Shift(bg, darkBg ? 0.05f : 0.03f, darkBg);
                IncomingBubble = Shift(bg, darkBg ? 0.09f : 0.06f, darkBg);
                InputBackground = Shift(bg, 0.07f, darkBg);
            }
            if (TryHex(c.GetString("text"), out var text)) { Text = text; IncomingText = text; MutedText = Color.Lerp(text, Background, 0.42f); }
            if (TryHex(c.GetString("panel"), out var panel)) Header = panel;
            if (TryHex(c.GetString("header"), out var header)) Header = header;
            if (TryHex(c.GetString("incoming"), out var inc)) { IncomingBubble = inc; IncomingText = ReadableOn(inc); }
            if (TryHex(c.GetString("outgoing"), out var outg)) { OutgoingBubble = outg; OutgoingText = ReadableOn(outg); }
        }

        static string NonEmpty(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;

        /// <summary>Lighter on dark backgrounds, darker on light ones.</summary>
        static Color Shift(Color c, float amount, bool lighten) => lighten ? Color.Lerp(c, Color.white, amount) : Color.Lerp(c, Color.black, amount);

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        public static Color ReadableOn(Color bg) => Luminance(bg) > 0.6f ? new Color(0.08f, 0.09f, 0.11f) : Color.white;

        /// <summary>Parses "#RGB", "#RGBA", "#RRGGBB" or "#RRGGBBAA" (the "#" is optional).</summary>
        public static bool TryHex(string s, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s[0] == '#') s = s.Substring(1);
            if (s.Length == 3 || s.Length == 4)
            {
                var e = new char[s.Length * 2];
                for (int i = 0; i < s.Length; i++) { e[2 * i] = s[i]; e[2 * i + 1] = s[i]; }
                s = new string(e);
            }
            if (s.Length != 6 && s.Length != 8) return false;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            if (s.Length == 6) v = (v << 8) | 0xFF;
            color = new Color32((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        public static Color Hex(string s) => TryHex(s, out var c) ? c : Color.magenta;
    }
}
