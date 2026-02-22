namespace NotiPulse.Core;

public interface IToastNotificationService
{
    Task ShowToastAsync(string title, string content, string? thirdRow = null, string? heroImageUrl = null, string? logoUrl = null, string? clickUrl = null);
}