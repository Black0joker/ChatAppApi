using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ChatApp.Infrastructure.Redis;

/// <summary>
/// Single reachability probe shared by presence-store selection and backplane
/// wiring, so both make the same Redis up/down decision at startup.
/// </summary>
public static class RedisSetup
{
    public static IConnectionMultiplexer? TryConnect(string? connectionString, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.ConnectRetry = 1;
            var mux = ConnectionMultiplexer.Connect(options);
            mux.GetDatabase().Ping(); // fail fast when nothing is listening
            return mux;
        }
        catch (Exception ex)
        {
            // Concise: full SE.Redis dumps are pages of pool stats nobody reads at startup.
            logger.LogWarning("Redis unreachable ({Message}). Running single-instance: no backplane, in-memory presence.",
                ex.Message.Split('\n')[0]);
            return null;
        }
    }
}
