using ChatApp.Api.Extensions;
using ChatApp.Api.Hubs;
using ChatApp.Api.Middleware;
using ChatApp.Api.Presence;
using ChatApp.Infrastructure;
using ChatApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- Layered services (PLAN §3) ---
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuth(builder.Configuration);

// --- Real-time: Redis backplane only when Redis is actually reachable (PLAN §21) ---
// A configured-but-down Redis must NOT enable the backplane: its lifetime manager
// throws on SUBSCRIBE and tears down every SignalR connection. Probe first.
var signalR = builder.Services.AddSignalR();
var redisCs = builder.Configuration.GetConnectionString("Redis");
using (var probeLog = LoggerFactory.Create(b => b.AddConsole()))
{
    using var probe = ChatApp.Infrastructure.Redis.RedisSetup.TryConnect(
        redisCs, probeLog.CreateLogger("Startup"));
    if (probe is not null)
    {
        var redisOptions = ConfigurationOptions.Parse(redisCs!);
        redisOptions.AbortOnConnectFail = false;
        signalR.AddStackExchangeRedis(o =>
        {
            o.Configuration = redisOptions;
            o.Configuration.ChannelPrefix = RedisChannel.Literal("ChatApp");
        });
    }
}

// --- Presence transition -> SignalR broadcast bridge ---
builder.Services.AddHostedService<PresenceBroadcastService>();

// --- Controllers ---
builder.Services.AddControllers();

// --- Health checks (PLAN §33): liveness vs readiness ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ChatDbContext>("sqlserver", tags: ["ready"]);

// --- Swagger (dev) with JWT support ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "ChatApp API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Structured logging note (PLAN §32): default providers already emit TraceId/UserId-friendly
// scopes; avoid logging message content (sensitive) — enforced by convention in later phases.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Liveness: process is up. Readiness: dependencies (SQL Server) reachable.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

// Phase 1 deliverable: API connects to SQL Server. Apply migrations on startup
// so a fresh `docker compose up` + `dotnet run` yields a working database.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrated successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed.");
        throw;
    }
}

app.Run();

public partial class Program { }
