using System;
using UnityEngine;
using UnityEngine.EventSystems;
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace InngameSDK
{
    /// <summary>Works with the legacy Input Manager, the Input System package, or both.</summary>
    internal static class InputCompat
    {
        public static bool GetKeyDown(KeyCode key)
        {
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                var k = Map(key);
                if (k != Key.None && kb[k].wasPressedThisFrame) return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (Input.GetKeyDown(key)) return true; } catch (InvalidOperationException) { }
#endif
            return false;
        }

        /// <summary>Escape, gamepad B / Circle, Android back.</summary>
        public static bool BackPressed()
        {
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) return true;
                if (Input.GetKeyDown(KeyCode.JoystickButton1)) return true;
            }
            catch (InvalidOperationException) { }
#endif
            return false;
        }

        /// <summary>Gamepad "Y / Triangle": used as a shortcut to focus the message field.</summary>
        public static bool FocusComposerPressed()
        {
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (Input.GetKeyDown(KeyCode.JoystickButton3)) return true; } catch (InvalidOperationException) { }
#endif
            return false;
        }

        public static bool GamepadActive()
        {
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                for (var k = KeyCode.JoystickButton0; k <= KeyCode.JoystickButton19; k++) if (Input.GetKeyDown(k)) return true;
            }
            catch (InvalidOperationException) { }
#endif
            return false;
        }

        public static Vector3 Acceleration()
        {
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (Accelerometer.current != null)
            {
                if (!Accelerometer.current.enabled) InputSystem.EnableDevice(Accelerometer.current);
                return Accelerometer.current.acceleration.ReadValue();
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { return Input.acceleration; } catch (InvalidOperationException) { }
#endif
            return Vector3.zero;
        }

        /// <summary>Creates an EventSystem with the right input module if the scene has none. Returns it if created.</summary>
        public static GameObject EnsureEventSystem()
        {
            if (EventSystem.current != null) return null;
            var existing = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (existing != null) return null;
            var go = new GameObject("Inngame EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = go.AddComponent<InputSystemUIInputModule>();
            m.AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            return go;
        }

#if INNGAME_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static Key Map(KeyCode k)
        {
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return Key.F1 + (k - KeyCode.F1);
            if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return k == KeyCode.Alpha0 ? Key.Digit0 : Key.Digit1 + (k - KeyCode.Alpha1);
            switch (k)
            {
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Print: return Key.PrintScreen;
                case KeyCode.Home: return Key.Home;
                case KeyCode.End: return Key.End;
                case KeyCode.Insert: return Key.Insert;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                default: return Key.None;
            }
        }
#endif
    }

    internal static class ImageUtil
    {
        /// <summary>Downscale so the longest side ≤ maxSize (GPU blit; returns the source if it fails or is small enough).</summary>
        public static Texture2D Downscale(Texture2D src, int maxSize)
        {
            if (src == null || maxSize <= 0) return src;
            int w = src.width, h = src.height;
            int longest = Mathf.Max(w, h);
            if (longest <= maxSize) return src;
            float k = (float)maxSize / longest;
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
            try
            {
                var rt = RenderTexture.GetTemporary(nw, nh, 0);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var dst = new Texture2D(nw, nh, TextureFormat.RGB24, false);
                dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
                dst.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(src);
                return dst;
            }
            catch (Exception) { return src; }
        }
    }
}
