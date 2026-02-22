namespace NotiPulse.Core;

public interface IToastNotificationService
{
    Task ShowToastAsync(string title, string content, string? heroImageUrl = null, string? logoUrl = null, string? clickUrl = null);
}