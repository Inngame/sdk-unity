using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace InngameSDK
{
    internal sealed class ApiResponse
    {
        public long Status;
        public string Text;
        public Dictionary<string, object> Json;
        public string NetworkError;
        public bool Ok => Status >= 200 && Status < 300;
        /// <summary>Offline, timeout, 5xx or 429: worth retrying with the same request.</summary>
        public bool Transient => Status == 0 || Status == 408 || Status == 429 || Status >= 500;
        /// <summary>Server error code ("blocked", "invalid_device_token"…).</summary>
        public string ErrorCode => Json?.GetString("error") ?? Json?.GetDict("error")?.GetString("code");
        public override string ToString() => Ok ? $"{Status}" : $"{Status} {ErrorCode ?? NetworkError ?? Text}";
    }

    /// <summary>REST client for /v1/sdk (UnityWebRequest, main thread, async/await, retries with backoff).</summary>
    internal sealed class ApiClient
    {
        public string BaseUrl;
        public string AppKey;
        public string DeviceToken;
        public int TimeoutSeconds = 20;
        /// <summary>Delay provider (runtime: unscaled-time coroutine so it also works on WebGL and while paused).</summary>
        public Func<float, Task> Delay = s => Task.Delay(TimeSpan.FromSeconds(s));

        public ApiClient(string baseUrl, string appKey)
        {
            BaseUrl = (baseUrl ?? Inngame.DefaultBaseUrl).TrimEnd('/');
            AppKey = appKey;
        }

        public string Url(string path) => BaseUrl + "/v1/sdk" + path;

        public Task<ApiResponse> Get(string path, int retries = 2) => Send("GET", path, null, retries);
        public Task<ApiResponse> Post(string path, object body, int retries = 2) => Send("POST", path, body, retries);

        public async Task<ApiResponse> Send(string method, string path, object body, int retries = 2)
        {
            string json = body == null ? null : InngameJson.Serialize(body);
            ApiResponse r = null;
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                if (attempt > 0) await Delay(Mathf.Min(8f, 0.5f * Mathf.Pow(2, attempt - 1)));
                using (var req = new UnityWebRequest(Url(path), method))
                {
                    req.downloadHandler = new DownloadHandlerBuffer();
                    if (json != null)
                    {
                        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                        req.SetRequestHeader("Content-Type", "application/json");
                    }
                    r = await Run(req);
                }
                if (r.Ok || !r.Transient) return r;
                Inngame.Log(InngameLogLevel.Verbose, $"{method} {path} failed ({r}), attempt {attempt + 1}/{retries + 1}");
            }
            return r;
        }

        public async Task<ApiResponse> Upload(string path, byte[] bytes, string fileName, string mime, int retries = 2)
        {
            ApiResponse r = null;
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                if (attempt > 0) await Delay(Mathf.Min(8f, 0.5f * Mathf.Pow(2, attempt - 1)));
                var form = new List<IMultipartFormSection> { new MultipartFormFileSection("file", bytes, fileName, mime) };
                using (var req = UnityWebRequest.Post(Url(path), form))
                {
                    req.downloadHandler = new DownloadHandlerBuffer();
                    r = await Run(req);
                }
                if (r.Ok || !r.Transient) return r;
            }
            return r;
        }

        async Task<ApiResponse> Run(UnityWebRequest req)
        {
            req.timeout = TimeoutSeconds;
            req.SetRequestHeader("X-App-Key", AppKey ?? "");
            if (!string.IsNullOrEmpty(DeviceToken)) req.SetRequestHeader("Authorization", "Bearer " + DeviceToken);
            req.SetRequestHeader("X-Inngame-Sdk", "unity/" + Inngame.SdkVersion);
            await SendAsync(req);
            var r = new ApiResponse { Status = req.responseCode, Text = req.downloadHandler?.text };
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
            {
                r.Status = 0;
                r.NetworkError = req.error;
            }
            if (!string.IsNullOrEmpty(r.Text) && r.Text[0] == '{') r.Json = InngameJson.ParseObject(r.Text);
            return r;
        }

        public static Task SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = req.SendWebRequest();
            if (op.isDone) tcs.TrySetResult(true);
            else op.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
