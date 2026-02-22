using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;

namespace NotiFlare;

public class SubscriptionManagerWorker : BackgroundService
{
    private readonly ILogger<SubscriptionManagerWorker> _logger;
    private readonly YouTubeApiService _apiService;
    private readonly IConfiguration _configuration;

    public SubscriptionManagerWorker(ILogger<SubscriptionManagerWorker> logger, YouTubeApiService apiService, IConfiguration configuration)
    {
        _logger = logger;
        _apiService = apiService;
        _configuration = configuration;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Listen for activation
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            ToastArguments args = ToastArguments.Parse(toastArgs.Argument);
            if (args.TryGetValue("action", out string action) && action == "viewVideo")
            {
                if (args.TryGetValue("videoId", out string videoId))
                {
                    var url = $"https://www.youtube.com/watch?v={videoId}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
        };

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var webhookUrl = _configuration.GetValue<string>("PublicWebhookUrl");
                if (string.IsNullOrEmpty(webhookUrl) || webhookUrl.Contains("YOUR-PUBLIC-URL-HERE"))
                {
                    _logger.LogWarning("PublicWebhookUrl is missing or invalid in appsettings.json. PubSubHubbub requires a public endpoint (e.g. ngrok or Cloudflare tunnel). Subscriptions paused.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }

                var isAuthenticated = await _apiService.AuthenticateAsync(stoppingToken);
                if (isAuthenticated)
                {
                    _logger.LogInformation("Refreshing YouTube PubSubHubbub subscriptions...");
                    var channelIds = await _apiService.GetSubscriptionsAsync(stoppingToken);
                    
                    _logger.LogInformation("Found {Count} channel subscriptions. Initiating WebSub requests...", channelIds.Count);
                    
                    using var httpClient = new HttpClient();
                    foreach (var channelId in channelIds)
                    {
                        await SubscribeToChannelAsync(httpClient, channelId, webhookUrl, stoppingToken);
                        // Small delay to prevent rate-limiting when bulk subscribing
                        await Task.Delay(500, stoppingToken);
                    }
                    _logger.LogInformation("Finished renewing WebSub subscriptions. They will last for a few days.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during subscription renewal loop.");
            }

            // Google's WebSub subscriptions typically expire after 5 days.
            // Renewing them every 24 hours is a safe best practice.
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task SubscribeToChannelAsync(HttpClient httpClient, string channelId, string webhookUrl, CancellationToken stoppingToken)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hub.callback", $"{webhookUrl.TrimEnd('/')}/api/youtube/webhook"),
                new KeyValuePair<string, string>("hub.topic", $"https://www.youtube.com/xml/feeds/videos.xml?channel_id={channelId}"),
                new KeyValuePair<string, string>("hub.verify", "async"),
                new KeyValuePair<string, string>("hub.mode", "subscribe")
            });

            var response = await httpClient.PostAsync("https://pubsubhubbub.appspot.com/subscribe", content, stoppingToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to subscribe to channel {ChannelId}: {StatusCode}", channelId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception subscribing to channel {ChannelId}", channelId);
        }
    }
}
