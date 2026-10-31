using UnityEngine;

namespace InngameSDK
{
    /// <summary>
    /// Project settings (Tools › Inngame › Settings). Stored in Assets/Resources/InngameSettings.asset so that
    /// <see cref="Inngame.ConfigureFromSettings"/> can load it at runtime.
    /// </summary>
    public sealed class InngameSettings : ScriptableObject
    {
        public const string ResourceName = "InngameSettings";

        [Tooltip("Public app key (app_…) from the Inngame dashboard. It is safe to ship in the game.")]
        public string AppKey = "";
        public string BaseUrl = Inngame.DefaultBaseUrl;
        [Tooltip("Configure the SDK automatically when the game starts.")]
        public bool AutoInitialize;
        public InngameTheme Theme;
        public bool PauseGameWhileOpen;
        public KeyCode ScreenshotHotkey = KeyCode.F8;
        public bool ShakeToReport;
        public InngameLogLevel LogLevel = InngameLogLevel.Warning;

        public static InngameSettings Load() => Resources.Load<InngameSettings>(ResourceName);

        public InngameOptions ToOptions() => new InngameOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? Inngame.DefaultBaseUrl : BaseUrl.Trim(),
            Theme = Theme,
            PauseGameWhileOpen = PauseGameWhileOpen,
            ScreenshotHotkey = ScreenshotHotkey,
            ShakeToReport = ShakeToReport,
            LogLevel = LogLevel,
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInit()
        {
            if (Inngame.IsConfigured) return;
            var s = Load();
            if (s != null && s.AutoInitialize && !string.IsNullOrWhiteSpace(s.AppKey)) Inngame.Configure(s.AppKey.Trim(), s.ToOptions());
        }
    }
}
