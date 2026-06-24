using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Get a Leg Up (Bloomburrow, {G}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Until end of turn, target creature gets +1/+1 for each creature you
///    control and gains reach."
///
/// Get a Leg Up composes two shapes the engine already supports onto a single
/// "target creature" request (CR 601 — one 1..1 target):
/// - The <b>count-scaled pump</b> mirrors <see cref="DistortionStrikeFactory"/>'s
///   single-target <see cref="PumpUntilEndOfTurnEffect"/>, except the magnitude
///   is "+1/+1 for each creature you control" — a count snapshotted once at
///   resolution (CR 608.2 — the spell's effect is locked in when it resolves;
///   creatures entering or leaving afterward do not change the bonus). N =
///   number of creatures the caster controls on the battlefield, so the target
///   gains +N/+N (CR 613.1g layer 7c; CR 514.2 — expires in cleanup). Same
///   count-then-pump idiom as <see cref="InspiringCallFactory"/>'s counted set.
/// - The <b>reach grant</b> mirrors <see cref="AtarkasCommandFactory"/>'s
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>(c, "Reach") — CR 702.17
///   Reach, CR 613.1d layer 6 keyword grant, CR 514.2 cleanup expiry.
///
/// Card shape (name / Instant / {G}) comes from the embedded JSON
/// (<c>get-a-leg-up.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema) — same posture as
/// <see cref="DistortionStrikeFactory"/>.
///
/// On resolution both clauses share a single resolution effect (CR 608.2e) and
/// both no-op when the target is no longer a creature on the battlefield
/// (CR 608.2b — an illegal target fizzles). The caster's creature count is read
/// at the moment of resolution; if the target is one of the caster's creatures
/// it is included in its own count (CR 109.5 — it is a creature the caster
/// controls), matching the printed "for each creature you control".
/// </summary>
[CardName("Get a Leg Up")]
public static class GetALegUpFactory
{
    public const string CardName = "Get a Leg Up";
    public const string Slug = "get-a-leg-up";
    public const string PrintedManaCost = "{G}";

    /// <summary>Keyword granted until end of turn (CR 702.17).</summary>
    public const string GrantedKeyword = "Reach";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Get a Leg Up is cast.
    /// Single 1..1 "target creature" request, no modes, no X. On resolution:
    /// pump the target +N/+N until end of turn where N = the number of creatures
    /// the caster controls (CR 608.2 — counted once at resolution), and grant it
    /// reach until end of turn (CR 702.17 / CR 514.2).
    /// </summary>
    /// <param name="caster">The spell's controller — owner of the "creatures you
    /// control" count read at resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Get a Leg Up: target creature gets +1/+1 for each creature you control and gains reach until end of turn",
                        () => ApplyToTarget(caster, target)),
                };
            });
    }

    private static void ApplyToTarget(Player caster, object target)
    {
        // CR 608.2b — the effect applies only while the target is still a
        // creature on the battlefield; otherwise the spell fizzles (no-op).
        if (target is not Creature creature) return;
        if (creature.Zone != ZoneType.Battlefield) return;
        if (creature.ActiveEffects == null) return;

        // CR 608.2 — "+1/+1 for each creature you control" is read once at
        // resolution. N = creatures the caster controls on the battlefield. The
        // target is counted in its own bonus when it is one of the caster's
        // creatures (CR 109.5).
        var creatureCount = caster.Zones.Battlefield
            .GetCards()
            .OfType<Creature>()
            .Count(c => c.Zone == ZoneType.Battlefield);

        // CR 613.1g layer 7c — +N/+N; CR 514.2 — until end of turn.
        creature.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(creature, creatureCount, creatureCount));

        // CR 702.17 / CR 613.1d layer 6 — grant reach until end of turn
        // (CR 514.2 cleanup expiry).
        creature.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
    }
}
