namespace NotiPulse.Services.X;

public class XStreamWorker : BackgroundService
{
    private readonly ILogger<XStreamWorker> _logger;
    private readonly XApiService _xApiService;
    private readonly IConfiguration _configuration;

    public XStreamWorker(ILogger<XStreamWorker> logger, XApiService xApiService, IConfiguration configuration)
    {
        _logger = logger;
        _xApiService = xApiService;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var usernames = _configuration.GetSection("XTargetUsernames").Get<string[]>();
                if (usernames == null || usernames.Length == 0)
                {
                    _logger.LogInformation("No XTargetUsernames configured in appsettings.json. Skipping X streaming.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Starting X live stream connection...");
                await _xApiService.StartStreamAsync(usernames, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "X Stream disconnected or threw an error. Reconnecting in 10 seconds...");
            }

            // If the stream disconnects cleanly or crashes, wait 10 seconds before trying to reconnect to avoid rate limits
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}