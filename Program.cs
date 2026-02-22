using NotiPulse.Core;
using NotiPulse.Endpoints;
using NotiPulse.Services.YouTube;

var builder = WebApplication.CreateBuilder(args);

// Register Core Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IToastNotificationService, ToastNotificationService>();

// Register YouTube Services
builder.Services.AddSingleton<YouTubeApiService>();
builder.Services.AddHostedService<YouTubeSubscriptionWorker>();

var app = builder.Build();

var port = builder.Configuration.GetValue<int>("Port", 5000);

// Map Endpoints
app.MapYouTubeEndpoints();

// Future endpoints will be mapped here:
// app.MapXEndpoints();

app.Run($"http://*:{port}");