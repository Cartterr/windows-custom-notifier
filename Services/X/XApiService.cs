using System.Net.Http.Headers;
using System.Text.Json;
using NotiPulse.Core;

namespace NotiPulse.Services.X;

public class XApiService
{
    private readonly ILogger<XApiService> _logger;
    private readonly IToastNotificationService _toastService;
    private readonly string _credentialsPath;
    private readonly string _stateFilePath;
    private HashSet<string> _notifiedTweetIds = new();
    private string? _bearerToken;

    public XApiService(ILogger<XApiService> logger, IToastNotificationService toastService)
    {
        _logger = logger;
        _toastService = toastService;
        
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataFolder, "WindowsCustomNotifier");
        Directory.CreateDirectory(appFolder);
        
        _credentialsPath = Path.Combine(appFolder, "x_credentials.json");
        _stateFilePath = Path.Combine(appFolder, "x_state.json");
        
        LoadState();
        LoadCredentials();
    }

    private void LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<HashSet<string>>(json);
                if (state != null) _notifiedTweetIds = state;
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to load X state"); }
        }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_notifiedTweetIds);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to save X state"); }
    }

    private void LoadCredentials()
    {
        if (File.Exists(_credentialsPath))
        {
            try
            {
                var json = File.ReadAllText(_credentialsPath);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("BearerToken", out var bearerTokenElement))
                {
                    _bearerToken = bearerTokenElement.GetString();
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to load X credentials"); }
        }
    }

    public async Task CheckForNewTweetsAsync(IEnumerable<string> usernames, CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_bearerToken))
        {
            _logger.LogWarning("X API Bearer token not found in credentials.json. Polling paused.");
            return;
        }

        if (!usernames.Any()) return;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

        try
        {
            var usernamesQuery = string.Join(",", usernames.Select(u => u.TrimStart('@')));
            var userLookupUrl = $"https://api.twitter.com/2/users/by?usernames={usernamesQuery}&user.fields=profile_image_url";
            
            var userResponse = await httpClient.GetAsync(userLookupUrl, stoppingToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var error = await userResponse.Content.ReadAsStringAsync(stoppingToken);
                _logger.LogWarning("Failed to lookup X users. Status: {StatusCode}. Error: {Error}", userResponse.StatusCode, error);
                return;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(stoppingToken);
            using var userDoc = JsonDocument.Parse(userJson);
            
            if (!userDoc.RootElement.TryGetProperty("data", out var usersArray)) return;

            foreach (var user in usersArray.EnumerateArray())
            {
                var userId = user.GetProperty("id").GetString();
                var name = user.GetProperty("name").GetString();
                var username = user.GetProperty("username").GetString();
                string? profileImageUrl = null;
                if (user.TryGetProperty("profile_image_url", out var profileImageElement))
                {
                    profileImageUrl = profileImageElement.GetString()?.Replace("_normal", ""); 
                }

                if (userId != null)
                {
                    await CheckUserTimelineAsync(httpClient, userId, name ?? username ?? "X User", profileImageUrl, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking X API for new tweets");
        }
    }

    private async Task CheckUserTimelineAsync(HttpClient httpClient, string userId, string authorName, string? profileImageUrl, CancellationToken stoppingToken)
    {
        try
        {
            var timelineUrl = $"https://api.twitter.com/2/users/{userId}/tweets?max_results=5&tweet.fields=created_at&exclude=retweets,replies";
            var response = await httpClient.GetAsync(timelineUrl, stoppingToken);
            
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync(stoppingToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var tweetsArray)) return;

            var tweets = tweetsArray.EnumerateArray().Reverse().ToList();

            foreach (var tweet in tweets)
            {
                var tweetId = tweet.GetProperty("id").GetString();
                var text = tweet.GetProperty("text").GetString();

                if (tweetId != null && !_notifiedTweetIds.Contains(tweetId))
                {
                    _logger.LogInformation("New tweet found for {AuthorName}: {TweetId}", authorName, tweetId);
                    
                    var clickUrl = $"https://x.com/{authorName}/status/{tweetId}";
                    await _toastService.ShowToastAsync($"New post from {authorName} on X", text ?? "View post", null, profileImageUrl, clickUrl);

                    _notifiedTweetIds.Add(tweetId);
                    SaveState();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking timeline for user {UserId}", userId);
        }
    }
}