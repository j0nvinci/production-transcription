# Embedded remote web viewer (initial implementation)

The desktop app is the capture and recognition host. The embedded ASP.NET Core listener is **off by default**.

## Settings (left column)

- Enable web UI
- Listening network interface: localhost (127.0.0.1), all IPv4 interfaces (0.0.0.0), or a detected local IPv4 address
- TCP port: default 8765
- Read only (default), or read/write (allows start/stop capture only)
- Per-install randomly generated access token; regeneration invalidates existing clients

Navigate to `http://<host-ip>:8765/` (substitute the configured port and interface), enter the token from desktop Settings, and click Connect. The browser can select its own channel independently of the desktop. Messages, draft updates, and status are refreshed every 750 ms. This first implementation uses snapshot polling, **not WebSockets**.

## Security and operational notes

- All transcript and capture endpoints require an Authorization: Bearer token header.
- Transcript text is rendered using textContent (not HTML), and the desktop censor is applied before transmission.
- The access token is kept in browser memory only; refreshing the page requires entering it again.
- The listener is **HTTP, not HTTPS**. The token and transcript can be observed on an untrusted network. Use only on a trusted LAN or trusted VPN; never port-forward this listener to the public Internet. A TLS reverse proxy or native HTTPS should be added before using it on untrusted networks.
- Selecting All interfaces does not configure Windows Firewall. You may need to allow inbound TCP on the selected port for the relevant private network.
- Read/write mode authorizes only starting and stopping capture, not changing audio settings, sources, or credentials.
- The app does not require administrator rights for the listener under normal Kestrel operation.

## Verification still required

Build on Windows with .NET 10, test installer packaging, verify interface binding and firewall access, verify browser channel selection and censored partial/final updates, verify that read-only users cannot POST capture actions, and test startup/shutdown while audio capture is running.
