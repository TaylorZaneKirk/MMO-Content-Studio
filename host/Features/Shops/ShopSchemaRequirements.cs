using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Shops;
public sealed class ShopSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "shop-authoring-v1";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("shop_definitions"),
        AuthoringSchemaRequirement.Column("shop_definitions", "shop_definition_id"),
        AuthoringSchemaRequirement.Column("shop_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("shop_definitions", "publication_state"),
        AuthoringSchemaRequirement.Column("shop_definitions", "buys_unstocked_items"),
        AuthoringSchemaRequirement.Column("shop_definitions", "price_change_per_stock_percent"),
        AuthoringSchemaRequirement.Column("shop_definitions", "notes"),
        AuthoringSchemaRequirement.Column("shop_definitions", "created_at"),
        AuthoringSchemaRequirement.Column("shop_definitions", "updated_at"),
        AuthoringSchemaRequirement.Table("shop_stock_items"),
        AuthoringSchemaRequirement.Column("shop_stock_items", "shop_definition_id"),
        AuthoringSchemaRequirement.Column("shop_stock_items", "stock_order"),
        AuthoringSchemaRequirement.Column("shop_stock_items", "item_id"),
        AuthoringSchemaRequirement.Column("shop_stock_items", "default_stock"),
        AuthoringSchemaRequirement.Column("shop_stock_items", "restock_ticks"),
        AuthoringSchemaRequirement.Column("shop_stock_items", "updated_at"),
    ];
}
