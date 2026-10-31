using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace InngameSDK
{
    /// <summary>Build number and time zone name, which Unity doesn't expose (Application.version is only the marketing version).</summary>
    internal static class NativeInfo
    {
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string InngameBuildNumber();
        [DllImport("__Internal")] static extern string InngameTimeZone();
#endif
        static string build, zone;
        static bool read;

        /// <summary>iOS CFBundleVersion, Android versionCode; null elsewhere.</summary>
        public static string Build { get { Read(); return build; } }

        /// <summary>IANA name such as "Europe/Paris" when the platform gives one; otherwise the .NET id.</summary>
        public static string TimeZone { get { Read(); return zone; } }

        /// <summary>Minutes ahead of UTC, right now (daylight saving included).</summary>
        public static int UtcOffsetMinutes => (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes);

        static void Read()
        {
            if (read) return;
            read = true;
            try
            {
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
                build = InngameBuildNumber();
                zone = InngameTimeZone();
#elif UNITY_ANDROID && !UNITY_EDITOR
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0))
                    build = info.Get<int>("versionCode").ToString(System.Globalization.CultureInfo.InvariantCulture);
                using (var tz = new AndroidJavaClass("java.util.TimeZone"))
                using (var def = tz.CallStatic<AndroidJavaObject>("getDefault"))
                    zone = def.Call<string>("getID");
#endif
            }
            catch (Exception e) { Inngame.Log(InngameLogLevel.Verbose, "NativeInfo: " + e.Message); }
            if (string.IsNullOrEmpty(zone))
            {
                try { zone = TimeZoneInfo.Local.Id; } catch (Exception) { }
                if (zone == "Local") zone = null; // Mono on iOS/Android without tzdata
            }
        }
    }
}
