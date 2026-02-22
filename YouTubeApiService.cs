using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace YoutubeNotifier;

public class YouTubeApiService
{
    private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeReadonly };
    private YouTubeService? _youtubeService;
    private readonly ILogger<YouTubeApiService> _logger;
    private readonly string _credentialsPath;
    private readonly string _tokenPath;

    public YouTubeApiService(ILogger<YouTubeApiService> logger)
    {
        _logger = logger;
        
        // Define paths for credentials and tokens
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataFolder, "WindowsCustomNotifier");
        Directory.CreateDirectory(appFolder);
        
        _credentialsPath = Path.Combine(appFolder, "credentials.json");
        _tokenPath = Path.Combine(appFolder, "token");
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

            _logger.LogInformation("Successfully authenticated with YouTube Data API.");
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

    public async Task<List<Activity>> GetRecentActivitiesAsync(List<string> channelIds, DateTime afterDate, CancellationToken cancellationToken)
    {
        if (_youtubeService == null) throw new InvalidOperationException("Not authenticated");

        var activities = new List<Activity>();

        // Google API has quotas, so we only check a subset of channels or use the user's home feed.
        // Let's use the user's home feed activities which is much more quota efficient.
        try
        {
            var request = _youtubeService.Activities.List("snippet,contentDetails");
            request.Home = true;
            request.MaxResults = 50;
            request.PublishedAfterDateTimeOffset = afterDate;

            var response = await request.ExecuteAsync(cancellationToken);

            foreach (var item in response.Items)
            {
                if (item.Snippet.Type == "upload") // Only care about new uploads
                {
                    activities.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activities");
        }

        return activities;
    }
}
