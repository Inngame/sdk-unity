using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace InngameSDK
{
    /// <summary>
    /// "refund" cards on iOS: Apple's own refund sheet (StoreKit <c>Transaction.beginRefundRequest</c>, Plugins/iOS/InngameRefund.swift).
    /// Elsewhere, or when the sheet can't be shown (iOS 14 and older), the SDK opens Apple's "Report a Problem" page instead.
    /// </summary>
    internal static class NativeRefund
    {
        public const string ReportProblemUrl = "https://reportaproblem.apple.com/";

        delegate void Done(int status);

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int InngameBeginRefund(string transactionId, Done done);
#endif

        static string pendingMessageId;

        /// <summary>True if Apple's sheet is on its way; the result is reported to the team when the player closes it.</summary>
        public static bool Begin(string transactionId, string messageId)
        {
            if (string.IsNullOrEmpty(transactionId) || !ulong.TryParse(transactionId, out _)) return false;
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                pendingMessageId = messageId;
                if (InngameBeginRefund(transactionId, OnDone) == 1) return true;
            }
            catch (Exception e) { Debug.LogException(e); }
            pendingMessageId = null;
#endif
            return false;
        }

        /// <summary>1 submitted, 2 cancelled by the player, 0 error.</summary>
        [AOT.MonoPInvokeCallback(typeof(Done))]
        static void OnDone(int status)
        {
            var id = pendingMessageId;
            pendingMessageId = null;
            Inngame.Runtime?.ReportCardAction(id, ActionFor(status));
        }

        internal static string ActionFor(int status) => status == 1 ? "claimed" : status == 2 ? "dismissed" : "failed";
    }
}
