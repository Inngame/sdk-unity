using System;
using System.Collections.Generic;
using UnityEngine;

namespace InngameSDK
{
    /// <summary>Thread-safe ring buffer of the last log lines (fed by Application.logMessageReceivedThreaded).</summary>
    internal sealed class LogBuffer
    {
        internal struct Entry
        {
            public long Time;      // unix ms
            public LogType Type;
            public string Message;
            public string Stack;   // errors/exceptions/asserts only
        }

        readonly object gate = new object();
        readonly Queue<Entry> entries = new Queue<Entry>();
        int capacity;
        public const int MaxMessageChars = 1000;
        public const int MaxStackChars = 2000;

        public LogBuffer(int capacity) { this.capacity = Math.Max(0, capacity); }

        public int Capacity
        {
            get { lock (gate) return capacity; }
            set { lock (gate) { capacity = Math.Max(0, value); while (entries.Count > capacity) entries.Dequeue(); } }
        }

        public void Add(string message, string stack, LogType type)
        {
            if (capacity == 0) return;
            bool withStack = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            var e = new Entry
            {
                Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Type = type,
                Message = Truncate(message, MaxMessageChars),
                Stack = withStack ? Truncate(stack, MaxStackChars) : null,
            };
            lock (gate)
            {
                entries.Enqueue(e);
                while (entries.Count > capacity) entries.Dequeue();
            }
        }

        public List<object> Snapshot()
        {
            Entry[] copy;
            lock (gate) copy = entries.ToArray();
            var list = new List<object>(copy.Length);
            foreach (var e in copy)
            {
                var d = new Dictionary<string, object> { ["t"] = e.Time, ["type"] = TypeName(e.Type), ["msg"] = e.Message };
                if (!string.IsNullOrEmpty(e.Stack)) d["stack"] = e.Stack;
                list.Add(d);
            }
            return list;
        }

        public void Clear() { lock (gate) entries.Clear(); }

        static string TypeName(LogType t) => t switch
        {
            LogType.Error => "error",
            LogType.Assert => "assert",
            LogType.Warning => "warning",
            LogType.Exception => "exception",
            _ => "log",
        };

        static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
