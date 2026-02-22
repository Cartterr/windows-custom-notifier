using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;
using NotiPulse.Services.YouTube;

namespace NotiPulse.Endpoints;

public static class YouTubeEndpoints
{
    public static void MapYouTubeEndpoints(this IEndpointRouteBuilder app)
    {
        // Webhook Verification Endpoint (PubSubHubbub challenge)
        app.MapGet("/api/youtube/webhook", ([FromQuery(Name = "hub.challenge")] string challenge, 
                                            [FromQuery(Name = "hub.topic")] string topic, 
                                            [FromQuery(Name = "hub.mode")] string mode) => 
        {
            // Google sends a GET request with hub.challenge when we subscribe.
            // We must return the exact challenge text in a 200 OK response with content-type text/plain.
            return Results.Content(challenge, "text/plain");
        });

        // Webhook Payload Endpoint (PubSubHubbub Push)
        app.MapPost("/api/youtube/webhook", async (HttpContext context, YouTubeApiService apiService) => 
        {
            try
            {
                using var reader = new StreamReader(context.Request.Body);
                var xmlContent = await reader.ReadToEndAsync();
                
                if (string.IsNullOrEmpty(xmlContent)) return Results.Ok();

                var xdoc = XDocument.Parse(xmlContent);
                var atomNs = XNamespace.Get("http://www.w3.org/2005/Atom");
                var ytNs = XNamespace.Get("http://www.youtube.com/xml/schemas/2015");

                var entry = xdoc.Descendants(atomNs + "entry").FirstOrDefault();
                if (entry != null)
                {
                    var videoId = entry.Element(ytNs + "videoId")?.Value;
                    var channelId = entry.Element(ytNs + "channelId")?.Value;
                    var title = entry.Element(atomNs + "title")?.Value;
                    
                    var author = entry.Element(atomNs + "author");
                    var channelName = author?.Element(atomNs + "name")?.Value ?? "YouTube";
                    
                    var publishedText = entry.Element(atomNs + "published")?.Value;

                    // When a video is updated (e.g. title changes), Google pushes another notification.
                    // A brand new upload usually has Published close to Updated.
                    // But checking against our seen cache is safest. We just pass it to the service.
                    if (!string.IsNullOrEmpty(videoId) && !string.IsNullOrEmpty(title))
                    {
                        await apiService.HandleIncomingPushNotificationAsync(videoId, channelId, channelName, title, publishedText);
                    }
                }
            }
            catch (Exception ex)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("YouTubeWebhook")
                    .LogError(ex, "Error processing webhook payload");
            }

            // Google always expects a 20x response, even if parsing fails, otherwise it retries.
            return Results.Ok();
        });

        // Debug Endpoint to Test Notifications
        app.MapGet("/api/youtube/test", async ([FromQuery(Name = "url")] string url, YouTubeApiService apiService) => 
        {
            try
            {
                var uri = new Uri(url);
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
                
                if (query.TryGetValue("v", out var videoIdValues))
                {
                    var videoId = videoIdValues.ToString();
                    await apiService.TestNotificationAsync(videoId);
                    return Results.Ok($"Test notification triggered for video {videoId}");
                }
                
                return Results.BadRequest("Invalid YouTube URL. Must contain a ?v= parameter.");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Error parsing URL: {ex.Message}");
            }
        });
    }
}