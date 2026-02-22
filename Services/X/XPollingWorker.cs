namespace NotiPulse.Services.X;

public class XPollingWorker : BackgroundService
{
    private readonly ILogger<XPollingWorker> _logger;
    private readonly XApiService _xApiService;
    private readonly IConfiguration _configuration;

    public XPollingWorker(ILogger<XPollingWorker> logger, XApiService xApiService, IConfiguration configuration)
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
                    _logger.LogInformation("No XTargetUsernames configured in appsettings.json. Skipping X polling.");
                }
                else
                {
                    await _xApiService.CheckForNewTweetsAsync(usernames, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during X polling loop.");
            }

            // Poll every 5 minutes to conserve API credits. 
            // In Pay-Per-Use, each API request costs credits.
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}