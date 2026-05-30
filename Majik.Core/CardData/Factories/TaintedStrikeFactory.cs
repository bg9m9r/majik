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
/// Named-card factory for Tainted Strike (New Phyrexia, {B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gets +1/+0 and gains infect until end of turn. (It
///    deals damage to creatures in the form of -1/-1 counters and to players
///    in the form of poison counters.)"
///
/// ## Implementation
/// Card shape comes from the embedded JSON (<c>tainted-strike.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/>.
///
/// On resolve (CR 608.2b — illegal target → no-op): when the target is still
/// a <see cref="Creature"/> on the battlefield, register two end-of-turn
/// continuous effects (CR 514.2 — both expire in the cleanup step) on the
/// target's <see cref="Creature.ActiveEffects"/>:
///   1. <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) — Layer 7c +P/+T
///      (CR 613.1g).
///   2. <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Infect") — Layer 6
///      keyword grant (CR 613.1f / CR 702.90b).
///
/// Mirrors <see cref="GiantGrowthFactory"/>'s pump-EOT shape, adding the
/// infect keyword grant in the same vein as <see cref="InkmothNexusFactory"/>
/// (which adds the "Infect" marker via its animate effect). As documented on
/// Inkmoth, the engine models Infect's combat-damage replacement
/// (CR 702.90c-d — damage to creatures becomes -1/-1 counters, to players
/// becomes poison counters) via <see cref="InfectDamageReplacement"/>; the
/// keyword marker is the consumer the replacement keys off of, so granting
/// "Infect" is sufficient to wire the mechanic.
/// </summary>
[CardName("Tainted Strike")]
public static class TaintedStrikeFactory
{
    public const string CardName = "Tainted Strike";
    public const string Slug = "tainted-strike";
    public const string PrintedManaCost = "{B}";

    /// <summary>Layer 7c +P magnitude (CR 613.1g) — +1 power.</summary>
    public const int PumpPower = 1;

    /// <summary>Layer 7c +T magnitude (CR 613.1g) — +0 toughness.</summary>
    public const int PumpToughness = 0;

    /// <summary>Keyword granted until end of turn (CR 702.90).</summary>
    public const string GrantedKeyword = "Infect";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "target creature gets +1/+0 and gains infect until end of
    /// turn" <see cref="SpellDefinition"/>. Single 1..1 "target creature"
    /// request, no modes, no X.
    ///
    /// On resolve: validates the target is still a <see cref="Creature"/> on
    /// the Battlefield (CR 608.2b — illegal target → no-op). When valid,
    /// registers a <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) and a
    /// <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Infect") on the
    /// target's <see cref="Creature.ActiveEffects"/> (CR 514.2 — both expire
    /// in cleanup). When ActiveEffects is null (shape-only tests without a
    /// live <see cref="ContinuousEffectsService"/>), the registration no-ops.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Tainted Strike — target creature gets +1/+0 and gains infect until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 514.2 — both effects end during the cleanup step.
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
    }
}
