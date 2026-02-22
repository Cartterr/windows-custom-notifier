using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using NotiPulse.Core;

namespace NotiPulse.Services.YouTube;

public class YouTubeApiService
{
    private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeReadonly };
    private YouTubeService? _youtubeService;
    private readonly ILogger<YouTubeApiService> _logger;
    private readonly IToastNotificationService _toastService;
    private readonly string _credentialsPath;
    private readonly string _tokenPath;
    private readonly string _stateFilePath;
    private HashSet<string> _notifiedVideoIds = new();

    public YouTubeApiService(ILogger<YouTubeApiService> logger, IToastNotificationService toastService)
    {
        _logger = logger;
        _toastService = toastService;
        
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
                ApplicationName = "NotiPulse Notification System",
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
        string? channelProfileUrl = null;

        // Try to fetch the high-quality thumbnail and channel profile if we are authenticated
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
                    thumbnailUrl = video.Snippet.Thumbnails?.Maxres?.Url 
                                ?? video.Snippet.Thumbnails?.Standard?.Url 
                                ?? video.Snippet.Thumbnails?.High?.Url 
                                ?? video.Snippet.Thumbnails?.Default__?.Url;
                }

                if (!string.IsNullOrEmpty(channelId))
                {
                    channelProfileUrl = await GetChannelProfilePictureUrlAsync(channelId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch rich data for {VideoId}", videoId);
            }
        }

        var clickUrl = $"https://www.youtube.com/watch?v={videoId}";
        await _toastService.ShowToastAsync(title, channelName, thumbnailUrl, channelProfileUrl, clickUrl);
        
        _notifiedVideoIds.Add(videoId);
        SaveState();
    }

    private async Task<string?> GetChannelProfilePictureUrlAsync(string channelId)
    {
        if (_youtubeService == null || string.IsNullOrEmpty(channelId)) return null;
        try
        {
            var request = _youtubeService.Channels.List("snippet");
            request.Id = channelId;
            var response = await request.ExecuteAsync();
            var channel = response.Items.FirstOrDefault();
            return channel?.Snippet?.Thumbnails?.Default__?.Url ?? channel?.Snippet?.Thumbnails?.High?.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch channel profile picture for {ChannelId}", channelId);
            return null;
        }
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
                var thumbnailUrl = video.Snippet.Thumbnails?.Maxres?.Url 
                                ?? video.Snippet.Thumbnails?.Standard?.Url 
                                ?? video.Snippet.Thumbnails?.High?.Url 
                                ?? video.Snippet.Thumbnails?.Default__?.Url;
                
                var channelProfileUrl = await GetChannelProfilePictureUrlAsync(video.Snippet.ChannelId);

                var clickUrl = $"https://www.youtube.com/watch?v={videoId}";
                await _toastService.ShowToastAsync(title, channelName, thumbnailUrl, channelProfileUrl, clickUrl);

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
}