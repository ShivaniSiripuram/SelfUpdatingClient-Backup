namespace POS_UPDATER_SYSTEM.Api.Options;

public sealed class UpdaterOptions
{
    public const string SectionName = "Updater";

    public string StorageRoot { get; set; } = "Storage";

    public string LatestJsonUrl { get; set; } = "http://localhost/updates/latest.json";

    public string? LiveAppBaseUrl { get; set; } = "http://localhost:5010";

    public int CheckIntervalMinutes { get; set; } = 30;

    public string MainScriptPattern { get; set; } = "main*.js";
}

