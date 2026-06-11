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
/// Named-card factory for Distemper of the Blood (Torment, {R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Target creature gets +2/+2 and gains trample until end of turn.
///    Madness {R}"
///
/// ## Implemented (v1)
/// - Sorcery card with printed mana cost {R}. Card shape comes from the
///   embedded JSON (<c>distemper-of-the-blood.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="BlossomingDefenseFactory"/>). The resolve-time body lives in
///   <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
///   carries a target request not expressible in the data-only JSON schema.
/// - <see cref="BuildDefinition"/> wires the resolve effect: a 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolve
///   (CR 608.2b illegal-target guard first):
///   1. Register a <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the
///      target's <see cref="Creature.ActiveEffects"/> (CR 613.1g Layer 7c,
///      CR 514.2 EOT expiry).
///   2. Register a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///      "Trample" (CR 702.19). Layer-6 keyword grant with EOT expiry
///      (CR 514.2).
///
/// Mirrors <see cref="BlossomingDefenseFactory"/>'s +2/+2 + keyword-grant
/// resolve shape; the only differences are the spell type (Sorcery), the
/// granted keyword (Trample rather than Hexproof), and that the target is
/// any creature (not "you control").
///
/// ## Madness (intrinsic — NOT wired here)
/// "Madness {R}" (CR 702.35) is handled centrally by
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> + the discard funnel
/// <c>Fx.DiscardCard</c>; the card is catalogued there (name → {R}), so the
/// alternative-cast-on-discard path needs no factory code.
/// </summary>
[CardName("Distemper of the Blood")]
public static class DistemperOfTheBloodFactory
{
    public const string CardName = "Distemper of the Blood";
    public const string Slug = "distemper-of-the-blood";
    public const string PrintedManaCost = "{R}";

    /// <summary>Layer 7c +P/+T magnitude (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>Granted keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request, no X. On resolution:
    /// <list type="bullet">
    /// <item>If the target is no longer a <see cref="Creature"/> on the
    ///   battlefield, the whole effect no-ops (CR 608.2b).</item>
    /// <item>Otherwise register a <see cref="PumpUntilEndOfTurnEffect"/>
    ///   (+2, +2) (CR 613.1g) and a
    ///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
    ///   <see cref="GrantedTrample"/> (CR 702.19), both expiring at
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
                    "target creature",
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
                        "Distemper of the Blood — target creature gets +2/+2 and gains trample until end of turn",
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

        // CR 702.19 — Trample. Layer-6 keyword grant with EOT expiry
        // (CR 514.2).
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedTrample));
    }
}
