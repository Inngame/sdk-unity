using System;
using System.Collections.Generic;
using UnityEngine;

namespace InngameSDK
{
    /// <summary>
    /// Sprites generated at runtime (anti-aliased signed-distance rasterization), so the package needs no image assets
    /// and works in any render pipeline: a 9-sliced rounded rectangle, a circle and a few line icons.
    /// </summary>
    internal static class UiSprites
    {
        public const int RoundedRadiusPx = 32;
        static Sprite rounded, circle, ring;
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        /// <summary>9-sliced rounded rectangle. Use Image.pixelsPerUnitMultiplier = RoundedRadiusPx / radius.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                int r = RoundedRadiusPx, size = r * 2 + 2;
                var tex = NewTex(size, "Inngame Rounded");
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedBox(x + 0.5f - size / 2f, y + 0.5f - size / 2f, size / 2f, size / 2f, r);
                    px[y * size + x] = new Color32(255, 255, 255, Cov(d));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                rounded.name = "Inngame Rounded";
                return rounded;
            }
        }

        static Sprite outline;
        /// <summary>9-sliced rounded outline (3 px stroke at radius 32) for the gamepad focus frame.</summary>
        public static Sprite RoundedOutline
        {
            get
            {
                if (outline != null) return outline;
                int r = RoundedRadiusPx, size = r * 2 + 2;
                var tex = NewTex(size, "Inngame Outline");
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedBox(x + 0.5f - size / 2f, y + 0.5f - size / 2f, size / 2f - 0.5f, size / 2f - 0.5f, r);
                    px[y * size + x] = new Color32(255, 255, 255, Cov(Mathf.Abs(d + 2f) - 2f));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                outline = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return outline;
            }
        }

        public static Sprite Circle => circle != null ? circle : (circle = Shape("Inngame Circle", 128, (x, y) => Mathf.Sqrt(x * x + y * y) - 63f));
        public static Sprite Ring => ring != null ? ring : (ring = Shape("Inngame Ring", 128, (x, y) => Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 60f) - 3f));

        public static Sprite Icon(string name)
        {
            if (icons.TryGetValue(name, out var s) && s != null) return s;
            Func<float, float, float> f;
            const float t = 2.6f; // half stroke width (64 px canvas)
            switch (name)
            {
                case "close": f = (x, y) => Mathf.Min(Seg(x, y, -13, -13, 13, 13), Seg(x, y, -13, 13, 13, -13)) - t; break;
                case "send": f = (x, y) => Mathf.Min(Seg(x, y, 0, -16, 0, 16), Mathf.Min(Seg(x, y, 0, 17, -13, 4), Seg(x, y, 0, 17, 13, 4))) - t * 1.15f; break;
                case "camera":
                    f = (x, y) => Mathf.Min(
                        Mathf.Abs(RoundedBox(x, y + 2, 22, 15, 6)) - t,
                        Mathf.Min(Mathf.Abs(Mathf.Sqrt(x * x + (y + 2) * (y + 2)) - 8f) - t, Mathf.Abs(RoundedBox(x, y - 16, 8, 3.5f, 2)) - t));
                    break;
                case "image":
                    f = (x, y) => Mathf.Min(Mathf.Abs(RoundedBox(x, y, 22, 18, 5)) - t,
                        Mathf.Min(Mathf.Min(Seg(x, y, -16, -10, -5, 2), Seg(x, y, -5, 2, 3, -6)), Mathf.Min(Seg(x, y, 3, -6, 8, -1), Seg(x, y, 8, -1, 16, -9))) - t * 0.9f);
                    break;
                case "chevron": f = (x, y) => Mathf.Min(Seg(x, y, -12, 6, 0, -6), Seg(x, y, 0, -6, 12, 6)) - t; break;
                case "check": f = (x, y) => Mathf.Min(Seg(x, y, -12, 0, -4, -8), Seg(x, y, -4, -8, 13, 9)) - t; break;
                case "alert": f = (x, y) => Mathf.Min(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 22f) - t, Mathf.Min(Seg(x, y, 0, -2, 0, 11), Mathf.Sqrt(x * x + (y + 10) * (y + 10)) - 0.5f) - t); break;
                case "plus": f = (x, y) => Mathf.Min(Seg(x, y, 0, -14, 0, 14), Seg(x, y, -14, 0, 14, 0)) - t; break;
                case "chat": f = (x, y) => Mathf.Min(Mathf.Abs(RoundedBox(x, y + 3, 21, 15, 8)) - t, Mathf.Min(Seg(x, y, -10, -18, -12, -27), Seg(x, y, -12, -27, 0, -18)) - t); break;
                case "dot": f = (x, y) => Mathf.Sqrt(x * x + y * y) - 20f; break;
                default: f = (x, y) => 1f; break;
            }
            s = Shape("Inngame Icon " + name, 64, f);
            icons[name] = s;
            return s;
        }

        static Sprite Shape(string name, int size, Func<float, float, float> sdf)
        {
            var tex = NewTex(size, name);
            var px = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color32(255, 255, 255, Cov(sdf(x + 0.5f - half, y + 0.5f - half)));
            tex.SetPixels32(px); tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            return s;
        }

        static Texture2D NewTex(int size, string name) => new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
        };

        static byte Cov(float d) => (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f);

        static float RoundedBox(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - hw + r, qy = Mathf.Abs(y) - hh + r;
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
            float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            float dx = pax - bax * h, dy = pay - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
