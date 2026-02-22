# Development Instructions

This project is a centralized custom notifier for Windows 11. It intercepts YouTube Data API v3 events via live push notifications using the PubSubHubbub / WebSub protocol.

## Architecture: Live WebSub Architecture

To make notifications instant without constantly burning CPU polling, this application is built as an **ASP.NET Core Web API** that listens for Webhook POST requests sent directly from Google.

1. **SubscriptionManagerWorker**: On startup, it grabs your YouTube channel subscriptions and automatically sends `hub.mode=subscribe` requests to the Google WebSub Hub (`pubsubhubbub.appspot.com`).
2. **Webhook Endpoint**: `Program.cs` exposes `GET /api/youtube/webhook` (to verify the subscription challenge) and `POST /api/youtube/webhook` (to receive the live XML payload instantly when a video drops).
3. **Notification Builder**: Parses the XML, downloads the video thumbnail, and fires the rich Windows 11 Toast instantly.

### Public Endpoint Requirement (Static IP / Tunnels)

Because Google's servers must be able to reach your computer to push the webhook, **you must have a public URL**.

**How to set this up:**
1. Open `appsettings.json`.
2. Change `"PublicWebhookUrl"` to a valid public URL that points to your PC on the configured `"Port"` (default 5000).

*Options for Public URLs:*
- **Cloudflare Tunnels (Recommended):** Run `cloudflared tunnel --url http://localhost:5000` to get a free, secure, persistent public URL without touching your router.
- **Ngrok (Great for testing):** Run `ngrok http 5000`.
- **Port Forwarding:** Set up a Static IP on your Windows PC, log into your router, and port forward TCP 5000 to your PC. Then, map a Dynamic DNS (like DuckDNS) to your router's public IP.

## Security and API Keys (CRITICAL)

**NEVER commit API keys or OAuth credentials directly into the repository.**

To ensure credentials remain local:
- The YouTube `credentials.json` and OAuth tokens are strictly saved inside `%LocalAppData%\WindowsCustomNotifier` (a folder completely isolated from this repository).
- If new integrations (like OpenAI) are added in the future, follow this exact same approach:
  - Create the authentication logic to read secrets from outside the repository.
  - Rely on Windows user folders (e.g., `%LocalAppData%\WindowsCustomNotifier`) for persistence.

## Building and Running

You can use the .NET CLI:
```bash
dotnet build
dotnet run
```
