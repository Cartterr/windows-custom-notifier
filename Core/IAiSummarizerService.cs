namespace NotiPulse.Core;

public interface IAiSummarizerService
{
    /// <summary>
    /// Analyzes the content (video title, tweet text, etc.) and returns a short, punchy 1-line sentence explaining why the user should care.
    /// </summary>
    Task<string> GetShortSummaryAsync(string authorName, string content, CancellationToken cancellationToken = default);
}