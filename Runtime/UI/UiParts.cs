using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace InngameSDK
{
    /// <summary>Caps the preferred width of a layout group (chat bubbles wrap at ~78 % of the list width).</summary>
    [AddComponentMenu("")]
    internal sealed class ClampedWidth : UIBehaviour, ILayoutElement
    {
        public float MaxWidth = 300;
        LayoutGroup group;
        float pref;

        public void CalculateLayoutInputHorizontal()
        {
            if (group == null) group = GetComponent<LayoutGroup>();
            if (group != null) { group.CalculateLayoutInputHorizontal(); pref = Mathf.Min(MaxWidth, group.preferredWidth); }
        }
        public void CalculateLayoutInputVertical() { }
        public float minWidth => -1;
        public float preferredWidth => pref;
        public float flexibleWidth => 0; // a bubble never stretches, even if a child (the footer) is flexible
        public float minHeight => -1;
        public float preferredHeight => -1;
        public float flexibleHeight => -1;
        public int layoutPriority => 10;
    }

    /// <summary>Select/deselect callbacks + a focus outline shown only while navigating with a gamepad or keyboard.</summary>
    [AddComponentMenu("")]
    internal sealed class UiFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        public GameObject Frame;
        public Action Selected;
        public Action Deselected;
        public static bool NavigationMode;
        bool selected;

        public void OnSelect(BaseEventData e) { selected = true; Refresh(); Selected?.Invoke(); }
        public void OnDeselect(BaseEventData e) { selected = false; Refresh(); Deselected?.Invoke(); }
        void Update() { if (Frame != null && Frame.activeSelf != (selected && NavigationMode)) Refresh(); }
        void Refresh() { if (Frame != null) Frame.SetActive(selected && NavigationMode); }
    }

    /// <summary>Downloads images from signed URLs once per session.</summary>
    internal static class ImageCache
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, List<Action<Texture2D>>> pending = new Dictionary<string, List<Action<Texture2D>>>();

        static string Key(string url)
        {
            // Signed URLs change every hour (e/s parameters): cache by path.
            int q = url.IndexOf('?');
            return q >= 0 ? url.Substring(0, q) : url;
        }

        public static void Get(MonoBehaviour host, string url, Action<Texture2D> done)
        {
            if (string.IsNullOrEmpty(url)) { done(null); return; }
            var k = Key(url);
            if (cache.TryGetValue(k, out var t) && t != null) { done(t); return; }
            if (pending.TryGetValue(k, out var list)) { list.Add(done); return; }
            pending[k] = new List<Action<Texture2D>> { done };
            host.StartCoroutine(Load(url, k));
        }

        static IEnumerator Load(string url, string k)
        {
            Texture2D tex = null;
            using (var req = UnityWebRequestTexture.GetTexture(url, true))
            {
                req.timeout = 30;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) tex = DownloadHandlerTexture.GetContent(req);
            }
            if (tex != null) cache[k] = tex;
            var cbs = pending[k];
            pending.Remove(k);
            foreach (var cb in cbs) { try { cb(tex); } catch (Exception e) { Debug.LogException(e); } }
        }
    }
}
