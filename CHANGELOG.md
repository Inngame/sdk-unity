# Changelog

## [Unreleased]

### Added
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
