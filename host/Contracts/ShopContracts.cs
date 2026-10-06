using System.Text.Json.Serialization;
namespace MMO.ContentStudio.AuthoringHost.Contracts;

// Complete reusable content. Stock array order is the authored stock_order.
public sealed record ShopDraft(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("buys_unstocked_items")] bool BuysUnstockedItems,
    [property: JsonPropertyName("price_change_per_stock_percent")] int PriceChangePerStockPercent,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("stock")] IReadOnlyList<ShopStockItem> Stock);
public sealed record ShopStockItem(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("default_stock")] int DefaultStock,
    [property: JsonPropertyName("restock_ticks")] int RestockTicks);
public sealed record ShopItemOption(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("runtime_enabled")] bool RuntimeEnabled,
    [property: JsonPropertyName("shop_policy")] string ShopPolicy,
    [property: JsonPropertyName("npc_buy_price")] long? NpcBuyPrice,
    [property: JsonPropertyName("npc_sell_price")] long? NpcSellPrice);
public sealed record ShopOptions(
    [property: JsonPropertyName("items")] IReadOnlyList<ShopItemOption> Items,
    [property: JsonPropertyName("shops")] IReadOnlyList<ShopSummary> Shops);
public sealed record ShopDefinition(
    [property: JsonPropertyName("shop_definition_id")] string DefinitionId,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("draft")] ShopDraft Draft,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record ShopSummary(
    [property: JsonPropertyName("shop_definition_id")] string DefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);

public sealed record ShopCatalogResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ShopSummary> Items);

public sealed record ShopRequest(
    [property: JsonPropertyName("draft")] ShopDraft Draft,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("target_operation")] string? TargetOperation);

public sealed record ShopPreview(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<ShopChange> Changes);

public sealed record ShopChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] object? Before,
    [property: JsonPropertyName("after")] object? After);

public sealed record ShopMutation(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("definition")] ShopDefinition? Definition,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);
