using YoutubeNotifier;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<YouTubeApiService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
