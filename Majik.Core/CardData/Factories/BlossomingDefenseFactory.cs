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
/// Named-card factory for Blossoming Defense (Kaladesh, {G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature you control gets +2/+2 and gains hexproof until end of
///    turn. (It can't be the target of spells or abilities your opponents
///    control.)"
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {G}. Card shape comes from the
///   embedded JSON (<c>blossoming-defense.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="PlayWithFireFactory"/>). The resolve-time body lives in
///   <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
///   carries a target request not expressible in the data-only JSON schema.
/// - <see cref="BuildDefinition"/> wires the resolve effect: a 1..1
///   "target creature you control" <see cref="TargetRequest"/>. On resolve
///   (CR 608.2b illegal-target guard first):
///   1. Register a <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the
///      target's <see cref="Creature.ActiveEffects"/> (CR 613.1g Layer 7c,
///      CR 514.2 EOT expiry).
///   2. Register a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///      "Hexproof" (CR 702.11b — "can't be the target of spells or abilities
///      your opponents control"). The engine's existing Hexproof handling in
///      <see cref="Majik.Core.Targeting.TargetLegality"/> delivers the
///      printed parenthetical clause. EOT expiry per CR 514.2.
///
/// Mirrors <see cref="VinesOfVastwoodFactory"/>'s hexproof + pump resolve
/// shape, minus the kicker rider: Blossoming Defense always grants both the
/// +2/+2 and Hexproof, on a creature the caster controls.
///
/// ## Deferred (v1 gaps)
/// - <b>Target-selection controller predicate</b>: the
///   <see cref="TargetRequest.Description"/> conveys "creature you control"
///   but the engine's structural target filtering does not yet enforce
///   control predicates from the description string. The resolve body
///   double-checks the controller at resolution (CR 608.2b) and no-ops when
///   the chosen creature is not controlled by the caster — same posture as
///   <see cref="RattlechainsFactory"/>'s "target Spirit you control" rider.
/// </summary>
[CardName("Blossoming Defense")]
public static class BlossomingDefenseFactory
{
    public const string CardName = "Blossoming Defense";
    public const string Slug = "blossoming-defense";
    public const string PrintedManaCost = "{G}";

    /// <summary>Layer 7c +P/+T magnitude (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature you control" request, no X. On resolution:
    /// <list type="bullet">
    /// <item>If the target is no longer a <see cref="Creature"/> on the
    ///   battlefield, the whole effect no-ops (CR 608.2b).</item>
    /// <item>Otherwise register a <see cref="PumpUntilEndOfTurnEffect"/>
    ///   (+2, +2) (CR 613.1g) and a
    ///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
    ///   <see cref="GrantedHexproof"/> (CR 702.11b), both expiring at
    ///   cleanup (CR 514.2).</item>
    /// </list>
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Blossoming Defense — target creature you control gets +2/+2 and gains hexproof until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.1g — Layer 7c +2/+2 with EOT expiry (CR 514.2).
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PumpAmount, PumpAmount));

        // CR 702.11b — Hexproof = "can't be the target of spells or abilities
        // your opponents control". Layer-6 keyword grant with EOT expiry
        // (CR 514.2); honoured by TargetLegality.
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedHexproof));
    }
}
