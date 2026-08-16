using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SpinSpell.Server.Auth;
using SpinSpell.Server.Endpoints;
using SpinSpell.Server.Hubs;
using SpinSpell.Server.Persistence;
using SpinSpell.Server.Rooms;

var builder = WebApplication.CreateBuilder(args);

// Render, Railway and Fly all inject the port to bind as $PORT and route to it.
// Honouring it here means the same image deploys anywhere with no custom start
// command — and locally, where PORT is unset, nothing changes.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddDbContext<SpinSpellDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")
                      ?? "Host=localhost;Port=5432;Database=spinspell;Username=spinspell;Password=spinspell"));

builder.Services.AddScoped<RoomStore>();
builder.Services.AddSingleton<IRoomStore, ScopedRoomStore>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IRoomBroadcaster, HubBroadcaster>();
builder.Services.AddSingleton<RoomRegistry>();
// Built eagerly and shared: when no Auth:Secret is configured the service
// generates a random signing key, so a second instance would validate tokens
// against a key the first one never signed with.
var tokens = new PlayerTokenService(builder.Configuration);
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton<IUserIdProvider, PlayerIdProvider>();
builder.Services.AddHostedService<RetentionService>();

builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = tokens.ValidationParameters;

        // WebSockets cannot carry an Authorization header, so SignalR passes the
        // token as a query string on the negotiate/connect request instead.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hub"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var configured = builder.Configuration["Cors:Origins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries);

    if (configured is { Length: > 0 })
    {
        policy.WithOrigins(configured);
    }
    else if (builder.Environment.IsDevelopment())
    {
        // Playtesting means phones on the local network hitting the Vite dev
        // server by LAN IP, and that IP changes with the wifi. Pinning an origin
        // list here would just mean editing config before every test session.
        policy.SetIsOriginAllowed(_ => true);
    }
    else
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:4173");
    }

    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SpinSpellDbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapRoomEndpoints();
app.MapHub<GameHub>("/hub/game");

// Hit by the keep-warm cron every 10 minutes. A cold start costs the host 30-60
// seconds of staring at nothing, which is the single worst first impression this
// product can make — see §10 of the development plan.
app.MapGet("/health", (RoomRegistry rooms) => Results.Ok(new
{
    status = "ok",
    liveRooms = rooms.LiveRoomCount,
    time = DateTimeOffset.UtcNow
}));

app.Lifetime.ApplicationStopping.Register(() =>
    app.Services.GetRequiredService<RoomRegistry>().ShutdownAsync().GetAwaiter().GetResult());

app.Run();

/// <summary>
/// Nightly cleanup. Player-written dares are exactly the content not worth
/// keeping; the anonymous counts in game_results are.
/// </summary>
public sealed class RetentionService(IRoomStore store, ILogger<RetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await store.PurgeExpiredAsync(Retention, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Retention sweep failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
