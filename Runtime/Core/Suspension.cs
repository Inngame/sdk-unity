namespace InngameSDK
{
    /// <summary>
    /// The studio's Inngame workspace is suspended: every SDK call answers 403 <c>org_suspended</c>.
    /// While it lasts the chat says support is unavailable, nothing is sent, and the server is only asked again
    /// every <see cref="RecheckSeconds"/> while the chat is open, or when the chat opens (at most once a minute).
    /// Pure logic (no Unity calls) so it can be unit-tested.
    /// </summary>
    internal sealed class Suspension
    {
        public const double RecheckSeconds = 600;
        public const double OpenRecheckGapSeconds = 60;
        public const string ErrorCode = "org_suspended";

        public bool Unavailable { get; private set; }
        public double LastCheckAt { get; private set; } = double.NegativeInfinity;

        public static bool IsSuspended(ApiResponse r) => r != null && r.Status == 403 && r.ErrorCode == ErrorCode;

        /// <summary>The answer a call gets without touching the network while support is unavailable.</summary>
        public static ApiResponse Refusal() => new ApiResponse
        {
            Status = 403, Text = "{\"error\":\"org_suspended\"}",
            Json = new System.Collections.Generic.Dictionary<string, object> { ["error"] = ErrorCode },
        };

        /// <summary>Notes a server answer; returns true when the state changed.</summary>
        public bool Observe(ApiResponse r, double now)
        {
            if (IsSuspended(r))
            {
                LastCheckAt = now;
                if (Unavailable) return false;
                Unavailable = true;
                return true;
            }
            if (r != null && r.Ok && Unavailable) { Unavailable = false; return true; }
            return false;
        }

        /// <summary>Slow recheck while the chat is open.</summary>
        public bool RecheckDue(double now, bool chatOpen) => Unavailable && chatOpen && now - LastCheckAt >= RecheckSeconds;

        /// <summary>The chat was just opened: ask again unless we asked a moment ago.</summary>
        public bool RecheckOnOpen(double now) => Unavailable && now - LastCheckAt >= OpenRecheckGapSeconds;

        public void MarkChecking(double now) => LastCheckAt = now;

        public void Reset() { Unavailable = false; LastCheckAt = double.NegativeInfinity; }
    }
}
