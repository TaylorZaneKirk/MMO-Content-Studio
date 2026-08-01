using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record ContentCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("sections")] IReadOnlyList<ContentCatalogSection> Sections);

public sealed record ContentCatalogSection(
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("implemented")] bool Implemented,
    [property: JsonPropertyName("entries")] IReadOnlyList<ContentCatalogEntry> Entries);

public sealed record ContentCatalogEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);
