using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;

namespace NotiPulse.Services.Google;

public class GoogleAuthService
{
    private readonly ILogger<GoogleAuthService> _logger;
    private readonly string _credentialsPath;
    private readonly string _tokenPath;

    public UserCredential? Credential { get; private set; }

    private static readonly string[] Scopes = { 
        "https://www.googleapis.com/auth/youtube.readonly",
        "https://www.googleapis.com/auth/generative-language"
    };

    public GoogleAuthService(ILogger<GoogleAuthService> logger)
    {
        _logger = logger;
        
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataFolder, "WindowsCustomNotifier");
        Directory.CreateDirectory(appFolder);
        
        _credentialsPath = Path.Combine(appFolder, "credentials.json");
        _tokenPath = Path.Combine(appFolder, "token");
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (Credential != null) return true;

        try
        {
            if (!File.Exists(_credentialsPath))
            {
                _logger.LogWarning("Credentials file not found at {CredentialsPath}.", _credentialsPath);
                return false;
            }

            using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
            {
                Credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    cancellationToken,
                    new FileDataStore(_tokenPath, true));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Google OAuth.");
            return false;
        }
    }
    
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (Credential == null) return null;
        return await Credential.GetAccessTokenForRequestAsync(cancellationToken);
    }
}