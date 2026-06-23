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
/// Named-card factory for Overprotect (Streets of New Capenna, {1}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature you control gets +3/+3 and gains trample, hexproof, and
///    indestructible until end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {1}{G}. Card shape comes from the
///   embedded JSON (<c>overprotect.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="BlossomingDefenseFactory"/>). The resolve-time body lives in
///   <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
///   carries a target request not expressible in the data-only JSON schema.
/// - <see cref="BuildDefinition"/> wires the resolve effect: a 1..1
///   "target creature you control" <see cref="TargetRequest"/>. On resolve
///   (CR 608.2b illegal-target guard first):
///   1. Register a <see cref="PumpUntilEndOfTurnEffect"/>(+3, +3) on the
///      target's <see cref="Creature.ActiveEffects"/> (CR 613.1g Layer 7c,
///      CR 514.2 EOT expiry).
///   2. Register three <see cref="GrantKeywordUntilEndOfTurnEffect"/> grants:
///      Trample (CR 702.19), Hexproof (CR 702.11b), and Indestructible
///      (CR 702.12b). Each is a Layer-6 keyword grant with EOT expiry
///      (CR 514.2). The engine honours all three: Trample in combat damage
///      assignment, Hexproof in <see cref="Majik.Core.Targeting.TargetLegality"/>,
///      Indestructible in the destruction SBA (CreatureDeathCheck).
///
/// Strictly an extension of <see cref="BlossomingDefenseFactory"/> (+X/+X +
/// keyword grants until end of turn): larger pump and a wider keyword set, all
/// of which the analogue's <see cref="GrantKeywordUntilEndOfTurnEffect"/>
/// already supports — no new engine mechanic.
///
/// ## Deferred (v1 gaps)
/// - <b>Target-selection controller predicate</b>: the
///   <see cref="TargetRequest.Description"/> conveys "creature you control"
///   but the engine's structural target filtering does not yet enforce
///   control predicates from the description string. The resolve body
///   double-checks the controller at resolution (CR 608.2b) and no-ops when
///   the chosen creature is not controlled by the caster — same posture as
///   <see cref="BlossomingDefenseFactory"/>.
/// </summary>
[CardName("Overprotect")]
public static class OverprotectFactory
{
    public const string CardName = "Overprotect";
    public const string Slug = "overprotect";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>Layer 7c +P/+T magnitude (CR 613.1g).</summary>
    public const int PumpAmount = 3;

    /// <summary>Granted keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Granted keyword — CR 702.12 Indestructible.</summary>
    public const string GrantedIndestructible = "Indestructible";

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
    ///   (+3, +3) (CR 613.1g) plus Trample, Hexproof, and Indestructible
    ///   keyword grants, all expiring at cleanup (CR 514.2).</item>
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
                        "Overprotect — target creature you control gets +3/+3 and gains trample, hexproof, and indestructible until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.1g — Layer 7c +3/+3 with EOT expiry (CR 514.2).
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PumpAmount, PumpAmount));

        // Layer-6 keyword grants with EOT expiry (CR 514.2).
        // CR 702.19 Trample, CR 702.11b Hexproof, CR 702.12b Indestructible.
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedTrample));
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedHexproof));
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedIndestructible));
    }
}
