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

    public async Task StartStreamAsync(IEnumerable<string> usernames, CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_bearerToken))
        {
            _logger.LogWarning("X API Bearer token not found. Stream paused.");
            return;
        }

        if (!usernames.Any()) return;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

        // 1. Sync Rules
        await SyncStreamRulesAsync(httpClient, usernames, stoppingToken);

        // 2. Connect to Stream
        _logger.LogInformation("Connecting to X live Filtered Stream...");
        var streamUrl = "https://api.twitter.com/2/tweets/search/stream?expansions=author_id&user.fields=profile_image_url,username";
        
        try
        {
            // Use HttpCompletionOption.ResponseHeadersRead so we can read the stream infinitely
            using var response = await httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(stoppingToken);
                _logger.LogWarning("Failed to connect to X stream. Status: {StatusCode}. Error: {Error}", response.StatusCode, error);
                return;
            }

            _logger.LogInformation("Connected successfully to X live Filtered Stream! Listening for tweets...");
            
            using var stream = await response.Content.ReadAsStreamAsync(stoppingToken);
            using var reader = new StreamReader(stream);

            while (!stoppingToken.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(line)) continue; // Keep-alive heartbeat from X

                ProcessStreamLine(line);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error while reading from X stream");
            throw; // Let the worker catch it and restart
        }
    }

    private void ProcessStreamLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            
            var tweetId = data.GetProperty("id").GetString();
            var text = data.GetProperty("text").GetString();
            var authorId = data.GetProperty("author_id").GetString();

            if (tweetId == null || _notifiedTweetIds.Contains(tweetId)) return;

            string authorName = "X User";
            string authorUsername = "X";
            string? profileImageUrl = null;

            if (doc.RootElement.TryGetProperty("includes", out var includes) && 
                includes.TryGetProperty("users", out var users))
            {
                foreach (var user in users.EnumerateArray())
                {
                    if (user.GetProperty("id").GetString() == authorId)
                    {
                        authorName = user.GetProperty("name").GetString() ?? authorName;
                        authorUsername = user.GetProperty("username").GetString() ?? authorUsername;
                        if (user.TryGetProperty("profile_image_url", out var profileImageElement))
                        {
                            profileImageUrl = profileImageElement.GetString()?.Replace("_normal", ""); 
                        }
                        break;
                    }
                }
            }

            _logger.LogInformation("Instant tweet received from {AuthorName}: {TweetId}", authorName, tweetId);
            
            var clickUrl = $"https://x.com/{authorUsername}/status/{tweetId}";
            // Fire and forget so we don't block the stream
            _ = _toastService.ShowToastAsync($"New post from {authorName} on X", text ?? "View post", null, profileImageUrl, clickUrl);

            _notifiedTweetIds.Add(tweetId);
            SaveState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse X stream payload");
        }
    }

    private async Task SyncStreamRulesAsync(HttpClient httpClient, IEnumerable<string> usernames, CancellationToken stoppingToken)
    {
        // For simplicity, we just delete all existing rules and recreate them.
        var rulesUrl = "https://api.twitter.com/2/tweets/search/stream/rules";
        
        var getRulesResponse = await httpClient.GetAsync(rulesUrl, stoppingToken);
        if (getRulesResponse.IsSuccessStatusCode)
        {
            var json = await getRulesResponse.Content.ReadAsStringAsync(stoppingToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var ruleIds = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Where(id => id != null).ToList();
                if (ruleIds.Any())
                {
                    var deletePayload = new { delete = new { ids = ruleIds } };
                    var deleteContent = new StringContent(JsonSerializer.Serialize(deletePayload), System.Text.Encoding.UTF8, "application/json");
                    await httpClient.PostAsync(rulesUrl, deleteContent, stoppingToken);
                }
            }
        }

        // Build new rules. E.g., "from:elonmusk OR from:SpaceX -is:retweet -is:reply"
        var ruleValue = string.Join(" OR ", usernames.Select(u => $"from:{u.TrimStart('@')}"));
        ruleValue += " -is:retweet -is:reply";
        
        var addPayload = new { add = new[] { new { value = ruleValue, tag = "NotiPulse Target Users" } } };
        var addContent = new StringContent(JsonSerializer.Serialize(addPayload), System.Text.Encoding.UTF8, "application/json");
        var addResponse = await httpClient.PostAsync(rulesUrl, addContent, stoppingToken);
        
        if (!addResponse.IsSuccessStatusCode)
        {
            var error = await addResponse.Content.ReadAsStringAsync(stoppingToken);
            _logger.LogWarning("Failed to set X stream rules. Status: {StatusCode}. Error: {Error}", addResponse.StatusCode, error);
        }
    }
}