using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class UnifiedItemValidator
{
    private static readonly IReadOnlySet<string> UseActions =
        new HashSet<string>(["eat", "drink", "use"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> EffectTypes =
        new HashSet<string>(["restore_resource"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ResourceTargets =
        new HashSet<string>(["health", "concentration", "special"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> RequirementTypes =
        new HashSet<string>(["skill_minimum"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> TradePolicies =
        new HashSet<string>(["tradeable", "untradeable"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> DeathBehaviors =
        new HashSet<string>(["ordinary", "always_keep", "always_destroy", "transform", "reclaim"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ShopPolicies =
        new HashSet<string>(["not_shop_traded", "npc_buys", "npc_sells", "npc_buys_and_sells"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ReclaimPolicies =
        new HashSet<string>(["none", "fixed_cost"], StringComparer.Ordinal);

    private readonly IUnifiedItemRepository _repository;
    private readonly ItemAuthoringRegistry _registry;
    private readonly ItemAssetService _assetService;
    private readonly ActorAppearanceCatalogService _actorAppearanceCatalogService;

    public UnifiedItemValidator(
        IUnifiedItemRepository repository,
        ItemAuthoringRegistry registry,
        ItemAssetService assetService,
        ActorAppearanceCatalogService actorAppearanceCatalogService)
    {
        _repository = repository;
        _registry = registry;
        _assetService = assetService;
        _actorAppearanceCatalogService = actorAppearanceCatalogService;
    }

    public async Task<UnifiedItemValidationOutcome> ValidateAsync(
        string itemId,
        NormalizedItemDraft draft,
        UnifiedItemRecord? existing,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(itemId, draft.DisplayName, messages);
        var asset = _assetService.Resolve(draft.IconTexturePath);
        if (!asset.Exists)
        {
            messages.Add(new ApiError(
                "item_icon_unavailable",
                asset.Message ?? "The item icon is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "icon_texture_path"));
        }

        if (draft.ConsumableBehavior is not null)
        {
            await ValidateConsumableAsync(itemId, draft.ConsumableBehavior, forPublication, messages, cancellationToken);
        }
        if (draft.Equipment is not null)
        {
            await ValidateEquipmentAsync(draft.Equipment, forPublication, messages, cancellationToken);
        }
        await ValidateToolCapabilitiesAsync(draft.ToolCapabilities, messages, cancellationToken);
        await ValidateEconomyLifecycleAsync(itemId, draft.EconomyLifecycle, forPublication, messages, cancellationToken);

        if (existing?.RuntimeEnabled == true && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish",
                "Saving or disabling this published item will make it unavailable after the next game-server restart.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        if (draft.ConsumableBehavior is not null)
        {
            messages.Add(new ApiError(
                "runtime_consumption_integration_pending",
                "Declarative consumable profiles can be authored, but the current MMO game server still uses the older runtime consumption path.",
                forPublication ? ValidationSeverity.Warning : ValidationSeverity.Info,
                "consumable_behavior"));
        }
        if (draft.ToolCapabilities.Count > 0)
        {
            messages.Add(new ApiError(
                "runtime_tool_execution_deferred",
                "MMO Project can resolve possessed tool capabilities, but gathering, processing, durability, and charges remain deferred.",
                ValidationSeverity.Info,
                "tool_capabilities"));
        }

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        return new UnifiedItemValidationOutcome(
            !hasErrors,
            !hasErrors && asset.Exists,
            messages,
            asset.FilePath);
    }

    private async Task ValidateEconomyLifecycleAsync(
        string itemId,
        ItemEconomyLifecycleDraft economy,
        bool forPublication,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (economy.ReferenceValue < 0)
        {
            messages.Add(new ApiError("invalid_reference_value", "Reference value must be non-negative.", ValidationSeverity.Error, "economy_lifecycle.reference_value"));
        }
        ValidatePolicy(economy.TradePolicy ?? string.Empty, TradePolicies, "trade_policy", messages);
        ValidatePolicy(economy.DeathBehavior ?? string.Empty, DeathBehaviors, "death_behavior", messages);
        ValidatePolicy(economy.ShopPolicy ?? string.Empty, ShopPolicies, "shop_policy", messages);
        ValidatePolicy(economy.ReclaimPolicy ?? string.Empty, ReclaimPolicies, "reclaim_policy", messages);
        var isTransform = string.Equals(economy.DeathBehavior, "transform", StringComparison.Ordinal);
        if (isTransform != (economy.DeathTransformItemId is not null))
        {
            messages.Add(new ApiError("invalid_death_transform", "Transform behavior requires a target item ID and every other behavior forbids one.", ValidationSeverity.Error, "economy_lifecycle.death_transform_item_id"));
        }
        if (economy.DeathTransformItemId is not null)
        {
            if (!UnifiedItemDomainRules.StableIdRegex().IsMatch(economy.DeathTransformItemId))
            {
                messages.Add(new ApiError("invalid_death_transform_item_id", "Transform target must use the stable lowercase underscore format.", ValidationSeverity.Error, "economy_lifecycle.death_transform_item_id"));
            }
            else if (string.Equals(itemId, economy.DeathTransformItemId, StringComparison.Ordinal))
            {
                messages.Add(new ApiError("death_transform_self_reference", "An item cannot transform into itself.", ValidationSeverity.Error, "economy_lifecycle.death_transform_item_id"));
            }
            else
            {
                var target = await _repository.LoadReferencedItemAsync(economy.DeathTransformItemId, cancellationToken);
                if (target is null || (forPublication && !target.RuntimeEnabled))
                {
                    messages.Add(new ApiError("death_transform_target_unavailable", "A published transform item must reference an existing published target item.", ValidationSeverity.Error, "economy_lifecycle.death_transform_item_id"));
                }
            }
        }
        var shopValid = economy.ShopPolicy switch
        {
            "not_shop_traded" => economy.NpcBuyPrice is null && economy.NpcSellPrice is null,
            "npc_buys" => economy.NpcBuyPrice is not null && economy.NpcSellPrice is null,
            "npc_sells" => economy.NpcBuyPrice is null && economy.NpcSellPrice is not null,
            "npc_buys_and_sells" => economy.NpcBuyPrice is not null && economy.NpcSellPrice is not null,
            _ => true
        };
        if (!shopValid || economy.NpcBuyPrice < 0 || economy.NpcSellPrice < 0)
        {
            messages.Add(new ApiError("invalid_shop_policy_prices", "Shop policy must exactly match non-negative NPC buy and sell prices.", ValidationSeverity.Error, "economy_lifecycle.shop_policy"));
        }
        var reclaimValid = economy.DeathBehavior == "reclaim"
            ? economy.ReclaimPolicy == "fixed_cost" && economy.ReclaimValue is not null
            : economy.ReclaimPolicy == "none" && economy.ReclaimValue is null;
        if (!reclaimValid || economy.ReclaimValue < 0)
        {
            messages.Add(new ApiError("invalid_reclaim_policy", "Only reclaim behavior may use a non-negative fixed reclaim value.", ValidationSeverity.Error, "economy_lifecycle.reclaim_policy"));
        }
        ValidateReservedPolicyId(economy.ConditionPolicyId, "condition_policy_id", messages);
        ValidateReservedPolicyId(economy.RepairPolicyId, "repair_policy_id", messages);
        if (forPublication && (economy.ConditionPolicyId is not null || economy.RepairPolicyId is not null))
        {
            messages.Add(new ApiError("condition_repair_publication_unsupported", "Condition and repair policies are reserved metadata and cannot be published in V1.", ValidationSeverity.Error, "economy_lifecycle"));
        }
    }

    private static void ValidatePolicy(string value, IReadOnlySet<string> allowed, string field, ICollection<ApiError> messages)
    {
        if (!allowed.Contains(value))
        {
            messages.Add(new ApiError("unknown_" + field, $"Unknown {field} '{value}'.", ValidationSeverity.Error, "economy_lifecycle." + field));
        }
    }

    private static void ValidateReservedPolicyId(string? value, string field, ICollection<ApiError> messages)
    {
        if (value is not null && !UnifiedItemDomainRules.StableIdRegex().IsMatch(value))
        {
            messages.Add(new ApiError("invalid_" + field, "Reserved policy IDs must use the stable lowercase underscore format.", ValidationSeverity.Error, "economy_lifecycle." + field));
        }
    }

    private async Task ValidateConsumableAsync(
        string itemId,
        NormalizedItemConsumableBehavior consumable,
        bool forPublication,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (!UseActions.Contains(consumable.UseAction))
        {
            messages.Add(new ApiError(
                "invalid_use_action",
                "Use action must be eat, drink, or use.",
                ValidationSeverity.Error,
                "consumable_behavior.use_action"));
        }
        if (consumable.ConsumeQuantity is < 1 or > 999)
        {
            messages.Add(new ApiError(
                "invalid_consume_quantity",
                "Consume quantity must be between 1 and 999.",
                ValidationSeverity.Error,
                "consumable_behavior.consume_quantity"));
        }
        if (consumable.CooldownMs is < 0 or > 86_400_000)
        {
            messages.Add(new ApiError(
                "invalid_cooldown_ms",
                "Cooldown must be between 0 and 86,400,000 milliseconds.",
                ValidationSeverity.Error,
                "consumable_behavior.cooldown_ms"));
        }
        ValidateOptionalText(consumable.SuccessMessage, 300, "consumable_behavior.success_message", messages);
        ValidateSemanticId(consumable.UseAnimationId, "consumable_behavior.use_animation_id", messages);
        ValidateSoundPath(consumable.UseSoundResourcePath, messages);
        await ValidateResultItemAsync(itemId, consumable.ResultItemId, forPublication, messages, cancellationToken);
        await ValidateConsumableRequirementsAsync(consumable.Requirements, messages, cancellationToken);
        ValidateConsumableEffects(consumable.Effects, forPublication, messages);
    }

    private async Task ValidateEquipmentAsync(
        NormalizedItemEquipmentMetadata equipment,
        bool forPublication,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        var slots = (await _repository.LoadSlotsAsync(cancellationToken))
            .Select(slot => slot.SlotId)
            .ToHashSet(StringComparer.Ordinal);
        if (!slots.Contains(equipment.EquipmentSlotId))
        {
            messages.Add(new ApiError(
                "invalid_equipment_slot",
                "Equipment metadata must use a known equipment slot.",
                ValidationSeverity.Error,
                "equipment.equipment_slot_id"));
        }
        if (equipment.RequiredStrength is < 1 or > UnifiedItemDomainRules.MaximumMagnitude)
        {
            messages.Add(new ApiError(
                "invalid_required_strength",
                $"Required strength must be between 1 and {UnifiedItemDomainRules.MaximumMagnitude:N0}.",
                ValidationSeverity.Error,
                "equipment.required_strength"));
        }

        var knownSkills = (await _repository.LoadSkillsAsync(cancellationToken))
            .Select(skill => skill.SkillId)
            .ToHashSet(StringComparer.Ordinal);
        ValidateEquipmentRequirements(equipment.Requirements, knownSkills, messages);
        ValidateEquipmentModifiers(equipment.SkillModifiers, knownSkills, messages);
        ValidateBonuses(equipment.CombatBonuses, messages);
        ValidateWeaponProfile(equipment, forPublication, messages);
        ValidateEquippedVisual(equipment.EquippedVisual, forPublication, messages);
    }

    private async Task ValidateToolCapabilitiesAsync(
        IReadOnlyList<ItemToolCapabilityDraft> capabilities,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (capabilities.Count > UnifiedItemDomainRules.MaximumToolCapabilities)
        {
            messages.Add(new ApiError(
                "too_many_tool_capabilities",
                $"Items may have at most {UnifiedItemDomainRules.MaximumToolCapabilities} tool capabilities.",
                ValidationSeverity.Error,
                "tool_capabilities"));
        }

        var knownCapabilities = (await _repository.LoadGatheringCapabilitiesAsync(cancellationToken))
            .Select(skill => skill.SkillId)
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            var field = $"tool_capabilities[{capability.CapabilityId}]";
            if (string.IsNullOrWhiteSpace(capability.CapabilityId)
                || !UnifiedItemDomainRules.StableIdRegex().IsMatch(capability.CapabilityId))
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
            if (capability.PowerTier is < 1 or > UnifiedItemDomainRules.MaximumPowerTier)
            {
                messages.Add(new ApiError(
                    "invalid_tool_power_tier",
                    $"Tool capability power tier must be between 1 and {UnifiedItemDomainRules.MaximumPowerTier:N0}.",
                    ValidationSeverity.Error,
                    field));
            }
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
        if (!UnifiedItemDomainRules.StableIdRegex().IsMatch(resultItemId))
        {
            messages.Add(new ApiError(
                "invalid_result_item_id",
                "Result item ID must use the stable lowercase underscore format.",
                ValidationSeverity.Error,
                "consumable_behavior.result_item_id"));
            return;
        }
        if (string.Equals(itemId, resultItemId, StringComparison.Ordinal))
        {
            messages.Add(new ApiError(
                "result_item_self_reference",
                "A consumable cannot transform into itself.",
                ValidationSeverity.Error,
                "consumable_behavior.result_item_id"));
            return;
        }

        var resultItem = await _repository.LoadReferencedItemAsync(resultItemId, cancellationToken);
        if (resultItem is null)
        {
            messages.Add(new ApiError(
                "result_item_not_found",
                $"Result item '{resultItemId}' does not exist.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "consumable_behavior.result_item_id"));
            return;
        }
        if (forPublication && !resultItem.RuntimeEnabled)
        {
            messages.Add(new ApiError(
                "result_item_not_published",
                $"Result item '{resultItemId}' exists but is not published.",
                ValidationSeverity.Error,
                "consumable_behavior.result_item_id"));
        }
    }

    private async Task ValidateConsumableRequirementsAsync(
        IReadOnlyList<ConsumableRequirementDefinition> requirements,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (requirements.Count > UnifiedItemDomainRules.MaximumConsumableRequirements)
        {
            messages.Add(new ApiError(
                "too_many_consumable_requirements",
                $"A consumable may have at most {UnifiedItemDomainRules.MaximumConsumableRequirements} requirements.",
                ValidationSeverity.Error,
                "consumable_behavior.requirements"));
        }

        var knownSkills = (await _repository.LoadSkillsAsync(cancellationToken))
            .Select(skill => skill.SkillId)
            .ToHashSet(StringComparer.Ordinal);
        var seenSkills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            var field = $"consumable_behavior.requirements[{requirement.RequirementIndex}]";
            if (!RequirementTypes.Contains(requirement.RequirementType))
            {
                messages.Add(new ApiError(
                    "unsupported_consumable_requirement",
                    "Consumables support only skill_minimum requirements.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!knownSkills.Contains(requirement.TargetId))
            {
                messages.Add(new ApiError(
                    "unknown_requirement_skill",
                    $"Skill '{requirement.TargetId}' does not exist.",
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
            if (requirement.MinimumValue is < 1 or > UnifiedItemDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_requirement_minimum",
                    "Minimum skill value must be between 1 and 1,000,000.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidateConsumableEffects(
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
                "consumable_behavior.effects"));
        }
        if (effects.Count > UnifiedItemDomainRules.MaximumConsumableEffects)
        {
            messages.Add(new ApiError(
                "too_many_consumable_effects",
                $"A consumable may have at most {UnifiedItemDomainRules.MaximumConsumableEffects} effects.",
                ValidationSeverity.Error,
                "consumable_behavior.effects"));
        }

        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            var field = $"consumable_behavior.effects[{effect.EffectIndex}]";
            if (!EffectTypes.Contains(effect.EffectType))
            {
                messages.Add(new ApiError(
                    "unsupported_consumable_effect",
                    "Consumables support only restore_resource effects.",
                    ValidationSeverity.Error,
                    field));
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
            if (effect.MinimumAmount is < 1 or > UnifiedItemDomainRules.MaximumMagnitude
                || effect.MaximumAmount is < 1 or > UnifiedItemDomainRules.MaximumMagnitude
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

    private static void ValidateEquipmentRequirements(
        IReadOnlyList<EquipmentSkillRequirementDraft> requirements,
        IReadOnlySet<string> knownSkills,
        ICollection<ApiError> messages)
    {
        if (requirements.Count > UnifiedItemDomainRules.MaximumEquipmentRequirements)
        {
            messages.Add(new ApiError(
                "too_many_equipment_requirements",
                $"Equipment may have at most {UnifiedItemDomainRules.MaximumEquipmentRequirements} skill requirements.",
                ValidationSeverity.Error,
                "equipment.requirements"));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            var field = $"equipment.requirements[{requirement.SkillId}]";
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
            if (requirement.RequiredValue is < 1 or > UnifiedItemDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_requirement_value",
                    "Required skill value must be between 1 and 1,000,000.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static void ValidateEquipmentModifiers(
        IReadOnlyList<EquipmentSkillModifierDraft> modifiers,
        IReadOnlySet<string> knownSkills,
        ICollection<ApiError> messages)
    {
        if (modifiers.Count > UnifiedItemDomainRules.MaximumEquipmentModifiers)
        {
            messages.Add(new ApiError(
                "too_many_equipment_modifiers",
                $"Equipment may have at most {UnifiedItemDomainRules.MaximumEquipmentModifiers} skill modifiers.",
                ValidationSeverity.Error,
                "equipment.skill_modifiers"));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modifier in modifiers)
        {
            var field = $"equipment.skill_modifiers[{modifier.SkillId}]";
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
            if (modifier.ModifierValue is < -UnifiedItemDomainRules.MaximumMagnitude or > UnifiedItemDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_modifier_value",
                    "Skill modifier must be between -1,000,000 and 1,000,000.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private void ValidateWeaponProfile(
        NormalizedItemEquipmentMetadata equipment,
        bool forPublication,
        ICollection<ApiError> messages)
    {
        if (equipment.EquipmentSlotId == ItemAuthoringRegistry.ActiveWeaponSlotId
            && forPublication
            && equipment.WeaponProfile is null)
        {
            messages.Add(new ApiError(
                "right_hand_weapon_profile_required",
                "The current runtime requires every published right_hand item to have an explicit weapon combat profile.",
                ValidationSeverity.Error,
                "equipment.weapon_profile"));
        }
        if (equipment.EquipmentSlotId == "left_hand" && equipment.WeaponProfile is not null && forPublication)
        {
            messages.Add(new ApiError(
                "left_hand_weapon_profile_not_runtime_supported",
                "The current runtime only resolves active weapon combat profiles from right_hand.",
                ValidationSeverity.Error,
                "equipment.equipment_slot_id"));
        }
        if (equipment.WeaponProfile is null)
        {
            return;
        }
        if (equipment.EquipmentSlotId != ItemAuthoringRegistry.ActiveWeaponSlotId)
        {
            messages.Add(new ApiError(
                "weapon_slot_not_runtime_supported",
                "Weapon profiles are currently supported only for right_hand equipment.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "equipment.equipment_slot_id"));
        }

        var profile = equipment.WeaponProfile;
        if (string.IsNullOrWhiteSpace(profile.ProfileId) || profile.ProfileId.Length > 100)
        {
            messages.Add(new ApiError(
                "invalid_weapon_profile_id",
                "Weapon profile ID must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "equipment.weapon_profile.profile_id"));
        }
        if (!_registry.SupportedAttackFamilies.Contains(profile.AttackType))
        {
            messages.Add(new ApiError(
                "unsupported_attack_family",
                $"Attack family '{profile.AttackType}' is not supported.",
                ValidationSeverity.Error,
                "equipment.weapon_profile.attack_type"));
        }
        if (profile.AccuracyStyle is null || !_registry.SupportedAttackStyles.Contains(profile.AccuracyStyle))
        {
            messages.Add(new ApiError(
                "unsupported_attack_style",
                "Melee weapon profiles must use thrust, slash, or crush accuracy style.",
                ValidationSeverity.Error,
                "equipment.weapon_profile.accuracy_style"));
        }
        if (profile.MinimumRangeTiles < (forPublication ? 1 : 0)
            || profile.MaximumRangeTiles < profile.MinimumRangeTiles
            || profile.MaximumRangeTiles > UnifiedItemDomainRules.MaximumRangeTiles)
        {
            messages.Add(new ApiError(
                "invalid_weapon_range",
                "Weapon profiles must use logical tile ranges with maximum >= minimum.",
                ValidationSeverity.Error,
                "equipment.weapon_profile.maximum_range_tiles"));
        }
        if (profile.AttackSpeedUnits is < 1 or > UnifiedItemDomainRules.MaximumAttackSpeedUnits)
        {
            messages.Add(new ApiError(
                "invalid_attack_speed_units",
                $"Attack speed must be stored as 1-{UnifiedItemDomainRules.MaximumAttackSpeedUnits} combat units.",
                ValidationSeverity.Error,
                "equipment.weapon_profile.attack_speed_units"));
        }
    }

    private void ValidateEquippedVisual(
        NormalizedItemEquippedVisual? equippedVisual,
        bool forPublication,
        ICollection<ApiError> messages)
    {
        if (equippedVisual is null)
        {
            return;
        }

        var catalog = _actorAppearanceCatalogService.LoadRigCatalog();
        if (!catalog.Available)
        {
            messages.Add(new ApiError(
                "actor_rig_catalog_unavailable",
                catalog.Message ?? "The canonical actor rig catalog is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "equipment.equipped_visual.rig_id"));
            return;
        }

        if (string.IsNullOrWhiteSpace(equippedVisual.AssetKey)
            || equippedVisual.AssetKey.Length > 100
            || !UnifiedItemDomainRules.StableIdRegex().IsMatch(equippedVisual.AssetKey))
        {
            messages.Add(new ApiError(
                "invalid_equipped_visual_asset_key",
                "Equipped visual asset_key must use the stable lowercase underscore format.",
                ValidationSeverity.Error,
                "equipment.equipped_visual.asset_key"));
        }

        if (string.IsNullOrWhiteSpace(equippedVisual.BindingType)
            || (equippedVisual.BindingType != "rig_layer" && equippedVisual.BindingType != "socket"))
        {
            messages.Add(new ApiError(
                "invalid_equipped_visual_binding_type",
                "Equipped visual binding_type must be rig_layer or socket.",
                ValidationSeverity.Error,
                "equipment.equipped_visual.binding_type"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(equippedVisual.SecondarySocketId))
        {
            messages.Add(new ApiError(
                "secondary_socket_not_supported",
                "secondary_socket_id is reserved for later two-handed support and must remain empty in V1.",
                ValidationSeverity.Error,
                "equipment.equipped_visual.secondary_socket_id"));
        }

        var rig = catalog.Rigs.FirstOrDefault(value => string.Equals(value.RigId, equippedVisual.RigId, StringComparison.Ordinal));
        if (rig is null)
        {
            messages.Add(new ApiError(
                "unknown_equipped_visual_rig",
                $"Equipped visual rig_id '{equippedVisual.RigId}' is not present in the canonical actor rig catalog.",
                ValidationSeverity.Error,
                "equipment.equipped_visual.rig_id"));
            return;
        }

        var knownLayers = rig.Layers.Select(value => value.LayerId).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(equippedVisual.RenderLayerId)
            || !knownLayers.Contains(equippedVisual.RenderLayerId))
        {
            messages.Add(new ApiError(
                "unknown_equipped_visual_render_layer",
                $"Equipped visual render_layer_id '{equippedVisual.RenderLayerId}' is not defined by rig '{rig.RigId}'.",
                ValidationSeverity.Error,
                "equipment.equipped_visual.render_layer_id"));
        }

        if (equippedVisual.BindingType == "rig_layer")
        {
            if (!string.IsNullOrWhiteSpace(equippedVisual.SocketId))
            {
                messages.Add(new ApiError(
                    "rig_layer_socket_not_allowed",
                    "rig_layer equipped visuals must not define socket_id.",
                    ValidationSeverity.Error,
                    "equipment.equipped_visual.socket_id"));
            }
            if (equippedVisual.GripAnchors.Count > 0)
            {
                messages.Add(new ApiError(
                    "rig_layer_grip_anchors_not_allowed",
                    "rig_layer equipped visuals must not define grip anchors.",
                    ValidationSeverity.Error,
                    "equipment.equipped_visual.grip_anchors"));
            }
            ValidateFlipXByPose(equippedVisual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal), messages);
            ValidateHiddenPoses(equippedVisual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal), messages);
            ValidateItemOverGripByPose(equippedVisual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal), messages);
            return;
        }

        var knownSockets = rig.Sockets.Select(value => value.SocketId).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(equippedVisual.SocketId)
            || !knownSockets.Contains(equippedVisual.SocketId))
        {
            messages.Add(new ApiError(
                "unknown_equipped_visual_socket",
                $"Equipped visual socket_id '{equippedVisual.SocketId}' is not defined by rig '{rig.RigId}'.",
                ValidationSeverity.Error,
                "equipment.equipped_visual.socket_id"));
        }

        ValidateGripAnchors(
            equippedVisual.GripAnchors,
            equippedVisual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
            forPublication,
            messages);
        ValidateFlipXByPose(equippedVisual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal), messages);
        ValidateHiddenPoses(equippedVisual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal), messages);
        ValidateItemOverGripByPose(equippedVisual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal), messages);
    }

    private static void ValidateFlipXByPose(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> flipXByPose,
        ICollection<ApiError> messages)
    {
        var expectedDirections = new HashSet<string>(["N", "E", "S", "W"], StringComparer.Ordinal);
        var expectedFrames = new HashSet<string>(["1", "2", "3", "4"], StringComparer.Ordinal);

        foreach (var direction in flipXByPose.Keys)
        {
            if (!expectedDirections.Contains(direction))
            {
                messages.Add(new ApiError(
                    "invalid_equipped_visual_flip_direction",
                    $"Flip direction '{direction}' is not supported.",
                    ValidationSeverity.Error,
                    $"equipment.equipped_visual.flip_x.{direction}"));
                continue;
            }

            foreach (var frame in flipXByPose[direction].Keys)
            {
                if (!expectedFrames.Contains(frame))
                {
                    messages.Add(new ApiError(
                        "invalid_equipped_visual_flip_frame",
                        $"Flip frame '{frame}' is not supported.",
                        ValidationSeverity.Error,
                        $"equipment.equipped_visual.flip_x.{direction}.{frame}"));
                }
            }
        }
    }

    private static void ValidateGripAnchors(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> gripAnchors,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> hiddenPoses,
        bool forPublication,
        ICollection<ApiError> messages)
    {
        var expectedDirections = new HashSet<string>(["N", "E", "S", "W"], StringComparer.Ordinal);
        var expectedFrames = new HashSet<string>(["1", "2", "3", "4"], StringComparer.Ordinal);

        foreach (var direction in gripAnchors.Keys)
        {
            if (!expectedDirections.Contains(direction))
            {
                messages.Add(new ApiError(
                    "invalid_grip_anchor_direction",
                    $"Grip anchor direction '{direction}' is not supported.",
                    ValidationSeverity.Error,
                    $"equipment.equipped_visual.grip_anchors.{direction}"));
                continue;
            }

            foreach (var frame in gripAnchors[direction].Keys)
            {
                if (!expectedFrames.Contains(frame))
                {
                    messages.Add(new ApiError(
                        "invalid_grip_anchor_frame",
                        $"Grip anchor frame '{frame}' is not supported.",
                        ValidationSeverity.Error,
                        $"equipment.equipped_visual.grip_anchors.{direction}.{frame}"));
                }
            }
        }

        if (!forPublication)
        {
            return;
        }

        foreach (var direction in expectedDirections)
        {
            foreach (var frame in expectedFrames)
            {
                var hidden = hiddenPoses.TryGetValue(direction, out var hiddenFrames)
                    && hiddenFrames.TryGetValue(frame, out var isHidden)
                    && isHidden;
                if (hidden)
                {
                    continue;
                }

                if (!gripAnchors.TryGetValue(direction, out var frames))
                {
                    messages.Add(new ApiError(
                        "missing_grip_anchor_direction",
                        $"Socket-bound equipped visuals must define all four directions; '{direction}' is missing.",
                        ValidationSeverity.Error,
                        $"equipment.equipped_visual.grip_anchors.{direction}"));
                    break;
                }

                if (!frames.ContainsKey(frame))
                {
                    messages.Add(new ApiError(
                        "missing_grip_anchor_frame",
                        $"Socket-bound equipped visuals must define frame {frame} for direction {direction}.",
                        ValidationSeverity.Error,
                        $"equipment.equipped_visual.grip_anchors.{direction}.{frame}"));
                }
            }
        }
    }

    private static void ValidateHiddenPoses(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> hiddenPoses,
        ICollection<ApiError> messages)
    {
        var expectedDirections = new HashSet<string>(["N", "E", "S", "W"], StringComparer.Ordinal);
        var expectedFrames = new HashSet<string>(["1", "2", "3", "4"], StringComparer.Ordinal);

        foreach (var direction in hiddenPoses.Keys)
        {
            if (!expectedDirections.Contains(direction))
            {
                messages.Add(new ApiError(
                    "invalid_equipped_visual_hidden_pose_direction",
                    $"Hidden pose direction '{direction}' is not supported.",
                    ValidationSeverity.Error,
                    $"equipment.equipped_visual.hidden_poses.{direction}"));
                continue;
            }

            foreach (var frame in hiddenPoses[direction].Keys)
            {
                if (!expectedFrames.Contains(frame))
                {
                    messages.Add(new ApiError(
                        "invalid_equipped_visual_hidden_pose_frame",
                        $"Hidden pose frame '{frame}' is not supported.",
                        ValidationSeverity.Error,
                        $"equipment.equipped_visual.hidden_poses.{direction}.{frame}"));
                }
            }
        }
    }

    private static void ValidateItemOverGripByPose(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> itemOverGripByPose,
        ICollection<ApiError> messages)
    {
        var expectedDirections = new HashSet<string>(["N", "E", "S", "W"], StringComparer.Ordinal);
        var expectedFrames = new HashSet<string>(["1", "2", "3", "4"], StringComparer.Ordinal);

        foreach (var direction in itemOverGripByPose.Keys)
        {
            if (!expectedDirections.Contains(direction))
            {
                messages.Add(new ApiError(
                    "invalid_equipped_visual_item_over_grip_direction",
                    $"Item-over-grip direction '{direction}' is not supported.",
                    ValidationSeverity.Error,
                    $"equipment.equipped_visual.item_over_grip.{direction}"));
                continue;
            }

            foreach (var frame in itemOverGripByPose[direction].Keys)
            {
                if (!expectedFrames.Contains(frame))
                {
                    messages.Add(new ApiError(
                        "invalid_equipped_visual_item_over_grip_frame",
                        $"Item-over-grip frame '{frame}' is not supported.",
                        ValidationSeverity.Error,
                        $"equipment.equipped_visual.item_over_grip.{direction}.{frame}"));
                }
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
            if (pair.Value is < -UnifiedItemDomainRules.MaximumMagnitude or > UnifiedItemDomainRules.MaximumMagnitude)
            {
                messages.Add(new ApiError(
                    "invalid_combat_bonus",
                    $"Combat bonus '{pair.Key}' must be between -1,000,000 and 1,000,000.",
                    ValidationSeverity.Error,
                    $"equipment.combat_bonuses.{pair.Key}"));
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
            || !UnifiedItemDomainRules.StableIdRegex().IsMatch(itemId))
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

    private static void ValidateOptionalText(
        string? value,
        int maximumLength,
        string field,
        ICollection<ApiError> messages)
    {
        if (value is not null && (value.Length > maximumLength || value.Any(char.IsControl)))
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
        if (value is not null && (value.Length > 100 || !UnifiedItemDomainRules.StableIdRegex().IsMatch(value)))
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
        if (value is not null
            && (value.Length > 300
                || !value.StartsWith("res://assets/", StringComparison.Ordinal)
                || value.Contains("..", StringComparison.Ordinal)))
        {
            messages.Add(new ApiError(
                "invalid_sound_resource_path",
                "Sound resource path must be a contained res://assets/... path.",
                ValidationSeverity.Error,
                "consumable_behavior.use_sound_resource_path"));
        }
    }
}

public sealed record UnifiedItemValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
