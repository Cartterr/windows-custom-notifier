using Microsoft.Toolkit.Uwp.Notifications;

namespace NotiPulse.Core;

public class ToastNotificationService : IToastNotificationService
{
    private readonly ILogger<ToastNotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ToastNotificationService(ILogger<ToastNotificationService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task ShowToastAsync(string title, string content, string? heroImageUrl = null, string? logoUrl = null, string? clickUrl = null)
    {
        string? localHeroImagePath = null;
        if (!string.IsNullOrEmpty(heroImageUrl))
        {
            localHeroImagePath = await DownloadImageAsync(heroImageUrl, "hero");
        }

        string? localLogoPath = null;
        if (!string.IsNullOrEmpty(logoUrl))
        {
            localLogoPath = await DownloadImageAsync(logoUrl, "logo");
        }

        var defaultLogoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notipulse_icon.png");
        var finalLogoUri = !string.IsNullOrEmpty(localLogoPath) ? new Uri(localLogoPath) : new Uri(defaultLogoPath);

        var builder = new ToastContentBuilder();

        if (!string.IsNullOrEmpty(clickUrl))
        {
            builder.SetProtocolActivation(new Uri(clickUrl));
        }

        builder.AddText(title, hintWrap: true, hintMaxLines: 2)
               .AddText(content, hintWrap: true, hintMaxLines: 4);

        if (!string.IsNullOrEmpty(localHeroImagePath))
        {
            builder.AddHeroImage(new Uri(localHeroImagePath));
        }
        
        builder.AddAppLogoOverride(finalLogoUri, ToastGenericAppLogoCrop.Circle);
        
        builder.Show(toast => 
        {
            toast.Tag = Guid.NewGuid().ToString();
            toast.Group = "NotiPulse";
        });
        _logger.LogInformation("Fired live push notification for {Title}!", title);
    }

    private async Task<string?> DownloadImageAsync(string url, string prefix)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var imageBytes = await client.GetByteArrayAsync(url);
            
            var safeId = Guid.NewGuid().ToString("N");
            var tempPath = Path.Combine(Path.GetTempPath(), $"notipulse_{prefix}_{safeId}.jpg");
            await File.WriteAllBytesAsync(tempPath, imageBytes);
            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image {Url}", url);
            return null;
        }
    }
}