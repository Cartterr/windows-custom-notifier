# NotiPulse (Windows Custom Notifier)

NotiPulse is an advanced, ultra-efficient C# Background Web API that delivers **instant, live, rich Windows 11 Toast Notifications** for your YouTube subscriptions. 

By utilizing Google's PubSubHubbub (WebSub) push notification architecture, NotiPulse completely eliminates the need for CPU-heavy background polling. The exact millisecond your favorite creator uploads a video, NotiPulse catches the webhook and renders a beautiful native Windows notification.

## Features

- **⚡ Live Push Notifications:** Uses Google's WebSub protocol to instantly receive video uploads via HTTP POST webhooks.
- **🖼️ Rich Media Windows 11 Toasts:** Automatically fetches the official high-quality YouTube thumbnail for the video and embeds it inline, alongside a grouped "YouTube" app header and the NotiPulse logo.
- **🖱️ Interactive Launching:** Clicking the notification instantly opens the video in your default web browser (such as Chrome, Edge, or Comet).
- **🔒 Secure Local Storage:** Your Google OAuth `credentials.json` and API tokens are saved completely outside of this repository in `%LocalAppData%\WindowsCustomNotifier` to prevent accidental credential leakage.
- **🔄 Automated Subscription Management:** The app autonomously pulls your hundreds of YouTube channel subscriptions and bulk-subscribes them to the Google WebSub Hub in the background.
- **🐞 Instant Debug Endpoint:** Contains a local `/api/youtube/test` endpoint allowing you to trigger a test notification using any valid YouTube URL to test UI changes.

## Prerequisites & Setup

To receive live Push Notifications from Google, your computer must be reachable from the internet. 

### 1. Google Cloud Configuration
1. Go to the [Google Cloud Console](https://console.cloud.google.com/) and create a new project.
2. Enable the **YouTube Data API v3**.
3. Create an **OAuth Desktop Client ID** and download the `client_secret.json` file.
4. Rename that file exactly to `credentials.json` and place it in the `%LocalAppData%\WindowsCustomNotifier` folder on your PC.

### 2. Set Up a Public URL
You need a public HTTPS URL pointing to port `5000` on your machine.
- **Recommended:** Use a Cloudflare named tunnel (e.g., `https://yt-webhook.yourdomain.com`).
- **Alternative:** Use `ngrok http 5000` or port forward your router to a Dynamic DNS.

Open `appsettings.json` and update the `"PublicWebhookUrl"` with your tunnel address:
```json
{
  "PublicWebhookUrl": "https://yt-webhook.yourdomain.com",
  "Port": 5000
}
```
*(Note: Ensure your Cloudflare/WAF settings do not throw a CAPTCHA or JS challenge on this URL, as Google's WebSub bots cannot solve them.)*

## Running NotiPulse

Run the application using the .NET CLI:
```powershell
dotnet build
dotnet run
```

On your **first run**, a browser window will open asking you to securely authenticate your YouTube account. Once granted, it generates a persistent token locally. From then on, it runs silently in the background!

## Testing the Notification UI

You can instantly trigger a production-grade notification for debugging purposes. While the app is running, run this in a new terminal:
```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/youtube/test?url=https://www.youtube.com/watch?v=J3GQK9CUJWk"
```

## Attribution

This project uses concepts, layouts, and payload structures heavily referenced from the excellent [Windos/BurntToast](https://github.com/Windos/BurntToast) PowerShell module.

### BurntToast License
```text
The MIT License (MIT)
Copyright (c) 2015 Joshua King
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
