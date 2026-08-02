using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class EquipmentItemValidator
{
    private const int MaximumRequirements = 16;
    private const int MaximumModifiers = 16;
    private const int MaximumMagnitude = 1_000_000;

    private readonly EquipmentItemRepository _repository;
    private readonly ItemAssetService _assetService;

    public EquipmentItemValidator(
        EquipmentItemRepository repository,
        ItemAssetService assetService)
    {
        _repository = repository;
        _assetService = assetService;
    }

    public async Task<EquipmentValidationOutcome> ValidateAsync(
        string itemId,
        NormalizedEquipmentDraft draft,
        EquipmentItemRecord existing,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(itemId, draft.DisplayName, messages);

        if (existing.HasConsumableProfile)
        {
            messages.Add(new ApiError(
                "wrong_authoring_workspace",
                "Consumable items cannot be edited by Equipment.",
                ValidationSeverity.Error,
                "item_id",
                "Open this item in the Consumables workspace."));
        }

        var asset = _assetService.Resolve(draft.IconTexturePath);
        if (!asset.Exists)
        {
            messages.Add(new ApiError(
                "item_icon_unavailable",
                asset.Message ?? "The item icon is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "icon_texture_path",
                "Choose or import an existing PNG from the configured game-client items directory."));
        }

        if (draft.Equippable)
        {
            await ValidateEquippableAsync(draft, existing, messages, cancellationToken);
        }
        else
        {
            ValidateNotEquippable(draft, existing, messages);
        }

        if (existing.RuntimeEnabled && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish",
                "Saving or disabling this published item will make it unavailable after the next game-server restart.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        messages.Add(new ApiError(
            "derived_equipment_visual_key",
            "The current game client derives wearable visual keys from display name and slot; direct paper-doll asset overrides remain deferred.",
            ValidationSeverity.Info,
            "display_name"));

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        return new EquipmentValidationOutcome(
            !hasErrors,
            !hasErrors && asset.Exists,
            messages,
            asset.FilePath);
    }

    private async Task ValidateEquippableAsync(
        NormalizedEquipmentDraft draft,
        EquipmentItemRecord existing,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        var slots = (await _repository.LoadSlotsAsync(cancellationToken))
            .Where(slot => EquipmentItemRepository.IsWearableSlot(slot.SlotId))
            .Select(slot => slot.SlotId)
            .ToHashSet(StringComparer.Ordinal);
        if (draft.EquipmentSlotId is null || !slots.Contains(draft.EquipmentSlotId))
        {
            messages.Add(new ApiError(
                "invalid_wearable_slot",
                "T3A requires one wearable slot: head, cape, body, legs, boots, gloves, or ring.",
                ValidationSeverity.Error,
                "equipment_slot_id",
                "Hand-held weapons and tools remain in T3B."));
        }

        if (existing.HasCombatProfile)
        {
            messages.Add(new ApiError(
                "weapon_or_tool_requires_t3b",
                "This item has a combat profile and cannot be edited as wearable equipment.",
                ValidationSeverity.Error,
                "equipment_slot_id",
                "Turn off Equippable to deliberately strip all equipment/combat metadata, or wait for T3B to edit the weapon/tool."));
        }

        if (draft.RequiredStrength is < 1 or > MaximumMagnitude)
        {
            messages.Add(new ApiError(
                "invalid_required_strength",
                $"Required strength must be between 1 and {MaximumMagnitude:N0}.",
                ValidationSeverity.Error,
                "required_strength"));
        }

        var knownSkills = (await _repository.LoadSkillsAsync(cancellationToken))
            .Select(skill => skill.SkillId)
            .ToHashSet(StringComparer.Ordinal);
        ValidateRequirements(draft, knownSkills, messages);
        ValidateModifiers(draft, knownSkills, messages);
        ValidateBonuses(draft.CombatBonuses, messages);
    }

    private static void ValidateNotEquippable(
        NormalizedEquipmentDraft draft,
        EquipmentItemRecord existing,
        ICollection<ApiError> messages)
    {
        if (draft.EquipmentSlotId is not null
            || draft.RequiredStrength != 1
            || draft.Requirements.Count != 0
            || draft.SkillModifiers.Count != 0
            || !draft.CombatBonuses.IsZero)
        {
            messages.Add(new ApiError(
                "non_equippable_metadata_not_empty",
                "Not-equippable items cannot retain a slot, requirements, modifiers, or combat bonuses.",
                ValidationSeverity.Error,
                "equippable"));
        }

        if (EquipmentItemRepository.HasEquipmentMetadata(existing))
        {
            messages.Add(new ApiError(
                "equipment_metadata_will_be_removed",
                "Applying this draft will remove the equipment slot, strength gate, skill requirements, skill modifiers, combat profile, and combat bonuses.",
                ValidationSeverity.Warning,
                "equippable",
                "This is the intended operation for ordinary materials such as Chunk of Iron that were misclassified as equipment."));
        }
    }

    private static void ValidateRequirements(
        NormalizedEquipmentDraft draft,
        IReadOnlySet<string> knownSkills,
        ICollection<ApiError> messages)
    {
        if (draft.Requirements.Count > MaximumRequirements)
        {
            messages.Add(new ApiError(
                "too_many_equipment_requirements",
                $"Equipment may have at most {MaximumRequirements} skill requirements.",
                ValidationSeverity.Error,
                "requirements"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in draft.Requirements)
        {
            var field = $"requirements[{requirement.SkillId}]";
            if (!knownSkills.Contains(requirement.SkillId))
            {
                messages.Add(new ApiError(
                    "unknown_requirement_skill",
                    $"Skill '{requirement.SkillId}' does not exist.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!seen.Add(requirement.SkillId))
            {
                messages.Add(new ApiError(
                    "duplicate_requirement_skill",
                    $"Skill '{requirement.SkillId}' is required more than once.",
                    ValidationSeverity.Error,
                    field));
            }
            if (requirement.RequiredValue is < 1 or > MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_requirement_value",
                    $"Required skill value must be between 1 and {MaximumMagnitude:N0}.",
                    ValidationSeverity.Error,
                    field));
            }
            if (string.Equals(requirement.SkillId, "strength", StringComparison.Ordinal))
            {
                messages.Add(new ApiError(
                    requirement.RequiredValue == draft.RequiredStrength
                        ? "redundant_strength_requirement"
                        : "conflicting_strength_requirement",
                    requirement.RequiredValue == draft.RequiredStrength
                        ? "The explicit Strength requirement duplicates Required strength. Keep one source of truth by removing the row."
                        : "The explicit Strength requirement conflicts with Required strength.",
                    requirement.RequiredValue == draft.RequiredStrength
                        ? ValidationSeverity.Warning
                        : ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidateModifiers(
        NormalizedEquipmentDraft draft,
        IReadOnlySet<string> knownSkills,
        ICollection<ApiError> messages)
    {
        if (draft.SkillModifiers.Count > MaximumModifiers)
        {
            messages.Add(new ApiError(
                "too_many_equipment_modifiers",
                $"Equipment may have at most {MaximumModifiers} skill modifiers.",
                ValidationSeverity.Error,
                "skill_modifiers"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modifier in draft.SkillModifiers)
        {
            var field = $"skill_modifiers[{modifier.SkillId}]";
            if (!knownSkills.Contains(modifier.SkillId))
            {
                messages.Add(new ApiError(
                    "unknown_modifier_skill",
                    $"Skill '{modifier.SkillId}' does not exist.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!seen.Add(modifier.SkillId))
            {
                messages.Add(new ApiError(
                    "duplicate_modifier_skill",
                    $"Skill '{modifier.SkillId}' is modified more than once.",
                    ValidationSeverity.Error,
                    field));
            }
            if (modifier.ModifierValue is < -MaximumMagnitude or > MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_modifier_value",
                    $"Skill modifier must be between {(-MaximumMagnitude):N0} and {MaximumMagnitude:N0}.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidateBonuses(
        EquipmentCombatBonusDefinition bonuses,
        ICollection<ApiError> messages)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["attack_thrust"] = bonuses.AttackThrust,
            ["attack_slash"] = bonuses.AttackSlash,
            ["attack_crush"] = bonuses.AttackCrush,
            ["attack_ranged"] = bonuses.AttackRanged,
            ["attack_magic"] = bonuses.AttackMagic,
            ["strength_melee"] = bonuses.StrengthMelee,
            ["strength_ranged"] = bonuses.StrengthRanged,
            ["strength_magic"] = bonuses.StrengthMagic,
            ["defence_thrust"] = bonuses.DefenceThrust,
            ["defence_slash"] = bonuses.DefenceSlash,
            ["defence_crush"] = bonuses.DefenceCrush,
            ["defence_ranged"] = bonuses.DefenceRanged,
            ["defence_magic"] = bonuses.DefenceMagic
        };
        foreach (var pair in values)
        {
            if (pair.Value is < -MaximumMagnitude or > MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_combat_bonus",
                    $"Combat bonus '{pair.Key}' must be between {(-MaximumMagnitude):N0} and {MaximumMagnitude:N0}.",
                    ValidationSeverity.Error,
                    $"combat_bonuses.{pair.Key}"));
            }
        }
    }

    private static void ValidateIdentity(
        string itemId,
        string displayName,
        ICollection<ApiError> messages)
    {
        if (string.IsNullOrWhiteSpace(itemId)
            || itemId.Length > 100
            || !StableItemIdRegex().IsMatch(itemId))
        {
            messages.Add(new ApiError(
                "invalid_item_id",
                "Item IDs must be 1-100 lowercase letters, numbers, or single underscores between segments.",
                ValidationSeverity.Error,
                "item_id"));
        }

        var trimmed = displayName.Trim();
        if (trimmed.Length is < 1 or > 100 || trimmed.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "invalid_display_name",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableItemIdRegex();
}

public sealed record NormalizedEquipmentDraft(
    string DisplayName,
    string IconTexturePath,
    bool Equippable,
    string? EquipmentSlotId,
    int RequiredStrength,
    IReadOnlyList<EquipmentSkillRequirementDraft> Requirements,
    IReadOnlyList<EquipmentSkillModifierDraft> SkillModifiers,
    EquipmentCombatBonusDefinition CombatBonuses);

public sealed record EquipmentValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
