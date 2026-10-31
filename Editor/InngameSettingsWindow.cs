using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace InngameSDK.Editor
{
    /// <summary>Tools › Inngame › Settings: app key, server, auto-init, and a real end-to-end test message.</summary>
    public sealed class InngameSettingsWindow : EditorWindow
    {
        const string AssetPath = "Assets/Resources/InngameSettings.asset";
        InngameSettings settings;
        SerializedObject so;
        string status = "";
        MessageType statusType = MessageType.None;
        bool busy;

        [MenuItem("Tools/Inngame/Settings", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<InngameSettingsWindow>("Inngame");
            w.minSize = new Vector2(420, 360);
            w.Show();
        }

        [MenuItem("Tools/Inngame/Documentation", priority = 20)]
        static void Docs() => Application.OpenURL("https://inngame.gg/docs/unity");

        public static InngameSettings LoadOrCreate()
        {
            var s = AssetDatabase.LoadAssetAtPath<InngameSettings>(AssetPath);
            if (s != null) return s;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            s = CreateInstance<InngameSettings>();
            AssetDatabase.CreateAsset(s, AssetPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        void OnEnable()
        {
            settings = AssetDatabase.LoadAssetAtPath<InngameSettings>(AssetPath);
            if (settings != null) so = new SerializedObject(settings);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Inngame", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("In-game support chat · SDK " + Inngame.SdkVersion, EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings asset yet. It will be created at " + AssetPath + " (it must stay in a Resources folder).", MessageType.Info);
                if (GUILayout.Button("Create settings", GUILayout.Height(28)))
                {
                    settings = LoadOrCreate();
                    so = new SerializedObject(settings);
                }
                return;
            }

            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.AppKey)), new GUIContent("App Key", "Dashboard › App › Install. Starts with app_"));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.BaseUrl)), new GUIContent("Server URL"));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.AutoInitialize)), new GUIContent("Auto initialize", "Call Inngame.Configure automatically when the game starts"));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.Theme)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.PauseGameWhileOpen)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.ScreenshotHotkey)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.ShakeToReport)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(InngameSettings.LogLevel)));
            so.ApplyModifiedProperties();

            var key = settings.AppKey?.Trim() ?? "";
            if (key.Length > 0 && !key.StartsWith("app_")) EditorGUILayout.HelpBox("App keys start with \"app_\".", MessageType.Warning);

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(busy || key.Length == 0))
            {
                if (GUILayout.Button(busy ? "Sending…" : "Send test message", GUILayout.Height(30))) _ = SendTest(key, settings.BaseUrl);
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Usage", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(settings.AutoInitialize
                ? "Inngame.OpenChat();   // e.g. from your pause menu's Help button"
                : "Inngame.ConfigureFromSettings();\nInngame.OpenChat();", EditorStyles.textArea, GUILayout.Height(40));
        }

        /// <summary>Opens a real session (as an "Editor test" player) and sends a message, so it shows up in the inbox.</summary>
        async Task SendTest(string appKey, string baseUrl)
        {
            busy = true;
            status = "Connecting…"; statusType = MessageType.Info;
            Repaint();
            try
            {
                var r = await SendTestMessage(appKey, baseUrl, "Test message from the Unity Editor 👋");
                status = r.ok ? $"Sent! Open your inbox: conversation {r.conversationId}" : "Failed: " + r.error;
                statusType = r.ok ? MessageType.Info : MessageType.Error;
            }
            finally
            {
                busy = false;
                Repaint();
            }
        }

        public static async Task<(bool ok, string conversationId, string error)> SendTestMessage(string appKey, string baseUrl, string text)
        {
            const string tokenKey = "inngame.editor-test.token";
            var api = new ApiClient(string.IsNullOrWhiteSpace(baseUrl) ? Inngame.DefaultBaseUrl : baseUrl.Trim(), appKey)
            {
                DeviceToken = EditorPrefs.GetString(tokenKey + appKey, null),
            };
            var s = await api.Post("/session", new Dictionary<string, object>
            {
                ["device"] = new Dictionary<string, object> { ["platform"] = "other", ["model"] = "Unity Editor", ["appVersion"] = Application.version, ["sdkVersion"] = "unity-" + Inngame.SdkVersion, ["language"] = "en" },
                ["identity"] = new Dictionary<string, object> { ["name"] = "Unity Editor test" },
            });
            if (!s.Ok) return (false, null, s.ToString());
            api.DeviceToken = s.Json.GetString("token");
            EditorPrefs.SetString(tokenKey + appKey, api.DeviceToken);
            var m = await api.Post("/messages", new Dictionary<string, object>
            {
                ["body"] = text,
                ["clientId"] = OutgoingQueue.NewClientId(),
                ["context"] = new Dictionary<string, object> { ["source"] = "Unity Editor test", ["unityVersion"] = Application.unityVersion, ["project"] = Path.GetFileName(Path.GetDirectoryName(Application.dataPath)) },
            });
            if (!m.Ok) return (false, null, m.ToString());
            return (true, m.Json.GetString("conversationId"), null);
        }
    }
}
