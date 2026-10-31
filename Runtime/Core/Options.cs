using System;
using System.Collections.Generic;
using UnityEngine;

namespace InngameSDK
{
    public sealed class InngameOptions
    {
        /// <summary>Inngame server. Default: the hosted service.</summary>
        public string BaseUrl = Inngame.DefaultBaseUrl;
        /// <summary>Player language (BCP-47, e.g. "fr" or "pt-BR"). Null: the system language.</summary>
        public string Language;
        /// <summary>Local theme. Server appearance settings (Settings › Appearance) override its colors and texts.</summary>
        public InngameTheme Theme;
        public InngameLogLevel LogLevel = InngameLogLevel.Warning;

        /// <summary>Sets Time.timeScale to 0 while the chat is open and restores it afterwards.</summary>
        public bool PauseGameWhileOpen;
        /// <summary>Shows and unlocks the cursor while the chat is open, then restores the previous state.</summary>
        public bool UnlockCursorWhileOpen = true;

        /// <summary>Number of recent log lines attached to each message (0 disables log capture).</summary>
        public int LogLines = 50;
        /// <summary>Extra context computed when a message is sent (player level, save slot…). Values must be JSON-friendly.</summary>
        public Func<Dictionary<string, object>> ContextProvider;

        /// <summary>Desktop hotkey that captures a screenshot and opens the chat with it attached. KeyCode.None disables it.</summary>
        public KeyCode ScreenshotHotkey = KeyCode.F8;
        /// <summary>Shaking the phone captures a screenshot and opens the chat.</summary>
        public bool ShakeToReport;
        /// <summary>JPEG quality of screenshots (1-100).</summary>
        public int ScreenshotQuality = 80;
        /// <summary>Screenshots are downscaled so that their longest side is at most this many pixels.</summary>
        public int ScreenshotMaxSize = 1600;

        /// <summary>Gallery picker for mobile (needs native code: implement it with your favourite plugin). Null: screenshot button only.</summary>
        public IImagePicker ImagePicker;
        /// <summary>Where the device token, offline queue and message cache are stored. Default: PlayerPrefs, namespaced per app key.</summary>
        public ITokenStore TokenStore;

        /// <summary>
        /// Text entry hook for platforms without a system keyboard (Steam Deck in Big Picture, consoles).
        /// Called with the current text and a callback that receives the new text; e.g. open SteamUtils.ShowGamepadTextInput.
        /// Null: the regular input field (with TouchScreenKeyboard where Unity supports it).
        /// </summary>
        public Action<string, Action<string>> OnScreenKeyboard;

        /// <summary>Real-time WebSocket (falls back to polling on WebGL or when the socket can't connect).</summary>
        public bool UseWebSocket = true;
        /// <summary>Polling intervals when the socket is not connected (seconds).</summary>
        public float PollIntervalClosed = 15f;
        public float PollIntervalOpen = 4f;

        /// <summary>Store ids used by "review" cards when the card itself doesn't carry them.</summary>
        public string AppStoreId;
        public string PlayStorePackage;
        public string SteamAppId;

        /// <summary>Sorting order of the chat canvas (above the game's own UI).</summary>
        public int SortingOrder = 30000;
        /// <summary>Force a layout: null = automatic (full screen on phones, centered panel elsewhere).</summary>
        public bool? FullScreen;

        public InngameOptions Clone() => (InngameOptions)MemberwiseClone();
    }

    /// <summary>Picks an image from the device gallery. Implement with native code (e.g. NativeGallery) and pass it in InngameOptions.ImagePicker.</summary>
    public interface IImagePicker
    {
        /// <summary>Call <paramref name="done"/> with the encoded image bytes (JPEG/PNG), its MIME type, or null if the player cancelled.</summary>
        void PickImage(Action<byte[], string> done);
    }

    /// <summary>Persistent storage for the device token and caches. Default: <see cref="PlayerPrefsTokenStore"/>.</summary>
    public interface ITokenStore
    {
        string Load(string key);
        void Save(string key, string value);
        void Delete(string key);
    }

    public sealed class PlayerPrefsTokenStore : ITokenStore
    {
        public string Load(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        public void Save(string key, string value) { PlayerPrefs.SetString(key, value ?? ""); PlayerPrefs.Save(); }
        public void Delete(string key) { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
    }

    /// <summary>In-memory store (tests, or games that don't want anything persisted).</summary>
    public sealed class MemoryTokenStore : ITokenStore
    {
        readonly Dictionary<string, string> d = new Dictionary<string, string>();
        public string Load(string key) => d.TryGetValue(key, out var v) ? v : null;
        public void Save(string key, string value) => d[key] = value;
        public void Delete(string key) => d.Remove(key);
    }
}
