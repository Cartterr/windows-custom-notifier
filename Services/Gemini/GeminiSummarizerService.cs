using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NotiPulse.Core;
using NotiPulse.Services.Google;

namespace NotiPulse.Services.Gemini;

public class GeminiSummarizerService : IAiSummarizerService
{
    private readonly ILogger<GeminiSummarizerService> _logger;
    private readonly GoogleAuthService _googleAuthService;

    public GeminiSummarizerService(ILogger<GeminiSummarizerService> logger, GoogleAuthService googleAuthService)
    {
        _logger = logger;
        _googleAuthService = googleAuthService;
    }

    public async Task<string> GetShortSummaryAsync(string authorName, string content, CancellationToken cancellationToken = default)
    {
        // Ensure we are authenticated
        await _googleAuthService.AuthenticateAsync(cancellationToken);
        
        var accessToken = await _googleAuthService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Google OAuth access token not available. Skipping AI summary.");
            return string.Empty;
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = $"You are a helpful notification assistant. The user just received a post/video from '{authorName}' with this content: '{content}'. Write a very short, punchy 1-line sentence (max 10 words) explaining why the user should care or what it's about. Do not use quotes." }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 30,
                temperature = 0.7
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Use gemini-1.5-flash for instant ultra-low latency responses
        var response = await client.PostAsync("https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent", httpContent, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Gemini API failed. Status: {Status}, Error: {Error}", response.StatusCode, error);
            return string.Empty;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        try
        {
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini response");
            return string.Empty;
        }
    }
}