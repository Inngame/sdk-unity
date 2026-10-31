using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if INNGAME_TMP
using TMPro;
#endif

namespace InngameSDK
{
    /// <summary>Text that is either TextMeshPro (when installed and a font is available) or legacy UnityEngine.UI.Text.</summary>
    internal sealed class UiText
    {
        public readonly GameObject GameObject;
        public readonly RectTransform Rect;
        readonly Text legacy;
#if INNGAME_TMP
        readonly TMP_Text tmp;
#endif
        public Graphic Graphic
        {
            get
            {
#if INNGAME_TMP
                if (tmp != null) return tmp;
#endif
                return legacy;
            }
        }

        UiText(GameObject go, Text t) { GameObject = go; Rect = (RectTransform)go.transform; legacy = t; }
#if INNGAME_TMP
        UiText(GameObject go, TMP_Text t) { GameObject = go; Rect = (RectTransform)go.transform; tmp = t; }
#endif

        public static UiText Create(Transform parent, string name, UiStyle style, int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiText ui;
#if INNGAME_TMP
            if (style.Tmp != null)
            {
                var t = go.AddComponent<TextMeshProUGUI>();
                t.font = style.Tmp;
                t.richText = false;
                t.raycastTarget = false;
                SetWrap(t, true);
                t.overflowMode = TextOverflowModes.Overflow;
                ui = new UiText(go, t);
            }
            else
#endif
            {
                var t = go.AddComponent<Text>();
                t.font = style.Font;
                t.supportRichText = false;
                t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.lineSpacing = 1.05f;
                ui = new UiText(go, t);
            }
            ui.Size = size;
            ui.Color = color;
            ui.Align(align);
            return ui;
        }

        public string Text
        {
            get
            {
#if INNGAME_TMP
                if (tmp != null) return tmp.text;
#endif
                return legacy.text;
            }
            set
            {
#if INNGAME_TMP
                if (tmp != null) { tmp.text = value; return; }
#endif
                legacy.text = value;
            }
        }

        public Color Color
        {
            get => Graphic.color;
            set => Graphic.color = value;
        }

        public int Size
        {
            set
            {
#if INNGAME_TMP
                if (tmp != null) { tmp.fontSize = value; return; }
#endif
                legacy.fontSize = value;
            }
        }

        public bool Bold
        {
            set
            {
#if INNGAME_TMP
                if (tmp != null) { tmp.fontStyle = value ? FontStyles.Bold : FontStyles.Normal; return; }
#endif
                legacy.fontStyle = value ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        public bool SingleLine
        {
            set
            {
#if INNGAME_TMP
                if (tmp != null) { SetWrap(tmp, !value); tmp.overflowMode = value ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow; return; }
#endif
                legacy.horizontalOverflow = value ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
            }
        }

        public void Align(TextAnchor a)
        {
#if INNGAME_TMP
            if (tmp != null) { tmp.alignment = ToTmp(a); return; }
#endif
            legacy.alignment = a;
        }

#if INNGAME_TMP
        static void SetWrap(TMP_Text t, bool wrap)
        {
#if UNITY_2023_2_OR_NEWER
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#else
            t.enableWordWrapping = wrap;
#endif
        }

        static TextAlignmentOptions ToTmp(TextAnchor a) => a switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight,
        };
#endif
    }

    /// <summary>Message field: TMP_InputField or legacy InputField, multi-line, Enter sends (Shift+Enter: new line with TMP).</summary>
    internal sealed class UiInput
    {
        public readonly GameObject GameObject;
        public readonly Selectable Selectable;
        readonly InputField legacy;
        readonly UiText placeholder;
#if INNGAME_TMP
        readonly TMP_InputField tmp;
#endif
        public event Action<string> Submitted;
        public event Action<string> Changed;

        public UiInput(Transform parent, UiStyle style, int size, Color text, Color placeholderColor, string placeholderText)
        {
            GameObject = new GameObject("Input", typeof(RectTransform));
            GameObject.transform.SetParent(parent, false);
            var bg = GameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0); // raycast target for clicks; the visible background is the parent pill
#if INNGAME_TMP
            if (style.Tmp != null)
            {
                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(GameObject.transform, false);
                Stretch((RectTransform)viewport.transform, 0, 0);
                var txt = UiText.Create(viewport.transform, "Text", style, size, text);
                Stretch(txt.Rect, 0, 0);
                placeholder = UiText.Create(viewport.transform, "Placeholder", style, size, placeholderColor);
                Stretch(placeholder.Rect, 0, 0);
                placeholder.Text = placeholderText;
                tmp = GameObject.AddComponent<TMP_InputField>();
                tmp.textViewport = (RectTransform)viewport.transform;
                tmp.textComponent = (TMP_Text)txt.Graphic;
                tmp.placeholder = placeholder.Graphic;
                tmp.fontAsset = style.Tmp;
                tmp.pointSize = size;
                tmp.lineType = TMP_InputField.LineType.MultiLineSubmit;
                tmp.characterLimit = 4000;
                tmp.richText = false;
                tmp.caretColor = text;
                tmp.customCaretColor = true;
                tmp.selectionColor = new Color(text.r, text.g, text.b, 0.25f);
                tmp.onSubmit.AddListener(v => Submitted?.Invoke(v));
                tmp.onValueChanged.AddListener(v => Changed?.Invoke(v));
                Selectable = tmp;
                return;
            }
#endif
            var t = UiText.Create(GameObject.transform, "Text", style, size, text);
            Stretch(t.Rect, 0, 0);
            placeholder = UiText.Create(GameObject.transform, "Placeholder", style, size, placeholderColor);
            Stretch(placeholder.Rect, 0, 0);
            placeholder.Text = placeholderText;
            legacy = GameObject.AddComponent<InputField>();
            legacy.textComponent = (Text)t.Graphic;
            legacy.placeholder = placeholder.Graphic;
            legacy.lineType = InputField.LineType.MultiLineSubmit;
            legacy.characterLimit = 4000;
            legacy.caretColor = text;
            legacy.customCaretColor = true;
            legacy.caretWidth = 2;
            legacy.selectionColor = new Color(text.r, text.g, text.b, 0.25f);
            legacy.onEndEdit.AddListener(v =>
            {
                bool enter = InputCompat.GetKeyDown(KeyCode.Return) || InputCompat.GetKeyDown(KeyCode.KeypadEnter);
                bool kbDone = legacy.touchScreenKeyboard != null && legacy.touchScreenKeyboard.status == TouchScreenKeyboard.Status.Done;
                if (enter || kbDone) Submitted?.Invoke(v);
            });
            legacy.onValueChanged.AddListener(v => Changed?.Invoke(v));
            Selectable = legacy;
        }

        static void Stretch(RectTransform r, float x, float y)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(x, y); r.offsetMax = new Vector2(-x, -y);
        }

        public string Text
        {
            get
            {
#if INNGAME_TMP
                if (tmp != null) return tmp.text;
#endif
                return legacy.text;
            }
            set
            {
#if INNGAME_TMP
                if (tmp != null) { tmp.text = value; return; }
#endif
                legacy.text = value;
            }
        }

        public string Placeholder { set => placeholder.Text = value; }

        public bool IsFocused
        {
            get
            {
#if INNGAME_TMP
                if (tmp != null) return tmp.isFocused;
#endif
                return legacy.isFocused;
            }
        }

        public bool Interactable { set => Selectable.interactable = value; }

        public void Focus()
        {
#if INNGAME_TMP
            if (tmp != null) { tmp.Select(); tmp.ActivateInputField(); return; }
#endif
            legacy.Select();
            legacy.ActivateInputField();
        }

        public void Unfocus()
        {
#if INNGAME_TMP
            if (tmp != null) { tmp.DeactivateInputField(); return; }
#endif
            legacy.DeactivateInputField();
        }

        public void SetColors(Color text, Color ph)
        {
#if INNGAME_TMP
            if (tmp != null) { tmp.textComponent.color = text; tmp.caretColor = text; tmp.selectionColor = new Color(text.r, text.g, text.b, 0.25f); placeholder.Color = ph; return; }
#endif
            legacy.textComponent.color = text;
            legacy.caretColor = text;
            legacy.selectionColor = new Color(text.r, text.g, text.b, 0.25f);
            placeholder.Color = ph;
        }

        /// <summary>Preferred height of the typed text at the current width (for the auto-growing composer).</summary>
        public float TextPreferredHeight
        {
            get
            {
#if INNGAME_TMP
                if (tmp != null) return tmp.textComponent.preferredHeight;
#endif
                return legacy.textComponent.preferredHeight;
            }
        }
    }

    /// <summary>Font choice for the whole chat.</summary>
    internal sealed class UiStyle
    {
        public Font Font;
#if INNGAME_TMP
        public TMP_FontAsset Tmp;
#endif

        public static UiStyle From(InngameTheme theme)
        {
            var s = new UiStyle();
            s.Font = theme.LegacyFont != null ? theme.LegacyFont : DefaultFont();
#if INNGAME_TMP
            if (theme.TextBackend != InngameTextBackend.Legacy)
            {
                s.Tmp = theme.TmpFont as TMP_FontAsset;
                if (s.Tmp == null)
                {
                    // Only use TMP's default font if the project imported TMP Essentials (otherwise TMP would pop its importer).
                    var settings = Resources.Load<TMP_Settings>("TMP Settings");
                    if (settings != null) s.Tmp = TMP_Settings.defaultFontAsset;
                }
                if (theme.TextBackend == InngameTextBackend.Auto && theme.TmpFont == null && s.Tmp != null && !(s.Tmp.atlasPopulationMode == AtlasPopulationMode.Dynamic))
                {
                    // A static default atlas misses most non-Latin glyphs: legacy Text (OS font fallback) is safer.
                    s.Tmp = null;
                }
            }
#endif
            return s;
        }

        static Font builtin;
        public static Font DefaultFont()
        {
            if (builtin != null) return builtin;
            try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (builtin == null) { try { builtin = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { } }
            if (builtin == null) builtin = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Segoe UI", "Roboto", "Arial" }, 16);
            return builtin;
        }
    }
}
