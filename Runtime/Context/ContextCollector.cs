using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;

namespace InngameSDK
{
    /// <summary>Builds the <c>context</c> object attached to every message the player sends (shown to the team, never to the player).</summary>
    internal static class ContextCollector
    {
        public const int MaxBytes = 64000; // server limit: JSON.stringify(context).length <= 64 000

        public static Dictionary<string, object> Collect(InngameRuntime rt)
        {
            var d = new Dictionary<string, object>();
            Safe(() =>
            {
                d["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (rt != null && rt.RecentScenes.Count > 0) d["recentScenes"] = new List<object>(rt.RecentScenes);
            });
            Safe(() =>
            {
                d["appVersion"] = Application.version;
                var build = NativeInfo.Build;
                if (!string.IsNullOrEmpty(build)) d["build"] = build;
                d["bundleId"] = Application.identifier;
                d["unityVersion"] = Application.unityVersion;
                d["platform"] = Application.platform.ToString();
                d["sdkVersion"] = Inngame.SdkVersion;
                d["isEditor"] = Application.isEditor;
                d["installMode"] = Application.installMode.ToString();
            });
            Safe(() =>
            {
                d["device"] = new Dictionary<string, object>
                {
                    ["model"] = SystemInfo.deviceModel,
                    ["type"] = SystemInfo.deviceType.ToString(),
                    ["os"] = SystemInfo.operatingSystem,
                    ["osFamily"] = SystemInfo.operatingSystemFamily.ToString(),
                    ["cpu"] = SystemInfo.processorType,
                    ["cpuCores"] = SystemInfo.processorCount,
                    ["ramMB"] = SystemInfo.systemMemorySize,
                    ["battery"] = SystemInfo.batteryLevel < 0 ? null : (object)Math.Round(SystemInfo.batteryLevel, 2),
                    ["network"] = Application.internetReachability.ToString(),
                };
            });
            Safe(() =>
            {
                d["gpu"] = new Dictionary<string, object>
                {
                    ["name"] = SystemInfo.graphicsDeviceName,
                    ["vendor"] = SystemInfo.graphicsDeviceVendor,
                    ["api"] = SystemInfo.graphicsDeviceType.ToString(),
                    ["version"] = SystemInfo.graphicsDeviceVersion,
                    ["memoryMB"] = SystemInfo.graphicsMemorySize,
                    ["shaderLevel"] = SystemInfo.graphicsShaderLevel,
                };
            });
            Safe(() =>
            {
                var res = Screen.currentResolution;
                d["screen"] = new Dictionary<string, object>
                {
                    ["width"] = Screen.width,
                    ["height"] = Screen.height,
                    ["dpi"] = Math.Round(Screen.dpi, 1),
#if UNITY_2022_2_OR_NEWER
                    ["refreshRate"] = Math.Round(res.refreshRateRatio.value, 1),
#else
                    ["refreshRate"] = res.refreshRate,
#endif
                    ["fullScreenMode"] = Screen.fullScreenMode.ToString(),
                    ["orientation"] = Screen.orientation.ToString(),
                    ["safeArea"] = $"{Screen.safeArea.x:0},{Screen.safeArea.y:0},{Screen.safeArea.width:0},{Screen.safeArea.height:0}",
                };
            });
            Safe(() =>
            {
                int q = QualitySettings.GetQualityLevel();
                var names = QualitySettings.names;
                d["quality"] = q >= 0 && q < names.Length ? names[q] : q.ToString(CultureInfo.InvariantCulture);
                d["targetFrameRate"] = Application.targetFrameRate;
                d["vSync"] = QualitySettings.vSyncCount;
            });
            Safe(() =>
            {
                if (rt != null) d["fps"] = rt.Fps.Snapshot();
                d["memory"] = new Dictionary<string, object>
                {
                    ["allocatedMB"] = Mb(Profiler.GetTotalAllocatedMemoryLong()),
                    ["reservedMB"] = Mb(Profiler.GetTotalReservedMemoryLong()),
                    ["monoUsedMB"] = Mb(Profiler.GetMonoUsedSizeLong()),
                    ["gcTotalMB"] = Mb(GC.GetTotalMemory(false)),
                };
            });
            Safe(() =>
            {
                d["systemLanguage"] = Application.systemLanguage.ToString();
                d["language"] = rt != null ? rt.PlayerLanguage : Inngame.DetectLanguage();
                var zone = NativeInfo.TimeZone;
                if (!string.IsNullOrEmpty(zone)) d["timeZone"] = zone;
                d["utcOffsetMin"] = NativeInfo.UtcOffsetMinutes;
                d["uptimeSec"] = Math.Round(Time.realtimeSinceStartup);
                d["timeScale"] = rt != null && rt.SavedTimeScale.HasValue ? rt.SavedTimeScale.Value : Time.timeScale;
            });
            if (rt != null)
            {
                if (rt.Logs.Capacity > 0) d["logs"] = rt.Logs.Snapshot();
                if (rt.CustomContext.Count > 0) d["custom"] = Sanitize(rt.CustomContext);
                var provider = rt.Options.ContextProvider;
                if (provider != null)
                {
                    try
                    {
                        var extra = provider();
                        if (extra != null) d["game"] = Sanitize(extra);
                    }
                    catch (Exception e) { Inngame.Log(InngameLogLevel.Warning, "ContextProvider threw: " + e.Message); }
                }
            }
            return EnforceCap(d, MaxBytes);
        }

        static double Mb(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);

        static void Safe(Action a)
        {
            try { a(); }
            catch (Exception) { /* some SystemInfo fields throw on exotic platforms: skip them */ }
        }

        /// <summary>Converts arbitrary values (Vector3, enums, custom classes) into JSON-friendly ones.</summary>
        internal static Dictionary<string, object> Sanitize(IDictionary<string, object> src)
        {
            var d = new Dictionary<string, object>();
            foreach (var kv in src) d[kv.Key] = SanitizeValue(kv.Value, 0);
            return d;
        }

        static object SanitizeValue(object v, int depth)
        {
            switch (v)
            {
                case null: return null;
                case string or bool or int or long or double or float or short or byte or uint or ulong or decimal: return v;
                case Enum e: return e.ToString();
                case IDictionary<string, object> dict when depth < 4:
                {
                    var o = new Dictionary<string, object>();
                    foreach (var kv in dict) o[kv.Key] = SanitizeValue(kv.Value, depth + 1);
                    return o;
                }
                case System.Collections.IEnumerable list when depth < 4:
                {
                    var o = new List<object>();
                    foreach (var x in list) { o.Add(SanitizeValue(x, depth + 1)); if (o.Count >= 200) break; }
                    return o;
                }
                default: return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Keeps the serialized context under <paramref name="maxBytes"/> characters (the server rejects bigger ones):
        /// drops the oldest log lines first, then strips stack traces, then shortens long strings, then drops custom sections.
        /// </summary>
        internal static Dictionary<string, object> EnforceCap(Dictionary<string, object> d, int maxBytes)
        {
            int Size() => InngameJson.Serialize(d).Length;
            if (Size() <= maxBytes) return d;

            if (d.GetList("logs") is List<object> logs)
            {
                // Remove oldest lines (fast path: estimate how many to remove, then refine).
                while (logs.Count > 0 && Size() > maxBytes)
                {
                    int over = Size() - maxBytes;
                    int drop = 1;
                    if (logs.Count > 4)
                    {
                        int avg = Math.Max(1, InngameJson.Serialize(logs).Length / logs.Count);
                        drop = Math.Max(1, Math.Min(logs.Count, over / avg));
                    }
                    logs.RemoveRange(0, drop);
                }
                if (Size() <= maxBytes) return d;
            }

            foreach (var key in new[] { "game", "custom" })
            {
                if (d.GetDict(key) is Dictionary<string, object> section)
                {
                    foreach (var k in new List<string>(section.Keys))
                        if (section[k] is string s && s.Length > 500) section[k] = s.Substring(0, 500) + "…";
                    if (Size() <= maxBytes) return d;
                }
            }
            foreach (var key in new[] { "game", "custom", "recentScenes", "logs" })
            {
                if (d.Remove(key))
                {
                    d["truncated"] = true;
                    if (Size() <= maxBytes) return d;
                }
            }
            // Last resort: only keep scalar fields.
            var keep = new Dictionary<string, object> { ["truncated"] = true };
            foreach (var kv in d)
                if (!(kv.Value is System.Collections.IEnumerable) || kv.Value is string)
                {
                    keep[kv.Key] = kv.Value is string s && s.Length > 200 ? s.Substring(0, 200) : kv.Value;
                }
            return keep;
        }

        internal static int SerializedSize(Dictionary<string, object> d) => InngameJson.Serialize(d).Length;

    }

    /// <summary>Average/min FPS over a rolling window (unscaled time).</summary>
    internal sealed class FpsMeter
    {
        readonly float[] samples = new float[120];
        int count, index;
        float acc; int frames;

        public void Tick(float unscaledDt)
        {
            acc += unscaledDt; frames++;
            if (acc >= 0.5f)
            {
                samples[index] = frames / acc;
                index = (index + 1) % samples.Length;
                count = Math.Min(count + 1, samples.Length);
                acc = 0; frames = 0;
            }
        }

        public Dictionary<string, object> Snapshot()
        {
            if (count == 0) return new Dictionary<string, object> { ["avg"] = null };
            float sum = 0, min = float.MaxValue;
            for (int i = 0; i < count; i++) { sum += samples[i]; min = Math.Min(min, samples[i]); }
            return new Dictionary<string, object> { ["avg"] = Math.Round(sum / count, 1), ["min"] = Math.Round(min, 1), ["windowSec"] = count / 2 };
        }
    }
}
