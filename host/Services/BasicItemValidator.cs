using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class BasicItemValidator
{
    private readonly ItemAssetService _assetService;

    public BasicItemValidator(ItemAssetService assetService)
    {
        _assetService = assetService;
    }

    public BasicItemValidationOutcome Validate(
        string itemId,
        string displayName,
        string iconTexturePath,
        BasicItemRecord? existing,
        bool forPublication)
    {
        var messages = new List<ApiError>();

        if (string.IsNullOrWhiteSpace(itemId)
            || itemId.Length > 100
            || !StableItemIdRegex().IsMatch(itemId))
        {
            messages.Add(new ApiError(
                "invalid_item_id",
                "Item IDs must be 1-100 lowercase letters, numbers, or single underscores between segments.",
                ValidationSeverity.Error,
                "item_id",
                "Use an ID such as 'iron_ore' or 'quest_key_1'."));
        }

        var trimmedName = displayName.Trim();
        if (trimmedName.Length is < 1 or > 100 || trimmedName.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "invalid_display_name",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }

        if (existing is not null
            && (existing.EquipmentSlotId is not null
                || existing.RequiredStrength != 1
                || existing.HasConsumableProfile))
        {
            messages.Add(new ApiError(
                "wrong_authoring_workspace",
                existing.HasConsumableProfile
                    ? "This item has a consumable profile and cannot be changed by Basic Items."
                    : "This item has equipment metadata and cannot be changed by Basic Items.",
                ValidationSeverity.Error,
                "item_id",
                existing.HasConsumableProfile
                    ? "Open it in the Consumables workspace."
                    : "Open it in the Equipment workspace after T3 is implemented."));
        }

        var asset = _assetService.Resolve(iconTexturePath.Trim());
        if (!asset.Exists)
        {
            messages.Add(new ApiError(
                "item_icon_unavailable",
                asset.Message ?? "The item icon is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "icon_texture_path",
                "Choose an existing PNG from the configured game-client items directory."));
        }

        if (existing?.RuntimeEnabled == true && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish",
                "Saving or disabling this published item will make it unavailable after the next game-server restart.",
                ValidationSeverity.Warning,
                "publication_state"));
            messages.Add(new ApiError(
                "static_content_references_not_checked",
                "T1 checks live database references, but static mob-drop and future authored-content references are still validated by MMO server startup.",
                ValidationSeverity.Warning,
                "publication_state",
                "Run the MMO server startup validator after disabling content; T4 will move mob definitions into the database-backed authoring boundary."));
        }

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        return new BasicItemValidationOutcome(
            !hasErrors,
            !hasErrors && asset.Exists,
            messages,
            asset.FilePath);
    }

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableItemIdRegex();
}

public sealed record BasicItemValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
