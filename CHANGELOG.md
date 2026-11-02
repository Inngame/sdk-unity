# Changelog

## [0.2.2] - 2026-11-02

### Changed
- Default server address is now `https://app.inngame.app` (`Inngame.DefaultBaseUrl`). The previous address keeps working, so 0.2.1 and earlier need no change.

### Fixed
- Cards sent with the server's default English label ("Rate the game", "Request a refund") now show the SDK's translated text in the player's language (25 languages). Labels written by the team are shown as they are. The server flags these cards with `defaultLabel: true`; `InngameCard.UsesDefaultLabel` and `InngameCard.CustomLabel` expose it.
- Refund cards get their own translated label ("Request a refund") instead of "Open".

## [0.2.1] - 2026-11-01

### Fixed
- Suspended workspace (`403 org_suspended` on every SDK call): the chat now shows "Support is unavailable right now. Please try again later." (25 languages) and hides the composer, instead of an empty chat, a stuck "Sending…" or dropped messages. Unsent messages stay queued without using up their retries; the socket and polling stop and the SDK asks again every 10 minutes while the chat is open and when it opens. When a session works again the chat returns to normal and the queue is sent.

### Added
- `Inngame.IsUnavailable` (read-only). `Identify` keeps the identity for the next session while support is unavailable; `Track` stops logging a warning for each event.

## [0.2.0] - 2026-11-01

### Added
- Build number (iOS CFBundleVersion, Android versionCode) and the real time zone ("Europe/Paris") plus UTC offset in the message context; Unity only exposes the marketing version.
- Game skins: `InngameTheme` takes 9-sliced sprites for the panel, header, composer, both bubbles, the text field and buttons (`PanelSprite`, `HeaderSprite`, `ComposerSprite`, `IncomingBubbleSprite`, `OutgoingBubbleSprite`, `InputSprite`, `ButtonSprite`, `TintSprites`, `SpriteScale`, `SkinPadding`); bubble padding follows the sprite borders.
- Logo in the chat header: `InngameTheme.Logo`, or the logo uploaded in the dashboard (`appearance.logoUrl`, downloaded once and cached).
- Team announcement at the top of the chat (outage, maintenance, known bug), translated by the server, set from the inbox.
- Availability line under the chat title: usual reply time, or "Away · back Mon 09:00" outside office hours (25 languages, player's local time); `InngameRemoteConfig.Availability`.
- Satisfaction cards (`rating`): the question in the player's language (25 languages) and five buttons from 1 to 5, gamepad-navigable; `InngameCard.Rating`.

### Security
- Card links only open http(s) URLs and app deep links; `javascript:`, `data:` and `file:` links are ignored (the server refuses them too).

## [0.1.0] - 2026-10-31

### Added
- `Inngame` facade: configure, identity (anonymous, user id, JWT, Steam Web API ticket), metadata, logout, delete data.
- Chat UI built from code with uGUI: full screen on phones, centered panel on desktop/console, safe area, light/dark themes, server appearance overrides, 13 UI languages.
- Message list with day separators, grouping, translations with "Show original", images with full-screen viewer, cards (review, link, screen, item, offer, restore), typing indicator, new-messages pill, offline banner, blocked state, pagination.
- Gamepad/keyboard navigation (legacy Input Manager and Input System), on-screen keyboard hook for Steam Deck/consoles, pause option.
- Automatic context (scene history, device, GPU, screen, quality, FPS, memory, logs) capped at 64 KB, `SetContext` and `ContextProvider`.
- Screenshots (button, F8, shake) uploaded without the chat overlay; `IImagePicker` hook for gallery photos.
- Persistent outgoing queue with `clientId` idempotency, retries with backoff, tap to retry.
- Real time over WebSocket with reconnection and ping, polling fallback; offline cache of the last 50 messages.
- Push token forwarding (APNs/FCM), `Track` events that can open the chat.
- Editor window (Tools › Inngame › Settings) with a "Send test message" button.
- Basic sample.
