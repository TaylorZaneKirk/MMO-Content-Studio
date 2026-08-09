using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static partial class RiggedSpriteVisualDescriptorValidator
{
    public static void Validate(
        string visualMode,
        RiggedSpriteVisualDescriptor? descriptor,
        ActorRiggedSpriteCatalogDefinition catalog,
        IReadOnlySet<string> knownItemIds,
        string codePrefix,
        ICollection<ApiError> messages)
    {
        if (visualMode == ActorVisualModes.FlatSprite)
        {
            if (descriptor is not null)
            {
                messages.Add(Error(codePrefix, "flat_sprite definitions must not define composite_visual.", "composite_visual"));
            }

            return;
        }

        if (visualMode != ActorVisualModes.CompositeRig)
        {
            messages.Add(Error(codePrefix, "visual_mode must be flat_sprite or composite_rig.", "visual_mode"));
            return;
        }

        if (descriptor is null)
        {
            messages.Add(Error(codePrefix, "composite_rig definitions require composite_visual.", "composite_visual"));
            return;
        }

        if (descriptor.ContainsLegacyBaseLayers)
        {
            messages.Add(Error(codePrefix, "composite_visual does not support legacy base_layers.", "composite_visual.base_layers"));
            return;
        }

        if (descriptor.SchemaVersion != 1)
        {
            messages.Add(Error(codePrefix, "composite_visual.schema_version must be 1.", "composite_visual.schema_version"));
        }

        if (!catalog.Available)
        {
            messages.Add(Error(codePrefix, catalog.Message ?? "Canonical rigged-sprite catalogs are unavailable.", "composite_visual"));
            return;
        }

        var rig = catalog.Rigs.SingleOrDefault(candidate => candidate.RigId == descriptor.RigId);
        if (rig is null)
        {
            messages.Add(Error(codePrefix, $"Unknown rig_id '{descriptor.RigId}'.", "composite_visual.rig_id"));
            return;
        }

        if (descriptor.CalibrationId is not null)
        {
            var calibration = catalog.Calibrations.SingleOrDefault(candidate => candidate.CalibrationId == descriptor.CalibrationId);
            if (calibration is null)
            {
                messages.Add(Error(codePrefix, $"Unknown calibration_id '{descriptor.CalibrationId}'.", "composite_visual.calibration_id"));
            }
            else if (calibration.RigId != descriptor.RigId)
            {
                messages.Add(Error(codePrefix, "calibration_id must reference the selected rig_id.", "composite_visual.calibration_id"));
            }
        }

        if (descriptor.PosePolicy == "actor_pose")
        {
            if (descriptor.FixedDirection is not null || descriptor.FixedFrame is not null)
            {
                messages.Add(Error(codePrefix, "actor_pose requires fixed_direction and fixed_frame to be null.", "composite_visual.pose_policy"));
            }
        }
        else if (descriptor.PosePolicy == "fixed")
        {
            if (descriptor.FixedDirection is not ("N" or "E" or "S" or "W"))
            {
                messages.Add(Error(codePrefix, "fixed pose_policy requires fixed_direction N, E, S, or W.", "composite_visual.fixed_direction"));
            }

            if (descriptor.FixedFrame is < 1 or > 4)
            {
                messages.Add(Error(codePrefix, "fixed pose_policy requires fixed_frame from 1 through 4.", "composite_visual.fixed_frame"));
            }
        }
        else
        {
            messages.Add(Error(codePrefix, "pose_policy must be actor_pose or fixed.", "composite_visual.pose_policy"));
        }

        foreach (var cosmetic in descriptor.CosmeticItemIds)
        {
            if (string.IsNullOrWhiteSpace(cosmetic.Key) || !StableIdentifierRegex().IsMatch(cosmetic.Key))
            {
                messages.Add(Error(codePrefix, "cosmetic_item_ids keys must be non-empty stable layer IDs.", "composite_visual.cosmetic_item_ids"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(cosmetic.Value) || !StableIdentifierRegex().IsMatch(cosmetic.Value))
            {
                messages.Add(Error(codePrefix, "cosmetic_item_ids values must be non-empty stable item IDs.", $"composite_visual.cosmetic_item_ids.{cosmetic.Key}"));
                continue;
            }

            if (!rig.Layers.Any(layer => layer.LayerId == cosmetic.Key))
            {
                messages.Add(Error(codePrefix, $"Unknown rig render layer '{cosmetic.Key}'.", $"composite_visual.cosmetic_item_ids.{cosmetic.Key}"));
                continue;
            }

            if (!knownItemIds.Contains(cosmetic.Value))
            {
                messages.Add(Error(codePrefix, $"Unknown published cosmetic item '{cosmetic.Value}'.", $"composite_visual.cosmetic_item_ids.{cosmetic.Key}"));
                continue;
            }

            var equippedVisual = catalog.EquippedVisuals.SingleOrDefault(visual => visual.ItemId == cosmetic.Value);
            if (equippedVisual is null)
            {
                messages.Add(Error(codePrefix, $"Published cosmetic item '{cosmetic.Value}' has no equipped visual.", $"composite_visual.cosmetic_item_ids.{cosmetic.Key}"));
                continue;
            }

            if (equippedVisual.RigId != descriptor.RigId || equippedVisual.RenderLayerId != cosmetic.Key)
            {
                messages.Add(Error(codePrefix, "Cosmetic equipped visual must match the selected rig and render layer.", $"composite_visual.cosmetic_item_ids.{cosmetic.Key}"));
            }

            if (equippedVisual.BindingType != "socket")
            {
                messages.Add(Error(codePrefix, "Solid rigged actors support only socket-bound cosmetic visuals.", $"composite_visual.cosmetic_item_ids.{cosmetic.Key}"));
            }
        }
    }

    private static ApiError Error(string codePrefix, string message, string field) => new(
        $"{codePrefix}_invalid_composite_visual",
        message,
        ValidationSeverity.Error,
        field);

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierRegex();
}
