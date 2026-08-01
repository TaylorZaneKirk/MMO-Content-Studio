using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record BasicItemCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<BasicItemSummary> Items);

public sealed record BasicItemSummary(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record BasicItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("editable_in_basic_items")] bool EditableInBasicItems,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record SaveBasicItemDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc);

public sealed record BasicItemPreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record BasicItemValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record BasicItemChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] string? Before,
    [property: JsonPropertyName("after")] string? After);

public sealed record BasicItemMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("item")] BasicItemDefinition Item,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record ItemAssetCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("assets")] IReadOnlyList<ItemAssetEntry> Assets);

public sealed record ItemAssetEntry(
    [property: JsonPropertyName("resource_path")] string ResourcePath,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("file_path")] string FilePath);


public sealed record ImportItemAssetRequest(
    [property: JsonPropertyName("source_file_path")] string SourceFilePath,
    [property: JsonPropertyName("target_file_name")] string? TargetFileName);

public sealed record ImportItemAssetResponse(
    [property: JsonPropertyName("asset")] ItemAssetEntry Asset,
    [property: JsonPropertyName("created")] bool Created,
    [property: JsonPropertyName("message")] string Message);
