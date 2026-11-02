// Inngame SDK — Apple's own refund sheet (StoreKit 2, Swift only) for "refund" cards.
// Called from C# through DllImport("__Internal"). Apple decides on the refund, not the game.
import Foundation
import StoreKit
import UIKit

public typealias InngameRefundDone = @convention(c) (Int32) -> Void

/// Starts the refund sheet for an App Store transaction id. Returns 1 if the sheet is on its way (the result comes
/// through `done`: 1 submitted, 2 cancelled, 0 error), 0 if it can't be shown here (iOS < 15, bad id, no active scene).
@_cdecl("InngameBeginRefund")
public func InngameBeginRefund(_ transactionId: UnsafePointer<CChar>?, _ done: InngameRefundDone?) -> Int32 {
    guard #available(iOS 15.0, *), let transactionId, let id = UInt64(String(cString: transactionId)) else { return 0 }
    // Unity calls plugins on its main thread, which is the UI thread on iOS.
    return MainActor.assumeIsolated {
        guard let scene = UIApplication.shared.connectedScenes.first(where: { $0.activationState == .foregroundActive }) as? UIWindowScene else { return 0 }
        Task { @MainActor in
            do {
                let status = try await Transaction.beginRefundRequest(for: id, in: scene)
                done?(status == .success ? 1 : 2)
            } catch {
                done?(0)
            }
        }
        return 1
    }
}
