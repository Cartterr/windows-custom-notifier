using NotiPulse.Core;
using NotiPulse.Endpoints;
using NotiPulse.Services.YouTube;
using NotiPulse.Services.X;
using NotiPulse.Services.Google;
using NotiPulse.Services.Gemini;

var builder = WebApplication.CreateBuilder(args);

// Register Core Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IToastNotificationService, ToastNotificationService>();
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddSingleton<IAiSummarizerService, GeminiSummarizerService>();

// Register YouTube Services
builder.Services.AddSingleton<YouTubeApiService>();
builder.Services.AddHostedService<YouTubeSubscriptionWorker>();

// Register X Services
builder.Services.AddSingleton<XApiService>();
builder.Services.AddHostedService<XStreamWorker>();

var app = builder.Build();

var port = builder.Configuration.GetValue<int>("Port", 5000);

// Map Endpoints
app.MapYouTubeEndpoints();
app.MapXEndpoints();

app.Run($"http://*:{port}");