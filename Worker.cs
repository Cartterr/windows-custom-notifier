using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Diagnostics;
using Google.Apis.YouTube.v3.Data;

namespace YoutubeNotifier;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly YouTubeApiService _apiService;
    private readonly string _stateFilePath;
    private HashSet<string> _notifiedVideoIds = new();

    public Worker(ILogger<Worker> logger, YouTubeApiService apiService)
    {
        _logger = logger;
        _apiService = apiService;
        
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataFolder, "YoutubeNotifier");
        Directory.CreateDirectory(appFolder);
        _stateFilePath = Path.Combine(appFolder, "state.json");
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        LoadState();
        
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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            
            var isAuthenticated = await _apiService.AuthenticateAsync(stoppingToken);
            if (isAuthenticated)
            {
                // Check recent activities (last 2 hours for safety margin)
                var recentActivities = await _apiService.GetRecentActivitiesAsync(new List<string>(), DateTime.UtcNow.AddHours(-2), stoppingToken);
                
                foreach (var activity in recentActivities)
                {
                    var videoId = activity.ContentDetails?.Upload?.VideoId;
                    if (string.IsNullOrEmpty(videoId) || _notifiedVideoIds.Contains(videoId))
                    {
                        continue; // Skip seen or invalid videos
                    }

                    await ShowNotificationAsync(activity, videoId);
                    
                    _notifiedVideoIds.Add(videoId);
                    SaveState();
                }
            }

            // Wait 15 minutes before checking again
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private void LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<HashSet<string>>(json);
                if (state != null)
                {
                    _notifiedVideoIds = state;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load state");
            }
        }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_notifiedVideoIds);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state");
        }
    }

    private async Task ShowNotificationAsync(Google.Apis.YouTube.v3.Data.Activity activity, string videoId)
    {
        var channelTitle = activity.Snippet.ChannelTitle;
        var videoTitle = activity.Snippet.Title;
        var publishedAt = activity.Snippet.PublishedAtDateTimeOffset?.LocalDateTime.ToString("g") ?? "Recently";
        var thumbnailUrl = activity.Snippet.Thumbnails?.High?.Url ?? activity.Snippet.Thumbnails?.Default__?.Url;

        string? localThumbnailPath = null;
        if (!string.IsNullOrEmpty(thumbnailUrl))
        {
            try
            {
                using var client = new HttpClient();
                var imageBytes = await client.GetByteArrayAsync(thumbnailUrl);
                
                var tempPath = Path.Combine(Path.GetTempPath(), $"yt_thumb_{videoId}.jpg");
                await File.WriteAllBytesAsync(tempPath, imageBytes);
                localThumbnailPath = tempPath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download thumbnail for notification");
            }
        }

        var builder = new ToastContentBuilder()
            .AddArgument("action", "viewVideo")
            .AddArgument("videoId", videoId)
            .AddText($"{channelTitle} uploaded a new video!")
            .AddText(videoTitle)
            .AddText($"Uploaded: {publishedAt}");

        if (!string.IsNullOrEmpty(localThumbnailPath))
        {
            builder.AddHeroImage(new Uri(localThumbnailPath));
            // We reference BurntToast's usage of AppLogoOverride for the channel picture feeling
            builder.AddAppLogoOverride(new Uri(localThumbnailPath), ToastGenericAppLogoCrop.Circle);
        }
        
        builder.Show();
    }
}
