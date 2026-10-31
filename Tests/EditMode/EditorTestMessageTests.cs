using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace InngameSDK.Tests
{
    /// <summary>The Settings window's "Send test message" button, against a real server (INNGAME_E2E_* env vars).</summary>
    public class EditorTestMessageTests
    {
        [UnityTest]
        public IEnumerator SendTestMessageReachesServer()
        {
            var url = Environment.GetEnvironmentVariable("INNGAME_E2E_BASE_URL");
            var key = Environment.GetEnvironmentVariable("INNGAME_E2E_APP_KEY");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key)) Assert.Ignore("E2E env vars not set");
            var t = InngameSDK.Editor.InngameSettingsWindow.SendTestMessage(key, url, "Editor test message");
            var end = DateTime.UtcNow.AddSeconds(30);
            while (!t.IsCompleted && DateTime.UtcNow < end) yield return null;
            Assert.IsTrue(t.IsCompleted, "timeout");
            Assert.IsTrue(t.Result.ok, t.Result.error);
            StringAssert.StartsWith("cnv_", t.Result.conversationId);
        }
    }
}
