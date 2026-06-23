using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Whispersilk Cloak (Mirrodin et al., {3}).
///
/// Artifact — Equipment. Oracle text (verified against Scryfall 2026-06-23):
///   "Equipped creature can't be blocked and has shroud. (It can't be the
///    target of spells or abilities.)"
///   "Equip {2}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has NO
/// equip ability and NO attached keyword / combat-restriction grant — a JSON
/// def alone produces only a vanilla Artifact shell. The functioning equipment
/// analogues (<see cref="LightningGreavesFactory"/>,
/// <see cref="SwiftfootBootsFactory"/>, <see cref="LavaspurBootsFactory"/>) are
/// hand-rolled for exactly this reason, so Whispersilk Cloak follows that
/// established pattern. The shipped <c>whispersilk-cloak.json</c> carries the
/// base shape only; the abilities are layered here.
///
/// ## Implementation
///
/// - <b>"has shroud"</b> (CR 702.18) — a Layer-6 <see cref="GrantAbilityEffect"/>
///   (CR 613.1f) projecting a <see cref="KeywordAbility"/>("Shroud") onto the
///   live equipped creature. The selector reads
///   <see cref="Permanent.AttachedTo"/> at sync time, so re-equipping transfers
///   the grant and LTB / detach revoke it.
///   <see cref="Majik.Core.Targeting.TargetLegality"/> reads "Shroud" off the
///   bearer's computed keyword set and rejects it as a target for ANY spell or
///   ability — including the controller's own. Identical shape to
///   <see cref="LightningGreavesFactory"/>'s shroud grant.
/// - <b>"can't be blocked"</b> (CR 509.1c) — a predicate-mode
///   <see cref="CombatRestrictionEffect"/> registered with
///   <see cref="CombatRestriction.CannotBeBlocked"/>. The predicate gates on
///   "is the currently equipped creature" so the restriction tracks
///   re-attachment; <c>IsActiveGate</c> ties it to the Cloak being on the
///   battlefield AND attached to a battlefield permanent. Same combat-restriction
///   surface as <see cref="SteelOfTheGodheadFactory"/>'s blue clause, minus the
///   colour condition (Whispersilk Cloak is unconditional). The validator
///   consults this via <see cref="ContinuousEffectsService.HasRestriction"/>.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the Puresteel
///   zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The shroud / can't-be-blocked grants are not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg overload
/// to wire the continuous effects; each grant gates on the Cloak being on the
/// battlefield AND attached to a battlefield permanent.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first controller-side
///   creature deterministically (same gap as the rest of the equipment cycle).
/// </summary>
[CardName("Whispersilk Cloak")]
public static class WhispersilkCloakFactory
{
    public const string CardName = "Whispersilk Cloak";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "whispersilk-cloak";

    public const string Cost = "{3}";

    /// <summary>CR 702.6 — printed equip cost: {2}.</summary>
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Whispersilk Cloak with no live continuous-effects wiring (the
    /// shape / dispatcher path). Neither the shroud nor the can't-be-blocked
    /// grant is registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Whispersilk Cloak. When <paramref name="continuousEffects"/>
    /// is supplied the shroud (CR 702.18) keyword grant and the can't-be-blocked
    /// (CR 509.1c) combat restriction are registered against it; each gates on
    /// the Cloak being on the battlefield AND attached to a battlefield
    /// permanent. When null, both are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Artifact card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Artifact but got "
                + $"'{built.GetType().Name}'.");
        }

        if (continuousEffects != null)
        {
            // CR 702.18 — grant Shroud. Layer-6 ability grant (CR 613.1f)
            // re-projected onto the live equipped creature; TargetLegality reads
            // "Shroud" off the bearer's computed keyword set and rejects it as a
            // target for any spell or ability, including the controller's own.
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("Shroud", bearer, bearer.Controller ?? owner)));

            // CR 509.1c — "can't be blocked." Predicate-mode restriction gating
            // on "is the currently equipped creature" so it tracks
            // re-attachment. IsActiveGate ties the restriction to the Cloak being
            // on the battlefield AND attached to a battlefield permanent.
            continuousEffects.Register(new CombatRestrictionEffect(
                CombatRestriction.CannotBeBlocked,
                predicate: c => ReferenceEquals(card.AttachedTo, c),
                isActiveGate: () =>
                    card.Zone == Majik.Core.Zones.ZoneType.Battlefield
                    && card.AttachedTo is { Zone: Majik.Core.Zones.ZoneType.Battlefield },
                expiresAtEndOfTurn: false));
        }

        // --------------------------------------------------------------
        // Equip {2} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
