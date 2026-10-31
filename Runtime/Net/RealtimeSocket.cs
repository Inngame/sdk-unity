using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InngameSDK
{
    /// <summary>
    /// WebSocket to /v1/sdk/ws using System.Net.WebSockets.ClientWebSocket (Standalone, iOS, Android, IL2CPP; not WebGL).
    /// Runs on the thread pool; every event is queued and drained on the main thread by <see cref="InngameRuntime"/>.
    /// Reconnects with exponential backoff and pings every 25 s.
    /// </summary>
    internal sealed class RealtimeSocket
    {
        public const string OpenEvent = "__open";
        public const string ClosedEvent = "__closed";
        public static float PingInterval = 25f;

        public static bool Supported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }

        public readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();
        public volatile bool Connected;
        public volatile int ConsecutiveFailures;

        readonly Func<Uri> urlProvider;
        CancellationTokenSource cts;
        ClientWebSocket socket;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        public RealtimeSocket(Func<Uri> urlProvider) { this.urlProvider = urlProvider; }

        public bool Running => cts != null && !cts.IsCancellationRequested;

        public void Start()
        {
            if (!Supported || Running) return;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            Task.Run(() => Loop(token));
        }

        public void Stop()
        {
            var c = cts;
            cts = null;
            if (c == null) return;
            try { c.Cancel(); } catch (ObjectDisposedException) { }
            var s = socket;
            if (s != null) { try { s.Abort(); } catch (Exception) { } }
            Connected = false;
        }

        /// <summary>Drop the current connection and reconnect now (e.g. after the device switched to another player).</summary>
        public void Restart() { Stop(); Start(); }

        public void Send(string json)
        {
            var s = socket;
            if (!Connected || s == null) return;
            var token = cts?.Token ?? CancellationToken.None;
            Task.Run(async () =>
            {
                try
                {
                    await sendLock.WaitAsync(token).ConfigureAwait(false);
                    try { await s.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, token).ConfigureAwait(false); }
                    finally { sendLock.Release(); }
                }
                catch (Exception) { /* the receive loop will notice and reconnect */ }
            });
        }

        async Task Loop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool opened = false;
                var ws = new ClientWebSocket();
                socket = ws;
                try
                {
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        connectCts.CancelAfter(TimeSpan.FromSeconds(15));
                        await ws.ConnectAsync(urlProvider(), connectCts.Token).ConfigureAwait(false);
                    }
                    opened = true;
                    Connected = true;
                    ConsecutiveFailures = 0;
                    Inbox.Enqueue(OpenEvent);
                    var pinger = PingLoop(ws, token);
                    await ReceiveLoop(ws, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (!token.IsCancellationRequested) Inngame.LogThreaded(InngameLogLevel.Verbose, "WebSocket: " + e.Message);
                }
                finally
                {
                    Connected = false;
                    try { ws.Dispose(); } catch (Exception) { }
                    if (ReferenceEquals(socket, ws)) socket = null;
                    if (opened) Inbox.Enqueue(ClosedEvent);
                }
                if (token.IsCancellationRequested) break;
                ConsecutiveFailures++;
                double wait = Math.Min(30, Math.Pow(2, Math.Min(ConsecutiveFailures, 5)));
                try { await Task.Delay(TimeSpan.FromSeconds(wait), token).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }

        async Task PingLoop(ClientWebSocket ws, CancellationToken token)
        {
            var ping = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
            try
            {
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PingInterval), token).ConfigureAwait(false);
                    if (ws.State != WebSocketState.Open) break;
                    await sendLock.WaitAsync(token).ConfigureAwait(false);
                    try { await ws.SendAsync(new ArraySegment<byte>(ping), WebSocketMessageType.Text, true, token).ConfigureAwait(false); }
                    finally { sendLock.Release(); }
                }
            }
            catch (Exception) { }
        }

        async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            using (var ms = new MemoryStream())
            {
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, res.Count);
                    if (!res.EndOfMessage) continue;
                    if (res.MessageType == WebSocketMessageType.Text)
                        Inbox.Enqueue(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                    ms.SetLength(0);
                    if (ms.Capacity > 1 << 20) ms.Capacity = 64 * 1024;
                }
            }
        }
    }
}
