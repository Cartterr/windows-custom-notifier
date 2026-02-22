using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Toolkit.Uwp.Notifications;

namespace NotiFlare;

public class YouTubeApiService
{
    private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeReadonly };
    private YouTubeService? _youtubeService;
    private readonly ILogger<YouTubeApiService> _logger;
    private readonly string _credentialsPath;
    private readonly string _tokenPath;
    private readonly string _stateFilePath;
    private HashSet<string> _notifiedVideoIds = new();

    public YouTubeApiService(ILogger<YouTubeApiService> logger)
    {
        _logger = logger;
        
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataFolder, "WindowsCustomNotifier");
        Directory.CreateDirectory(appFolder);
        
        _credentialsPath = Path.Combine(appFolder, "credentials.json");
        _tokenPath = Path.Combine(appFolder, "token");
        _stateFilePath = Path.Combine(appFolder, "state.json");
        
        LoadState();
    }

    private void LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<HashSet<string>>(json);
                if (state != null) _notifiedVideoIds = state;
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to load state"); }
        }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_notifiedVideoIds);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to save state"); }
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_credentialsPath))
            {
                _logger.LogWarning("Credentials file not found at {CredentialsPath}. Please create an OAuth 2.0 Client ID in Google Cloud Console and save it here.", _credentialsPath);
                return false;
            }

            UserCredential credential;
            using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    cancellationToken,
                    new FileDataStore(_tokenPath, true));
            }

            _youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "YouTube Windows 11 Notifier",
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with YouTube Data API.");
            return false;
        }
    }

    public async Task<List<string>> GetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (_youtubeService == null) throw new InvalidOperationException("Not authenticated");

        var channelIds = new List<string>();
        var nextPageToken = "";

        try
        {
            do
            {
                var request = _youtubeService.Subscriptions.List("snippet");
                request.Mine = true;
                request.MaxResults = 50;
                request.PageToken = nextPageToken;

                var response = await request.ExecuteAsync(cancellationToken);
                
                foreach (var item in response.Items)
                {
                    channelIds.Add(item.Snippet.ResourceId.ChannelId);
                }

                nextPageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(nextPageToken));

            return channelIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions");
            return channelIds;
        }
    }

    public async Task HandleIncomingPushNotificationAsync(string videoId, string? channelId, string channelName, string title, string? publishedText)
    {
        // YouTube WebSub occasionally sends updates for old videos (e.g., description edits).
        // Using our state file prevents duplicate notifications for the same video ID.
        if (_notifiedVideoIds.Contains(videoId))
        {
            _logger.LogInformation("Ignored duplicate webhook payload for video ID {VideoId}", videoId);
            return;
        }

        string? thumbnailUrl = null;

        // Try to fetch the high-quality thumbnail if we are authenticated
        if (_youtubeService != null)
        {
            try
            {
                var request = _youtubeService.Videos.List("snippet");
                request.Id = videoId;
                var response = await request.ExecuteAsync();
                var video = response.Items.FirstOrDefault();
                
                if (video != null)
                {
                    thumbnailUrl = video.Snippet.Thumbnails?.High?.Url ?? video.Snippet.Thumbnails?.Default__?.Url;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch rich thumbnail for {VideoId}", videoId);
            }
        }

        await ShowCustomToastAsync(channelName, title, publishedText ?? "Just Now", thumbnailUrl, videoId);
        
        _notifiedVideoIds.Add(videoId);
        SaveState();
    }

    public async Task TestNotificationAsync(string videoId)
    {
        if (_youtubeService == null) 
        {
            _logger.LogWarning("Not authenticated. Cannot fetch test video data.");
            return;
        }

        try
        {
            var request = _youtubeService.Videos.List("snippet");
            request.Id = videoId;
            var response = await request.ExecuteAsync();
            var video = response.Items.FirstOrDefault();
            
            if (video != null)
            {
                var channelName = video.Snippet.ChannelTitle;
                var title = video.Snippet.Title;
                var publishedText = video.Snippet.PublishedAtDateTimeOffset?.LocalDateTime.ToString("g") ?? "Just Now";
                var thumbnailUrl = video.Snippet.Thumbnails?.High?.Url ?? video.Snippet.Thumbnails?.Default__?.Url;

                await ShowCustomToastAsync(channelName, title, publishedText, thumbnailUrl, videoId);
                _logger.LogInformation("Successfully fired test notification for video {VideoId}", videoId);
            }
            else
            {
                _logger.LogWarning("Video not found: {VideoId}", videoId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching test video {VideoId}", videoId);
        }
    }

    private async Task ShowCustomToastAsync(string channelTitle, string videoTitle, string publishedAt, string? thumbnailUrl, string videoId)
    {
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
            // Adds the YouTube logo as the "App Logo" replacing the generic .exe icon in the header
            .AddHeader("YouTube", "YouTube", "action=youtube")
            .AddText($"{channelTitle} uploaded a new video!")
            .AddText(videoTitle)
            .AddText($"Published: {publishedAt}");

        if (!string.IsNullOrEmpty(localThumbnailPath))
        {
            builder.AddHeroImage(new Uri(localThumbnailPath));
            builder.AddAppLogoOverride(new Uri(localThumbnailPath), ToastGenericAppLogoCrop.Circle);
        }
        
        builder.Show();
        _logger.LogInformation("Fired live push notification for {Title}!", videoTitle);
    }
}
