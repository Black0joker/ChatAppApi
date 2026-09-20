namespace ChatApp.Application.Common;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "Local";
    /// <summary>Local provider root. Relative paths resolve against the app base directory.</summary>
    public string LocalPath { get; set; } = "App_Data/attachments";
}
