using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class HandEquipmentItemValidator
{
    private readonly HandEquipmentRepository _repository;
    private readonly HandEquipmentAuthoringRegistry _registry;
    private readonly ItemAssetService _assetService;

    public HandEquipmentItemValidator(
        HandEquipmentRepository repository,
        HandEquipmentAuthoringRegistry registry,
        ItemAssetService assetService)
    {
        _repository = repository;
        _registry = registry;
        _assetService = assetService;
    }

    public async Task<HandEquipmentValidationOutcome> ValidateAsync(
        string itemId,
        NormalizedHandEquipmentDraft draft,
        HandEquipmentItemRecord existing,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(itemId, draft.DisplayName, messages);

        if (existing.HasConsumableProfile)
        {
            messages.Add(new ApiError(
                "wrong_authoring_workspace",
                "Consumable items cannot be edited by Weapons and Tools.",
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
            await ValidateEquippableAsync(draft, messages, forPublication, cancellationToken);
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
            "derived_hand_equipment_visual_key",
            "The current game client derives hand-equipment visual keys from display name and slot; direct visual asset overrides remain deferred.",
            ValidationSeverity.Info,
            "display_name"));

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        return new HandEquipmentValidationOutcome(
            !hasErrors,
            !hasErrors && asset.Exists,
            messages,
            asset.FilePath);
    }

    private async Task ValidateEquippableAsync(
        NormalizedHandEquipmentDraft draft,
        ICollection<ApiError> messages,
        bool forPublication,
        CancellationToken cancellationToken)
    {
        var slots = (await _repository.LoadSlotsAsync(cancellationToken))
            .Select(slot => slot.SlotId)
            .ToHashSet(StringComparer.Ordinal);
        if (draft.EquipmentSlotId is null || !slots.Contains(draft.EquipmentSlotId))
        {
            messages.Add(new ApiError(
                "invalid_equipment_slot",
                "Equippable hand equipment must use a known equipment slot.",
                ValidationSeverity.Error,
                "equipment_slot_id"));
        }

        var isHandSlot = EquipmentItemRepository.IsHandSlot(draft.EquipmentSlotId);
        if (!isHandSlot && (draft.WeaponProfile is not null || draft.ToolCapabilities.Count > 0))
        {
            messages.Add(new ApiError(
                "non_hand_specialization",
                "Only right_hand and left_hand equipment may carry weapon profiles or tool capabilities.",
                ValidationSeverity.Error,
                "equipment_slot_id",
                "Changing a hand item to a wearable slot clears weapon and tool specialization rows."));
        }

        if (draft.RequiredStrength is < 1 or > HandEquipmentDomainRules.MaximumMagnitude)
        {
            messages.Add(new ApiError(
                "invalid_required_strength",
                $"Required strength must be between 1 and {HandEquipmentDomainRules.MaximumMagnitude:N0}.",
                ValidationSeverity.Error,
                "required_strength"));
        }

        var knownSkills = (await _repository.LoadSkillsAsync(cancellationToken))
            .Select(skill => skill.SkillId)
            .ToHashSet(StringComparer.Ordinal);
        var knownCapabilities = (await _repository.LoadGatheringCapabilitiesAsync(cancellationToken))
            .Select(skill => skill.SkillId)
            .ToHashSet(StringComparer.Ordinal);
        ValidateRequirements(draft, knownSkills, messages);
        ValidateModifiers(draft, knownSkills, messages);
        ValidateBonuses(draft.CombatBonuses, messages);
        ValidateWeaponProfile(draft, messages, forPublication);
        ValidateToolCapabilities(draft, knownCapabilities, messages);

        if (forPublication)
        {
            ValidatePublication(draft, messages);
        }
    }

    private static void ValidateNotEquippable(
        NormalizedHandEquipmentDraft draft,
        HandEquipmentItemRecord existing,
        ICollection<ApiError> messages)
    {
        if (draft.EquipmentSlotId is not null
            || draft.RequiredStrength != 1
            || draft.Requirements.Count != 0
            || draft.SkillModifiers.Count != 0
            || draft.WeaponProfile is not null
            || !draft.CombatBonuses.IsZero
            || draft.ToolCapabilities.Count != 0)
        {
            messages.Add(new ApiError(
                "non_equippable_metadata_not_empty",
                "Not-equippable items cannot retain a slot, requirements, modifiers, combat bonuses, weapon profile, or tool capabilities.",
                ValidationSeverity.Error,
                "equippable"));
        }

        if (HandEquipmentRepository.HasHandMetadata(existing))
        {
            messages.Add(new ApiError(
                "hand_equipment_metadata_will_be_removed",
                "Applying this draft will remove the equipment slot, strength gate, requirements, modifiers, combat profile, combat bonuses, and tool capabilities.",
                ValidationSeverity.Warning,
                "equippable"));
        }
    }

    private static void ValidateRequirements(
        NormalizedHandEquipmentDraft draft,
        IReadOnlySet<string> knownSkills,
        ICollection<ApiError> messages)
    {
        if (draft.Requirements.Count > HandEquipmentDomainRules.MaximumRequirements)
        {
            messages.Add(new ApiError(
                "too_many_equipment_requirements",
                $"Hand equipment may have at most {HandEquipmentDomainRules.MaximumRequirements} skill requirements.",
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
            if (requirement.RequiredValue is < 1 or > HandEquipmentDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_requirement_value",
                    $"Required skill value must be between 1 and {HandEquipmentDomainRules.MaximumMagnitude:N0}.",
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
        NormalizedHandEquipmentDraft draft,
        IReadOnlySet<string> knownSkills,
        ICollection<ApiError> messages)
    {
        if (draft.SkillModifiers.Count > HandEquipmentDomainRules.MaximumModifiers)
        {
            messages.Add(new ApiError(
                "too_many_equipment_modifiers",
                $"Hand equipment may have at most {HandEquipmentDomainRules.MaximumModifiers} skill modifiers.",
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
            if (modifier.ModifierValue is < -HandEquipmentDomainRules.MaximumMagnitude or > HandEquipmentDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_modifier_value",
                    $"Skill modifier must be between {(-HandEquipmentDomainRules.MaximumMagnitude):N0} and {HandEquipmentDomainRules.MaximumMagnitude:N0}.",
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
            if (pair.Value is < -HandEquipmentDomainRules.MaximumMagnitude or > HandEquipmentDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_combat_bonus",
                    $"Combat bonus '{pair.Key}' must be between {(-HandEquipmentDomainRules.MaximumMagnitude):N0} and {HandEquipmentDomainRules.MaximumMagnitude:N0}.",
                    ValidationSeverity.Error,
                    $"combat_bonuses.{pair.Key}"));
            }
        }
    }

    private void ValidateWeaponProfile(
        NormalizedHandEquipmentDraft draft,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        if (draft.WeaponProfile is null)
        {
            return;
        }

        var profile = draft.WeaponProfile;
        if (string.IsNullOrWhiteSpace(profile.ProfileId) || profile.ProfileId.Length > 100)
        {
            messages.Add(new ApiError(
                "invalid_weapon_profile_id",
                "Weapon profile ID must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "weapon_profile.profile_id"));
        }
        if (!_registry.SupportedAttackFamilies.Contains(profile.AttackType))
        {
            messages.Add(new ApiError(
                "unsupported_attack_family",
                $"Attack family '{profile.AttackType}' is not supported by the current runtime profile table.",
                ValidationSeverity.Error,
                "weapon_profile.attack_type"));
        }
        if (profile.AccuracyStyle is null || !_registry.SupportedAttackStyles.Contains(profile.AccuracyStyle))
        {
            messages.Add(new ApiError(
                "unsupported_attack_style",
                "Melee weapon profiles must use thrust, slash, or crush accuracy style.",
                ValidationSeverity.Error,
                "weapon_profile.accuracy_style"));
        }
        if (profile.MinimumRangeTiles < (forPublication ? 1 : 0)
            || profile.MaximumRangeTiles < profile.MinimumRangeTiles
            || profile.MaximumRangeTiles > HandEquipmentDomainRules.MaximumRangeTiles)
        {
            messages.Add(new ApiError(
                "invalid_weapon_range",
                forPublication
                    ? "Published weapon profiles must have a range of at least one logical tile and no more than 32 logical tiles."
                    : "Draft weapon profiles must use logical tile ranges between 0 and 32, with maximum >= minimum.",
                ValidationSeverity.Error,
                "weapon_profile.maximum_range_tiles"));
        }
        if (profile.AttackSpeedUnits is < 1 or > HandEquipmentDomainRules.MaximumAttackSpeedUnits)
        {
            messages.Add(new ApiError(
                "invalid_attack_speed_units",
                $"Attack speed must be stored as 1-{HandEquipmentDomainRules.MaximumAttackSpeedUnits} combat units. Each unit is {HandEquipmentAuthoringRegistry.CombatUnitMilliseconds} milliseconds.",
                ValidationSeverity.Error,
                "weapon_profile.attack_speed_units"));
        }
    }

    private void ValidateToolCapabilities(
        NormalizedHandEquipmentDraft draft,
        IReadOnlySet<string> knownCapabilities,
        ICollection<ApiError> messages)
    {
        if (draft.ToolCapabilities.Count > HandEquipmentDomainRules.MaximumToolCapabilities)
        {
            messages.Add(new ApiError(
                "too_many_tool_capabilities",
                $"Hand equipment may have at most {HandEquipmentDomainRules.MaximumToolCapabilities} tool capabilities.",
                ValidationSeverity.Error,
                "tool_capabilities"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in draft.ToolCapabilities)
        {
            var field = $"tool_capabilities[{capability.CapabilityId}]";
            if (string.IsNullOrWhiteSpace(capability.CapabilityId) || !StableIdentifierRegex().IsMatch(capability.CapabilityId))
            {
                messages.Add(new ApiError(
                    "invalid_tool_capability",
                    "Tool capability IDs must be lowercase identifiers.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!_registry.IsKnownToolCapability(capability.CapabilityId, knownCapabilities))
            {
                messages.Add(new ApiError(
                    "unknown_tool_capability",
                    $"Tool capability '{capability.CapabilityId}' is not registered.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!seen.Add(capability.CapabilityId))
            {
                messages.Add(new ApiError(
                    "duplicate_tool_capability",
                    $"Tool capability '{capability.CapabilityId}' appears more than once.",
                    ValidationSeverity.Error,
                    field));
            }
            if (capability.PowerTier is < 1 or > HandEquipmentDomainRules.MaximumPowerTier)
            {
                messages.Add(new ApiError(
                    "invalid_tool_power_tier",
                    $"Tool capability power tier must be between 1 and {HandEquipmentDomainRules.MaximumPowerTier:N0}.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidatePublication(
        NormalizedHandEquipmentDraft draft,
        ICollection<ApiError> messages)
    {
        if (!EquipmentItemRepository.IsHandSlot(draft.EquipmentSlotId))
        {
            messages.Add(new ApiError(
                "not_hand_equipment",
                "Weapons and Tools can only publish right_hand or left_hand equipment.",
                ValidationSeverity.Error,
                "equipment_slot_id"));
        }
        if (draft.EquipmentSlotId == HandEquipmentAuthoringRegistry.ActiveWeaponSlotId
            && draft.WeaponProfile is null)
        {
            messages.Add(new ApiError(
                "right_hand_weapon_profile_required",
                "The current runtime requires every published right_hand item to have an explicit weapon combat profile.",
                ValidationSeverity.Error,
                "weapon_profile"));
        }
        if (draft.EquipmentSlotId == "left_hand" && draft.WeaponProfile is not null)
        {
            messages.Add(new ApiError(
                "left_hand_weapon_profile_not_runtime_supported",
                "The current runtime only resolves active weapon combat profiles from right_hand.",
                ValidationSeverity.Error,
                "equipment_slot_id"));
        }
    }

    private static void ValidateIdentity(
        string itemId,
        string displayName,
        ICollection<ApiError> messages)
    {
        if (string.IsNullOrWhiteSpace(itemId)
            || itemId.Length > 100
            || !StableIdentifierRegex().IsMatch(itemId))
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
    private static partial Regex StableIdentifierRegex();
}

public sealed record NormalizedHandEquipmentDraft(
    string DisplayName,
    string IconTexturePath,
    bool Equippable,
    string? EquipmentSlotId,
    int RequiredStrength,
    IReadOnlyList<EquipmentSkillRequirementDraft> Requirements,
    IReadOnlyList<EquipmentSkillModifierDraft> SkillModifiers,
    EquipmentCombatProfileDefinition? WeaponProfile,
    EquipmentCombatBonusDefinition CombatBonuses,
    IReadOnlyList<HandEquipmentToolCapabilityDraft> ToolCapabilities);

public sealed record HandEquipmentValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
