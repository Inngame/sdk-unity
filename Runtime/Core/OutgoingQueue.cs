using System;
using System.Collections.Generic;

namespace InngameSDK
{
    /// <summary>
    /// Outgoing messages waiting for the server. Each message keeps the same <c>clientId</c> across every retry,
    /// so the server (which dedupes on clientId) never stores it twice even if a response was lost.
    /// Pure logic (no Unity calls) so it can be unit-tested and persisted.
    /// </summary>
    internal sealed class OutgoingQueue
    {
        internal sealed class Item
        {
            public string ClientId;
            public string Body;
            public List<object> Attachments = new List<object>(); // server attachment objects {key,type,size,…}
            public Dictionary<string, object> Context;
            public int Attempts;
            public double NextAttemptAt; // seconds, same clock as the "now" passed in
            public bool Failed;          // gave up automatic retries: waits for a tap on "Retry"
            public bool InFlight;
            public long CreatedAt;       // unix ms
        }

        public const int MaxAutoAttempts = 5;
        public const int MaxItems = 30;
        readonly List<Item> items = new List<Item>();
        public IReadOnlyList<Item> Items => items;
        public int Count => items.Count;

        public static string NewClientId() => "c_" + Guid.NewGuid().ToString("N");

        public Item Enqueue(string body, List<object> attachments, Dictionary<string, object> context, long createdAtMs, double now)
        {
            var it = new Item
            {
                ClientId = NewClientId(), Body = body ?? "", Attachments = attachments ?? new List<object>(),
                Context = context, CreatedAt = createdAtMs, NextAttemptAt = now,
            };
            items.Add(it);
            while (items.Count > MaxItems) items.RemoveAt(0);
            return it;
        }

        public Item Find(string clientId) => items.Find(i => i.ClientId == clientId);

        /// <summary>The next message that may be sent now (oldest first, one at a time to keep the order).</summary>
        public Item NextDue(double now)
        {
            foreach (var it in items)
            {
                if (it.InFlight) return null; // keep order: wait for the one in flight
                if (it.Failed) continue;
                return it.NextAttemptAt <= now ? it : null;
            }
            return null;
        }

        public void MarkInFlight(string clientId) { var it = Find(clientId); if (it != null) { it.InFlight = true; it.Attempts++; } }

        public void MarkSent(string clientId) => items.RemoveAll(i => i.ClientId == clientId);

        /// <summary>Transient failure (offline, 5xx): retry later with exponential backoff, then give up until the player taps Retry.</summary>
        public void MarkRetry(string clientId, double now)
        {
            var it = Find(clientId);
            if (it == null) return;
            it.InFlight = false;
            if (it.Attempts >= MaxAutoAttempts) { it.Failed = true; return; }
            it.NextAttemptAt = now + BackoffSeconds(it.Attempts);
        }

        /// <summary>Permanent failure (validation error, blocked): never retried automatically.</summary>
        public void MarkFailed(string clientId)
        {
            var it = Find(clientId);
            if (it == null) return;
            it.InFlight = false;
            it.Failed = true;
        }

        /// <summary>
        /// Not sent because support is unavailable (workspace suspended): the message is fine, so it keeps its place,
        /// doesn't use up an attempt and goes as soon as support is back (<see cref="WakeUp"/>).
        /// </summary>
        public void MarkWaiting(string clientId)
        {
            var it = Find(clientId);
            if (it == null) return;
            if (it.InFlight && it.Attempts > 0) it.Attempts--;
            it.InFlight = false;
        }

        /// <summary>Player tapped "Retry": same clientId, fresh attempt counter.</summary>
        public bool Retry(string clientId, double now)
        {
            var it = Find(clientId);
            if (it == null) return false;
            it.Failed = false;
            it.InFlight = false;
            it.Attempts = 0;
            it.NextAttemptAt = now;
            return true;
        }

        public bool Remove(string clientId) => items.RemoveAll(i => i.ClientId == clientId) > 0;

        /// <summary>Back online: everything waiting on backoff may go now.</summary>
        public void WakeUp(double now)
        {
            foreach (var it in items) if (!it.Failed && !it.InFlight) it.NextAttemptAt = Math.Min(it.NextAttemptAt, now);
        }

        public void Clear() => items.Clear();

        public static double BackoffSeconds(int attempts) => Math.Min(60, 2 * Math.Pow(2, Math.Max(0, attempts - 1)));

        // ---------- persistence ----------

        public string Serialize()
        {
            var list = new List<object>();
            foreach (var it in items)
                list.Add(new Dictionary<string, object>
                {
                    ["clientId"] = it.ClientId, ["body"] = it.Body, ["attachments"] = it.Attachments, ["context"] = it.Context,
                    ["failed"] = it.Failed, ["createdAt"] = it.CreatedAt,
                });
            return InngameJson.Serialize(list);
        }

        public void Load(string json, double now)
        {
            items.Clear();
            if (string.IsNullOrEmpty(json)) return;
            List<object> list;
            try { list = InngameJson.Parse(json) as List<object>; }
            catch (Exception) { return; }
            if (list == null) return;
            foreach (var o in list)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                var id = d.GetString("clientId");
                if (string.IsNullOrEmpty(id)) continue;
                items.Add(new Item
                {
                    ClientId = id, Body = d.GetString("body") ?? "", Attachments = d.GetList("attachments") ?? new List<object>(),
                    Context = d.GetDict("context"), Failed = d.GetBool("failed"), CreatedAt = d.GetLong("createdAt"), NextAttemptAt = now,
                });
            }
        }
    }
}
