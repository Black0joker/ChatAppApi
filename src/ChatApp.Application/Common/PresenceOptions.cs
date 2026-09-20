namespace ChatApp.Application.Common;

public sealed class PresenceOptions
{
    public const string SectionName = "Presence";

    /// <summary>How long a connection stays valid without a heartbeat/disconnect.</summary>
    public int ConnectionTimeoutSeconds { get; set; } = 45;

    /// <summary>How often the sweeper evicts stale connections.</summary>
    public int SweepIntervalSeconds { get; set; } = 20;

    /// <summary>Advertised to clients: how often to call Heartbeat.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 15;
}
