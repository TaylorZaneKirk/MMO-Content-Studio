using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class ConsumableItemValidator
{
    public const int MaximumRequirements = 16;
    public const int MaximumEffects = 16;

    private static readonly IReadOnlySet<string> UseActions =
        new HashSet<string>(["eat", "drink", "use"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> EffectTypes =
        new HashSet<string>(["restore_resource"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ResourceTargets =
        new HashSet<string>(["health", "concentration", "special"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> RequirementTypes =
        new HashSet<string>(["skill_minimum"], StringComparer.Ordinal);

    private readonly ItemAssetService _assetService;
    private readonly ConsumableItemRepository _repository;

    public ConsumableItemValidator(
        ItemAssetService assetService,
        ConsumableItemRepository repository)
    {
        _assetService = assetService;
        _repository = repository;
    }

    public async Task<ConsumableValidationOutcome> ValidateAsync(
        string itemId,
        NormalizedConsumableDraft draft,
        ConsumableItemRecord? existing,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateItemIdentity(itemId, draft.DisplayName, messages);

        if (existing is not null
            && (existing.EquipmentSlotId is not null || existing.RequiredStrength != 1))
        {
            messages.Add(new ApiError(
                "wrong_authoring_workspace",
                "This item has equipment metadata and cannot be changed by Consumables.",
                ValidationSeverity.Error,
                "item_id",
                "Open it in the Equipment workspace after T3 is implemented."));
        }

        var asset = _assetService.Resolve(draft.IconTexturePath);
        if (!asset.Exists)
        {
            messages.Add(new ApiError(
                "item_icon_unavailable",
                asset.Message ?? "The item icon is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "icon_texture_path",
                "Choose an existing PNG from the configured game-client items directory."));
        }

        if (!UseActions.Contains(draft.UseAction))
        {
            messages.Add(new ApiError(
                "invalid_use_action",
                "Use action must be eat, drink, or use.",
                ValidationSeverity.Error,
                "use_action"));
        }

        if (draft.ConsumeQuantity is < 1 or > 999)
        {
            messages.Add(new ApiError(
                "invalid_consume_quantity",
                "Consume quantity must be between 1 and 999.",
                ValidationSeverity.Error,
                "consume_quantity"));
        }

        if (draft.CooldownMs is < 0 or > 86_400_000)
        {
            messages.Add(new ApiError(
                "invalid_cooldown_ms",
                "Cooldown must be between 0 and 86,400,000 milliseconds.",
                ValidationSeverity.Error,
                "cooldown_ms"));
        }

        ValidateOptionalText(draft.SuccessMessage, 300, "success_message", messages);
        ValidateSemanticId(draft.UseAnimationId, "use_animation_id", messages);
        ValidateSoundPath(draft.UseSoundResourcePath, messages);

        await ValidateResultItemAsync(itemId, draft.ResultItemId, forPublication, messages, cancellationToken);
        await ValidateRequirementsAsync(draft.Requirements, messages, cancellationToken);
        ValidateEffects(draft.Effects, forPublication, messages);

        if (existing?.RuntimeEnabled == true && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish",
                "Saving or disabling this published consumable will make it unavailable after the next game-server restart.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        messages.Add(new ApiError(
            "runtime_consumption_integration_pending",
            forPublication
                ? "This profile can be published to the content database, but the current MMO game server will not execute it until the T2 runtime-consumption integration lands."
                : "The Content Studio can author this declarative profile, but the MMO game server still needs the T2 runtime-consumption integration before it executes these rows.",
            forPublication ? ValidationSeverity.Warning : ValidationSeverity.Info,
            "effects",
            "Apply the included migration now; complete the game-server consumer before treating newly authored consumables as playable."));

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        var hasEffect = draft.Effects.Count > 0;
        return new ConsumableValidationOutcome(
            !hasErrors,
            !hasErrors && asset.Exists && hasEffect,
            messages,
            asset.FilePath);
    }

    private static void ValidateItemIdentity(
        string itemId,
        string displayName,
        ICollection<ApiError> messages)
    {
        if (string.IsNullOrWhiteSpace(itemId)
            || itemId.Length > 100
            || !StableIdRegex().IsMatch(itemId))
        {
            messages.Add(new ApiError(
                "invalid_item_id",
                "Item IDs must be 1-100 lowercase letters, numbers, or single underscores between segments.",
                ValidationSeverity.Error,
                "item_id",
                "Use an ID such as 'minor_health_potion' or 'apple_slice'."));
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
    }

    private async Task ValidateResultItemAsync(
        string itemId,
        string? resultItemId,
        bool forPublication,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (resultItemId is null)
        {
            return;
        }

        if (!StableIdRegex().IsMatch(resultItemId))
        {
            messages.Add(new ApiError(
                "invalid_result_item_id",
                "Result item ID must use the stable lowercase underscore format.",
                ValidationSeverity.Error,
                "result_item_id"));
            return;
        }

        if (string.Equals(itemId, resultItemId, StringComparison.Ordinal))
        {
            messages.Add(new ApiError(
                "result_item_self_reference",
                "A consumable cannot transform into itself.",
                ValidationSeverity.Error,
                "result_item_id"));
            return;
        }

        var resultItem = await _repository.LoadReferencedItemAsync(resultItemId, cancellationToken);
        if (resultItem is null)
        {
            messages.Add(new ApiError(
                "result_item_not_found",
                $"Result item '{resultItemId}' does not exist.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "result_item_id",
                "Create the remaining portion or empty-container item before publishing."));
            return;
        }

        if (forPublication && !resultItem.RuntimeEnabled)
        {
            messages.Add(new ApiError(
                "result_item_not_published",
                $"Result item '{resultItemId}' exists but is not published.",
                ValidationSeverity.Error,
                "result_item_id",
                "Publish the result item before publishing this consumable."));
        }
    }

    private async Task ValidateRequirementsAsync(
        IReadOnlyList<ConsumableRequirementDefinition> requirements,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (requirements.Count > MaximumRequirements)
        {
            messages.Add(new ApiError(
                "too_many_consumable_requirements",
                $"A consumable may have at most {MaximumRequirements} requirements.",
                ValidationSeverity.Error,
                "requirements"));
        }

        var knownSkills = (await _repository.LoadSkillOptionsAsync(cancellationToken))
            .Select(skill => skill.Id)
            .ToHashSet(StringComparer.Ordinal);
        var seenSkills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            var field = $"requirements[{requirement.RequirementIndex}]";
            if (!RequirementTypes.Contains(requirement.RequirementType))
            {
                messages.Add(new ApiError(
                    "unsupported_consumable_requirement",
                    "T2 supports only skill_minimum requirements.",
                    ValidationSeverity.Error,
                    field));
                continue;
            }

            if (!knownSkills.Contains(requirement.TargetId))
            {
                messages.Add(new ApiError(
                    "unknown_requirement_skill",
                    $"Skill '{requirement.TargetId}' does not exist in skill_definitions.",
                    ValidationSeverity.Error,
                    field));
            }

            if (!seenSkills.Add(requirement.TargetId))
            {
                messages.Add(new ApiError(
                    "duplicate_requirement_skill",
                    $"Skill '{requirement.TargetId}' is required more than once.",
                    ValidationSeverity.Error,
                    field));
            }

            if (requirement.MinimumValue is < 1 or > 1_000_000)
            {
                messages.Add(new ApiError(
                    "invalid_requirement_minimum",
                    "Minimum skill value must be between 1 and 1,000,000.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidateEffects(
        IReadOnlyList<ConsumableEffectDefinition> effects,
        bool forPublication,
        ICollection<ApiError> messages)
    {
        if (effects.Count == 0)
        {
            messages.Add(new ApiError(
                "consumable_has_no_effects",
                "The consumable has no effects.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "effects",
                "Add at least one restore_resource effect before publishing."));
        }

        if (effects.Count > MaximumEffects)
        {
            messages.Add(new ApiError(
                "too_many_consumable_effects",
                $"A consumable may have at most {MaximumEffects} effects.",
                ValidationSeverity.Error,
                "effects"));
        }

        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            var field = $"effects[{effect.EffectIndex}]";
            if (!EffectTypes.Contains(effect.EffectType))
            {
                messages.Add(new ApiError(
                    "unsupported_consumable_effect",
                    "T2 supports only restore_resource effects.",
                    ValidationSeverity.Error,
                    field));
                continue;
            }

            if (!ResourceTargets.Contains(effect.TargetId))
            {
                messages.Add(new ApiError(
                    "unknown_resource_target",
                    $"Resource '{effect.TargetId}' is not health, concentration, or special.",
                    ValidationSeverity.Error,
                    field));
            }

            if (!seenTargets.Add(effect.TargetId))
            {
                messages.Add(new ApiError(
                    "duplicate_resource_effect",
                    $"Resource '{effect.TargetId}' is restored more than once.",
                    ValidationSeverity.Error,
                    field));
            }

            if (effect.MinimumAmount is < 1 or > 1_000_000
                || effect.MaximumAmount is < 1 or > 1_000_000
                || effect.MaximumAmount < effect.MinimumAmount)
            {
                messages.Add(new ApiError(
                    "invalid_effect_amount_range",
                    "Restore range must use positive values with maximum greater than or equal to minimum.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidateOptionalText(
        string? value,
        int maximumLength,
        string field,
        ICollection<ApiError> messages)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "invalid_optional_text",
                $"{field} must contain at most {maximumLength} printable characters.",
                ValidationSeverity.Error,
                field));
        }
    }

    private static void ValidateSemanticId(
        string? value,
        string field,
        ICollection<ApiError> messages)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > 100 || !StableIdRegex().IsMatch(value))
        {
            messages.Add(new ApiError(
                "invalid_semantic_id",
                $"{field} must use the stable lowercase underscore format.",
                ValidationSeverity.Error,
                field));
        }
    }

    private static void ValidateSoundPath(
        string? value,
        ICollection<ApiError> messages)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > 300
            || !value.StartsWith("res://assets/", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal))
        {
            messages.Add(new ApiError(
                "invalid_sound_resource_path",
                "Sound resource path must be a contained res://assets/... path.",
                ValidationSeverity.Error,
                "use_sound_resource_path"));
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}

public sealed record ConsumableValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
