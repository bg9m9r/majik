using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Liquimetal Coating (Mirrodin Besieged, {2}; the
/// Modern-legal Scars-of-Mirrodin block printing in 2026 is {3} pre-errata
/// — using the canonical printed mana cost {3} per the task brief).
///
/// Artifact. Oracle text:
///   "{T}: Target permanent becomes an artifact in addition to its other
///    types until end of turn."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {3}, owner / controller).
/// - <b>{T}: Target permanent becomes an artifact until EOT</b> —
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost"/>.Tap.
///   A 1..1 <see cref="TargetRequest"/> for "target permanent" is
///   declared. On resolution the factory reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and, when the choice
///   is a battlefield <see cref="Permanent"/>, registers a Layer 4
///   <see cref="LiquimetalCoatingAddArtifactEffect"/> against the
///   <paramref name="effects"/> service (mirrors Karn's animate-artifact
///   shape). Untargeted or off-battlefield choices resolve as a no-op
///   (CR 608.2b — illegal target → effect does nothing).
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not
///   yet filter targets to "any permanent" — resolution-time guard
///   handles illegal targets (CR 608.2b).
/// - <b>Non-Creature combat math</b>: same posture as Karn's animate —
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> applies
///   the Layer 4 ADD to <see cref="PermanentCharacteristics.Types"/>,
///   which is what downstream rules consumers consult for "is an artifact"
///   gates. Targeting a Creature instance still routes through
///   <see cref="CreatureCharacteristics"/> via the
///   <see cref="ContinuousEffect.Apply(CreatureCharacteristics)"/>
///   override.
/// - <b>No live continuous-effects service</b>: when
///   <paramref name="effects"/> is null the resolution path no-ops (the
///   tap is still applied per cost-payment semantics). Matches the
///   Karn shape-only path.
/// </summary>
[CardName("Liquimetal Coating")]
public static class LiquimetalCoatingFactory
{
    public const string CardName = "Liquimetal Coating";
    public const string Cost = "{3}";

    /// <summary>
    /// Construct Liquimetal Coating with no live runtime wiring. The
    /// activated ability is attached for shape observability; resolution
    /// no-ops on the Layer 4 type-add step (the
    /// <see cref="ContinuousEffectsService"/> is null).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, effects: null);

    /// <summary>
    /// Construct Liquimetal Coating. When <paramref name="effects"/> is
    /// supplied, activating the ability and resolving against a
    /// battlefield <see cref="Permanent"/> target registers a Layer 4
    /// <see cref="LiquimetalCoatingAddArtifactEffect"/> on the target
    /// until end of turn.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var coating = new Artifact(CardName, Cost);
        coating.SetOwner(owner);
        coating.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Target permanent becomes an artifact in addition to its
        // other types until end of turn.
        // CR 602 — ordinary activated ability. CR 613.1d — Layer 4 type-add.
        // ----------------------------------------------------------------
        ActivatedAbility? typeAddAbility = null;
        var typeAddEffect = new Effect(
            $"{CardName}: target permanent becomes an artifact until EOT",
            () =>
            {
                if (coating.Zone != ZoneType.Battlefield) return;
                if (effects == null) return; // shape-only path

                if (typeAddAbility == null) return;
                if (typeAddAbility.ChosenTargets.Count == 0) return;
                if (typeAddAbility.ChosenTargets[0].Count == 0) return;

                if (typeAddAbility.ChosenTargets[0][0] is not Permanent target)
                    return; // CR 608.2b — illegal target → no-op
                if (target.Zone != ZoneType.Battlefield)
                    return; // target left battlefield in response

                effects.Register(new LiquimetalCoatingAddArtifactEffect(target));
            });

        typeAddAbility = new ActivatedAbility(
            source: coating,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(coating),
            },
            effects: new IEffect[] { typeAddEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        coating.AddAbility(typeAddAbility);

        return coating;
    }
}
