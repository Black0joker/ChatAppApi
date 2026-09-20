using ChatApp.Application.Abstractions;
using ChatApp.Application.Auth;
using ChatApp.Application.Common;
using ChatApp.Application.Conversations;
using ChatApp.Application.Messages;
using ChatApp.Application.Presence;
using ChatApp.Infrastructure.Persistence;
using ChatApp.Infrastructure.Persistence.Repositories;
using ChatApp.Infrastructure.Redis;
using ChatApp.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.Configure<ChatOptions>(config.GetSection(ChatOptions.SectionName));
        services.Configure<PresenceOptions>(config.GetSection(PresenceOptions.SectionName));
        services.AddHostedService<PresenceSweepService>();

        var sql = config.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is missing.");

        services.AddDbContext<ChatDbContext>(o => o.UseSqlServer(sql, s =>
        {
            s.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddSingleton<IPresenceStore>(sp => CreatePresenceStore(sp, config));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }

    /// <summary>
    /// Redis when reachable, in-memory presence otherwise. The API must stay
    /// fully functional single-instance without Redis (local dev); multi-instance
    /// presence/backplane requires the compose stack.
    /// </summary>
    private static IPresenceStore CreatePresenceStore(IServiceProvider sp, IConfiguration config)
    {
        var logger = sp.GetRequiredService<ILogger<RedisPresenceStore>>();
        var presenceOptions = sp.GetRequiredService<IOptions<PresenceOptions>>();
        var mux = RedisSetup.TryConnect(config.GetConnectionString("Redis"), logger);
        if (mux is null)
            return new InMemoryPresenceStore(presenceOptions);

        logger.LogInformation("Redis presence store connected.");
        return new RedisPresenceStore(mux, presenceOptions, logger);
    }
}
