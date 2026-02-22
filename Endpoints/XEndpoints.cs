using Microsoft.AspNetCore.Mvc;
using NotiPulse.Services.X;

namespace NotiPulse.Endpoints;

public static class XEndpoints
{
    public static void MapXEndpoints(this IEndpointRouteBuilder app)
    {
        // Debug Endpoint to Test Notifications
        app.MapGet("/api/x/test", async ([FromQuery(Name = "url")] string url, XApiService apiService) => 
        {
            try
            {
                var uri = new Uri(url);
                // URL format: https://x.com/username/status/1234567890
                var segments = uri.AbsolutePath.Trim('/').Split('/');
                
                if (segments.Length >= 3 && segments[segments.Length - 2] == "status")
                {
                    var tweetId = segments[segments.Length - 1];
                    await apiService.TestNotificationAsync(tweetId);
                    return Results.Ok($"Test notification triggered for tweet {tweetId}");
                }
                
                return Results.BadRequest("Invalid X (Twitter) URL. Must contain /status/TWEET_ID.");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Error parsing URL: {ex.Message}");
            }
        });
    }
}