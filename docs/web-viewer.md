# Remote Web Viewer — implementation contract

## Goal
Deliver a LAN/VPN-only, read-only browser replica of the native WPF main screen. The Windows app remains the sole audio capture, recognition and control host.

## Visual fidelity
- Match MainWindow.xaml's heading, LIVE TRANSCRIPT bar, 225px channels sidebar, selected-channel styling, feed heading, message accent stripe, source name, timestamp, text sizing and Listening… indicator.
- Support desktop and responsive narrow/mobile screens, plus light/dark themes.
- Render all configured channels in the sidebar, with All Communications selected initially.
- Clicking a channel filters only that browser's feed; it must not change desktop selection or another browser's selection.
- Channel colors and renames propagate live. The selected channel should survive reconnects when it still exists.
- Do not render desktop Start, Stop, Mute or Settings actions in the browser.

## Data model and synchronization
- Stable unique message IDs and monotonically increasing event sequence numbers.
- Initial authenticated snapshot of the current session, followed by ordered WebSocket events for message-created, message-updated, message-removed, channel-list-changed, color-changed and session reset.
- A partial transcript updates its existing bubble in place; finalization updates that same ID.
- Empty partials, capture stop and retention trimming remove stale draft bubbles on all clients.
- A reconnect must re-snapshot atomically or replay from a sequence cursor, never introduce duplicates.
- Preserve message ordering and display timestamp semantics; do not expose audio samples or model credentials.
- Browser clients render transcript text as text, never HTML. Apply the same censorship policy as desktop; verify that the desktop's CensoredText attached property does not expose uncensored words via the API.

## Integration
- Extract transcript state into a UI-independent service. Keep WPF ObservableCollection/dispatcher integration intact.
- Add an opt-in embedded ASP.NET Core/Kestrel HTTP + WebSocket host with static assets shipped in the Windows self-contained artifact.
- Default listener: 127.0.0.1:8765; LAN binding requires explicit opt-in. Restrict access to LAN/VPN via firewall configuration and require a per-install token for remote access.
- Authenticated GET /api/snapshot and WS /ws; no mutation endpoints. Prefer secure handling of credentials that avoids leaking tokens through URLs or logs.
- Server start/stop and app shutdown must not block capture, UI or recognition.
- Settings: enabled, bind address, port, token regeneration and viewer URL. Validate port and report bind errors without crashing transcription.

## Acceptance tests
1. Desktop All Communications and browser All Communications show identical finalized and partial message content and ordering.
2. Browser A filters Camera 2; browser B filters FOH; desktop remains on All Communications.
3. Rename/recolor channel and verify all viewers update.
4. Partial message changes 10 times but remains one bubble, then finalizes in place.
5. Stop capture during a partial; no stale Listening… bubble remains.
6. Disconnect/reconnect browser; no missing or duplicated messages.
7. Unauthorized requests cannot fetch snapshot or connect to WS.
8. Browser cannot alter capture, settings, mute or transcript state.
9. Build self-contained Windows x64 artifact; test packaged static files and Windows Firewall LAN access.
10. ASIO/WASAPI and offline recognition behavior remain unchanged.
