using System.Text.Json.Serialization;

namespace POS_UPDATER_SYSTEM.Api.Models;

public sealed class LatestUpdateInfo
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("package")]
    public required string Package { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }
}
