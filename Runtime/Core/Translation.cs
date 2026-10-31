namespace InngameSDK
{
    /// <summary>Chooses which text to display for a message (translation vs original).</summary>
    public static class InngameTranslation
    {
        /// <summary>"pt-BR" → "pt", "zh_Hant" → "zh" (same rule as the server).</summary>
        public static string BaseLang(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            var t = tag.Trim().ToLowerInvariant();
            int cut = t.IndexOfAny(new[] { '-', '_' });
            return cut >= 0 ? t.Substring(0, cut) : t;
        }

        /// <summary>
        /// Text to display to the player. Team/AI/system messages show the translation into the player's base language when one exists
        /// (unless the player chose "Show original"); the player's own messages always show what they typed.
        /// </summary>
        public static string DisplayBody(InngameMessage m, string playerLanguage, out bool isTranslated)
        {
            isTranslated = false;
            if (m == null) return "";
            if (m.IsMine) return m.Body ?? "";
            var key = BaseLang(playerLanguage);
            if (key.Length > 0 && m.Translations != null && m.Translations.TryGetValue(key, out var t) && !string.IsNullOrWhiteSpace(t)
                && BaseLang(m.Lang) != key && t != m.Body)
            {
                isTranslated = true;
                return m.ShowOriginal ? (m.Body ?? "") : t;
            }
            return m.Body ?? "";
        }
    }
}
