using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class MobDefinitionValidator
{
    private static readonly HashSet<string> DraftBlockingValidationCodes = new(StringComparer.Ordinal)
    {
        "invalid_mob_definition_id",
        "mob_definition_id_immutable",
        "invalid_mob_display_name",
        "invalid_mob_visual_texture_path",
        "invalid_mob_visual_dimensions",
        "invalid_mob_visual_anchor",
        "invalid_mob_visual_render_scale",
        "invalid_mob_footprint",
        "invalid_mob_max_health",
        "invalid_mob_movement_speed",
        "unsupported_mob_movement_behavior",
        "invalid_mob_wander_radius",
        "unsupported_mob_aggression_mode",
        "invalid_mob_aggression_radius",
        "invalid_mob_leash_radius",
        "unsupported_mob_return_home_behavior",
        "inconsistent_mob_behavior_radii",
        "invalid_mob_faction",
        "invalid_mob_proactive_targeting",
        "unsupported_mob_attack_type",
        "unsupported_mob_accuracy_style",
        "invalid_mob_attack_range",
        "invalid_mob_attack_speed_units",
        "invalid_mob_combat_level",
        "duplicate_mob_drop_order",
        "duplicate_mob_drop_item",
        "invalid_mob_drop_order",
        "invalid_mob_drop_item_id",
        "invalid_mob_drop_stack_count",
        "invalid_mob_drop_item"
    };

    private readonly MobRepository _repository;
    private readonly ItemAssetService _assetService;

    public MobDefinitionValidator(
        MobRepository repository,
        ItemAssetService assetService)
    {
        _repository = repository;
        _assetService = assetService;
    }

    public async Task<MobValidationOutcome> ValidateAsync(
        string mobDefinitionId,
        NormalizedMobDraft draft,
        MobDefinitionRecord? existing,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(mobDefinitionId, draft, existing, messages);
        ValidateVisuals(draft, messages, forPublication);
        ValidateFootprintHealthMovement(draft, messages);
        ValidateBehavior(draft, messages);

        var factions = await _repository.LoadFactionsAsync(cancellationToken);
        var factionIds = factions.Select(faction => faction.FactionId).ToHashSet(StringComparer.Ordinal);
        ValidateFactionAndTargeting(draft, factionIds, messages);
        ValidateCombatProfile(draft, messages, forPublication);
        ValidateBonuses(draft.CombatBonuses, messages);

        var dropItems = await _repository.LoadDropItemsAsync(cancellationToken);
        var dropItemLookup = dropItems.ToDictionary(item => item.ItemId, StringComparer.Ordinal);
        ValidateGuaranteedDrops(draft, dropItemLookup, messages, forPublication);

        if (existing is not null && existing.PublicationState == "Published" && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish_mob",
                "Saving or disabling this published mob definition changes its authoring lifecycle state, but active runtime export remains a later integration step.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        var asset = _assetService.ResolveGameAssetPng(draft.VisualTexturePath, "mob visual texture");
        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        var hasDraftBlockingErrors = messages.Any(IsDraftBlocking);
        return new MobValidationOutcome(
            !hasDraftBlockingErrors,
            !hasErrors && asset.Exists && draft.PrimaryCombatProfile is not null,
            messages,
            asset.FilePath);
    }

    public static bool IsDraftBlocking(ApiError message) =>
        message.Severity == ValidationSeverity.Error
        && DraftBlockingValidationCodes.Contains(message.Code);

    public static void ValidateIdentity(
        string mobDefinitionId,
        NormalizedMobDraft draft,
        MobDefinitionRecord? existing,
        ICollection<ApiError> messages)
    {
        if (string.IsNullOrWhiteSpace(mobDefinitionId)
            || mobDefinitionId.Length > 100
            || !StableIdentifierRegex().IsMatch(mobDefinitionId))
        {
            messages.Add(new ApiError(
                "invalid_mob_definition_id",
                "Mob definition IDs must be 1-100 lowercase letters, numbers, or single underscores between segments.",
                ValidationSeverity.Error,
                "mob_definition_id"));
        }

        if (existing is not null
            && !string.Equals(existing.MobDefinitionId, mobDefinitionId, StringComparison.Ordinal))
        {
            messages.Add(new ApiError(
                "mob_definition_id_immutable",
                "Mob definition identity is immutable after creation.",
                ValidationSeverity.Error,
                "mob_definition_id"));
        }

        if (draft.DisplayName.Length is < 1 or > 100 || draft.DisplayName.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "invalid_mob_display_name",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }
    }

    public void ValidateVisuals(
        NormalizedMobDraft draft,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        var asset = _assetService.ResolveGameAssetPng(draft.VisualTexturePath, "mob visual texture");
        if (draft.VisualTexturePath.Length == 0)
        {
            messages.Add(new ApiError(
                "invalid_mob_visual_texture_path",
                "Mob visual texture path is required before the draft can be saved.",
                ValidationSeverity.Error,
                "visual_texture_path"));
        }
        else if (!asset.Exists)
        {
            messages.Add(new ApiError(
                "mob_visual_unavailable",
                asset.Message ?? "The mob visual texture is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "visual_texture_path",
                "Use a PNG under the configured game_client_assets root, such as res://assets/maps/objects/mobs/slime.png."));
        }

        ValidateVisualFields(draft, messages);
    }

    public static void ValidateVisualFields(
        NormalizedMobDraft draft,
        ICollection<ApiError> messages)
    {
        if (!MobDomainRules.AreSourceDimensionsValid(draft.SourceWidth, draft.SourceHeight))
        {
            messages.Add(new ApiError(
                "invalid_mob_visual_dimensions",
                "Mob source width and height must be positive.",
                ValidationSeverity.Error,
                "source_width"));
        }
        if (!MobDomainRules.IsFinite(draft.VisualAnchorOffsetX)
            || !MobDomainRules.IsFinite(draft.VisualAnchorOffsetY))
        {
            messages.Add(new ApiError(
                "invalid_mob_visual_anchor",
                "Mob visual anchor offsets must be finite numbers.",
                ValidationSeverity.Error,
                "visual_anchor_offset_x"));
        }
        if (!MobDomainRules.IsPositiveFinite(draft.VisualRenderScale))
        {
            messages.Add(new ApiError(
                "invalid_mob_visual_render_scale",
                "Mob visual render scale must be finite and greater than zero.",
                ValidationSeverity.Error,
                "visual_render_scale"));
        }
    }

    public static void ValidateFootprintHealthMovement(
        NormalizedMobDraft draft,
        ICollection<ApiError> messages)
    {
        if (!MobDomainRules.IsFootprintValid(draft.FootprintWidthTiles, draft.FootprintHeightTiles))
        {
            messages.Add(new ApiError(
                "invalid_mob_footprint",
                "Mob footprint dimensions must be positive logical-tile values.",
                ValidationSeverity.Error,
                "footprint_width_tiles"));
        }
        if (!MobDomainRules.IsLevelSupported(draft.MaxHealth) || draft.MaxHealth <= 0)
        {
            messages.Add(new ApiError(
                "invalid_mob_max_health",
                $"Max health must be between 1 and {MobAuthoringRegistry.MaxMobLevel:N0}.",
                ValidationSeverity.Error,
                "max_health"));
        }
        if (!MobDomainRules.IsPositiveFinite(draft.MovementSpeedTilesPerSecond))
        {
            messages.Add(new ApiError(
                "invalid_mob_movement_speed",
                "Movement speed must be finite and greater than zero.",
                ValidationSeverity.Error,
                "movement_speed_tiles_per_second"));
        }
    }

    public static void ValidateBehavior(
        NormalizedMobDraft draft,
        ICollection<ApiError> messages)
    {
        if (!MobDomainRules.IsSupportedMovementBehavior(draft.MovementBehavior))
        {
            messages.Add(new ApiError(
                "unsupported_mob_movement_behavior",
                "Mob movement behavior must be static or random_wander.",
                ValidationSeverity.Error,
                "movement_behavior"));
        }
        if (!MobDomainRules.IsWanderRadiusSupported(draft.WanderRadiusTiles))
        {
            messages.Add(new ApiError(
                "invalid_mob_wander_radius",
                $"Wander radius must be between 0 and {MobAuthoringRegistry.MaxWanderRadiusTiles} logical tiles.",
                ValidationSeverity.Error,
                "wander_radius_tiles"));
        }
        if (!MobDomainRules.IsSupportedAggressionMode(draft.AggressionMode))
        {
            messages.Add(new ApiError(
                "unsupported_mob_aggression_mode",
                "Mob aggression mode must be passive, retaliatory, or proactive.",
                ValidationSeverity.Error,
                "aggression_mode"));
        }
        if (!MobDomainRules.IsAggressionRadiusSupported(draft.AggressionRadiusTiles))
        {
            messages.Add(new ApiError(
                "invalid_mob_aggression_radius",
                $"Aggression radius must be between 0 and {MobAuthoringRegistry.MaxAggressionRadiusTiles} logical tiles.",
                ValidationSeverity.Error,
                "aggression_radius_tiles"));
        }
        if (!MobDomainRules.IsLeashRadiusSupported(draft.LeashRadiusTiles))
        {
            messages.Add(new ApiError(
                "invalid_mob_leash_radius",
                $"Leash radius must be between 0 and {MobAuthoringRegistry.MaxLeashRadiusTiles} logical tiles.",
                ValidationSeverity.Error,
                "leash_radius_tiles"));
        }
        if (!MobDomainRules.IsSupportedReturnHomeBehavior(draft.ReturnHomeBehavior))
        {
            messages.Add(new ApiError(
                "unsupported_mob_return_home_behavior",
                "Mob return-home behavior must be none or return_to_spawn.",
                ValidationSeverity.Error,
                "return_home_behavior"));
        }
        var hasSupportedMovement = MobDomainRules.IsSupportedMovementBehavior(draft.MovementBehavior)
            && MobDomainRules.IsWanderRadiusSupported(draft.WanderRadiusTiles);
        var hasSupportedAggression = MobDomainRules.IsSupportedAggressionMode(draft.AggressionMode)
            && MobDomainRules.IsAggressionRadiusSupported(draft.AggressionRadiusTiles);
        var hasSupportedLeash = MobDomainRules.IsLeashRadiusSupported(draft.LeashRadiusTiles);

        if (hasSupportedMovement
            && MobDomainRules.NormalizeMovementBehavior(draft.MovementBehavior) == "static"
            && draft.WanderRadiusTiles != 0)
        {
            messages.Add(new ApiError(
                "inconsistent_mob_behavior_radii",
                "Static mobs must use zero wander radius.",
                ValidationSeverity.Error,
                "wander_radius_tiles"));
        }
        if (hasSupportedMovement
            && MobDomainRules.NormalizeMovementBehavior(draft.MovementBehavior) == "random_wander"
            && draft.WanderRadiusTiles <= 0)
        {
            messages.Add(new ApiError(
                "inconsistent_mob_behavior_radii",
                "Random-wander mobs require a positive wander radius.",
                ValidationSeverity.Error,
                "wander_radius_tiles"));
        }
        if (hasSupportedAggression
            && MobDomainRules.NormalizeAggressionMode(draft.AggressionMode) is "passive" or "retaliatory"
            && draft.AggressionRadiusTiles != 0)
        {
            messages.Add(new ApiError(
                "inconsistent_mob_behavior_radii",
                "Passive and retaliatory mobs must use zero aggression radius.",
                ValidationSeverity.Error,
                "aggression_radius_tiles"));
        }
        if (hasSupportedAggression
            && MobDomainRules.NormalizeAggressionMode(draft.AggressionMode) == "proactive"
            && draft.AggressionRadiusTiles <= 0)
        {
            messages.Add(new ApiError(
                "inconsistent_mob_behavior_radii",
                "Proactive mobs require a positive aggression radius.",
                ValidationSeverity.Error,
                "aggression_radius_tiles"));
        }
        if (hasSupportedMovement && hasSupportedAggression && hasSupportedLeash)
        {
            var minimumLeashRadiusTiles = Math.Max(draft.WanderRadiusTiles, draft.AggressionRadiusTiles);
            if (draft.LeashRadiusTiles < minimumLeashRadiusTiles)
            {
                messages.Add(new ApiError(
                    "inconsistent_mob_behavior_radii",
                    $"Leash radius must be at least {minimumLeashRadiusTiles} logical tiles to cover the authored wander radius ({draft.WanderRadiusTiles}) and aggression radius ({draft.AggressionRadiusTiles}).",
                    ValidationSeverity.Error,
                    "leash_radius_tiles"));
            }
        }
    }

    public static void ValidateFactionAndTargeting(
        NormalizedMobDraft draft,
        IReadOnlySet<string> factionIds,
        ICollection<ApiError> messages)
    {
        if (draft.CombatFactionId is not null && !factionIds.Contains(draft.CombatFactionId))
        {
            messages.Add(new ApiError(
                "invalid_mob_faction",
                $"Faction '{draft.CombatFactionId}' does not exist.",
                ValidationSeverity.Error,
                "combat_faction_id"));
        }

        if (!MobDomainRules.IsProactiveTargetingConsistent(
                draft.CanProactivelyTargetHostileMobs,
                draft.CombatFactionId,
                draft.MobDetectionRadiusTiles,
                draft.MobTargetScanIntervalMs,
                draft.MobTargetScanCandidateLimit))
        {
            messages.Add(new ApiError(
                "invalid_mob_proactive_targeting",
                "Proactive hostile-mob targeting requires a faction, detection radius, scan interval, and candidate limit greater than zero.",
                ValidationSeverity.Error,
                "can_proactively_target_hostile_mobs"));
        }
    }

    public static void ValidateCombatProfile(
        NormalizedMobDraft draft,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        if (draft.PrimaryCombatProfile is null)
        {
            if (forPublication)
            {
                messages.Add(new ApiError(
                    "mob_combat_profile_required",
                    "Published mob definitions require a primary melee combat profile.",
                    ValidationSeverity.Error,
                    "primary_combat_profile"));
            }

            return;
        }

        var profile = draft.PrimaryCombatProfile;
        if (!MobDomainRules.IsSupportedAttackType(profile.AttackType))
        {
            messages.Add(new ApiError(
                "unsupported_mob_attack_type",
                "T4 mobs currently support only the melee attack type.",
                ValidationSeverity.Error,
                "primary_combat_profile.attack_type"));
        }
        if (!MobDomainRules.IsSupportedAccuracyStyle(profile.AccuracyStyle))
        {
            messages.Add(new ApiError(
                "unsupported_mob_accuracy_style",
                "Melee mob profiles must use thrust, slash, or crush accuracy style.",
                ValidationSeverity.Error,
                "primary_combat_profile.accuracy_style"));
        }
        if (!MobDomainRules.IsRangeSupported(profile.MinimumRangeTiles, profile.MaximumRangeTiles))
        {
            messages.Add(new ApiError(
                "invalid_mob_attack_range",
                $"Mob attack range must use logical tiles between 0 and {MobAuthoringRegistry.MaxRangeTiles}, with maximum >= minimum.",
                ValidationSeverity.Error,
                "primary_combat_profile.maximum_range_tiles"));
        }
        if (profile.MinimumRangeTiles < (forPublication ? 1 : 0))
        {
            messages.Add(new ApiError(
                "invalid_mob_publication_range",
                "Published mob attack range must be at least one logical tile.",
                ValidationSeverity.Error,
                "primary_combat_profile.minimum_range_tiles"));
        }
        if (!MobDomainRules.IsAttackSpeedSupported(profile.AttackSpeedUnits))
        {
            messages.Add(new ApiError(
                "invalid_mob_attack_speed_units",
                $"Attack speed must be 1-{MobAuthoringRegistry.MaxAttackSpeedUnits} combat units. Each unit is {MobAuthoringRegistry.CombatUnitMilliseconds} milliseconds.",
                ValidationSeverity.Error,
                "primary_combat_profile.attack_speed_units"));
        }
        if (!MobDomainRules.IsLevelSupported(profile.AttackLevel)
            || !MobDomainRules.IsLevelSupported(profile.StrengthLevel)
            || !MobDomainRules.IsLevelSupported(profile.DefenceLevel))
        {
            messages.Add(new ApiError(
                "invalid_mob_combat_level",
                $"Mob combat levels must be between 0 and {MobAuthoringRegistry.MaxMobLevel:N0}.",
                ValidationSeverity.Error,
                "primary_combat_profile.attack_level"));
        }
    }

    public static void ValidateBonuses(
        EquipmentCombatBonusDefinition bonuses,
        ICollection<ApiError> messages)
    {
        foreach (var pair in CombatBonusValues(bonuses))
        {
            if (!MobDomainRules.IsCombatBonusSupported(pair.Value))
            {
                messages.Add(new ApiError(
                    "invalid_mob_combat_bonus",
                    $"Combat bonus '{pair.Key}' must be between {(-MobAuthoringRegistry.MaxCombatBonusMagnitude):N0} and {MobAuthoringRegistry.MaxCombatBonusMagnitude:N0}.",
                    ValidationSeverity.Error,
                    $"combat_bonuses.{pair.Key}"));
            }
        }
    }

    public static void ValidateGuaranteedDrops(
        NormalizedMobDraft draft,
        IReadOnlyDictionary<string, MobDropItemRecord> dropItemLookup,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        if (MobDomainRules.HasDuplicateDropOrders(draft.GuaranteedDrops))
        {
            messages.Add(new ApiError(
                "duplicate_mob_drop_order",
                "Guaranteed drop order values must be unique for a mob.",
                ValidationSeverity.Error,
                "guaranteed_drops"));
        }
        if (MobDomainRules.HasDuplicateDropItems(draft.GuaranteedDrops))
        {
            messages.Add(new ApiError(
                "duplicate_mob_drop_item",
                "The same item cannot be listed more than once in guaranteed mob drops.",
                ValidationSeverity.Error,
                "guaranteed_drops"));
        }

        foreach (var drop in draft.GuaranteedDrops)
        {
            var field = $"guaranteed_drops[{drop.DropOrder}]";
            if (!MobDomainRules.IsDropOrderSupported(drop.DropOrder))
            {
                messages.Add(new ApiError(
                    "invalid_mob_drop_order",
                    $"Drop order must be between 0 and {MobAuthoringRegistry.MaxDropOrder}.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!MobDomainRules.IsStableId(drop.ItemId))
            {
                messages.Add(new ApiError(
                    "invalid_mob_drop_item_id",
                    "Guaranteed drop item IDs must be stable lowercase identifiers.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!MobDomainRules.IsStackCountSupported(drop.StackCount))
            {
                messages.Add(new ApiError(
                    "invalid_mob_drop_stack_count",
                    $"Guaranteed drop stack count must be between 1 and {MobAuthoringRegistry.MaxStackCount:N0}.",
                    ValidationSeverity.Error,
                    field));
            }
            if (!dropItemLookup.TryGetValue(drop.ItemId, out var item))
            {
                messages.Add(new ApiError(
                    "invalid_mob_drop_item",
                    $"Drop item '{drop.ItemId}' does not exist.",
                    ValidationSeverity.Error,
                    field));
                continue;
            }
            if (forPublication && !item.RuntimeEnabled)
            {
                messages.Add(new ApiError(
                    "unpublished_mob_drop_item",
                    $"Drop item '{drop.ItemId}' must be published before the mob can be published.",
                    ValidationSeverity.Error,
                    field));
            }
        }
    }

    private static IReadOnlyDictionary<string, int> CombatBonusValues(
        EquipmentCombatBonusDefinition bonuses) =>
        new Dictionary<string, int>(StringComparer.Ordinal)
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

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierRegex();
}

public sealed record NormalizedMobDraft(
    string DisplayName,
    string VisualTexturePath,
    int SourceWidth,
    int SourceHeight,
    double VisualAnchorOffsetX,
    double VisualAnchorOffsetY,
    double VisualRenderScale,
    int FootprintWidthTiles,
    int FootprintHeightTiles,
    int MaxHealth,
    double MovementSpeedTilesPerSecond,
    string MovementBehavior,
    int WanderRadiusTiles,
    string AggressionMode,
    int AggressionRadiusTiles,
    int LeashRadiusTiles,
    string ReturnHomeBehavior,
    string? CombatFactionId,
    bool CanProactivelyTargetHostileMobs,
    int MobDetectionRadiusTiles,
    int MobTargetScanIntervalMs,
    int MobTargetScanCandidateLimit,
    MobCombatProfileDefinition? PrimaryCombatProfile,
    EquipmentCombatBonusDefinition CombatBonuses,
    IReadOnlyList<MobDropDraft> GuaranteedDrops);

public sealed record MobValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
