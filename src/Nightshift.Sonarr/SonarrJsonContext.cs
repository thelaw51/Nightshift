using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nightshift.Sonarr;

/// <summary>
/// Source-generated serializer metadata for every Sonarr payload Nightshift reads.
/// Web defaults give camelCase names and case-insensitive matching.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<Series>))]
[JsonSerializable(typeof(List<Episode>))]
[JsonSerializable(typeof(List<RootFolder>))]
[JsonSerializable(typeof(PagedResult<Episode>))]
[JsonSerializable(typeof(PagedResult<QueueItem>))]
public sealed partial class SonarrJsonContext : JsonSerializerContext;
