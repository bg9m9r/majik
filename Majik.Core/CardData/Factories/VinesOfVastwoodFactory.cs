using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vines of Vastwood (Zendikar, {G}).
///
/// Instant. Oracle text:
///   "Kicker {G} (You may pay an additional {G} as you cast this spell.)
///    Choose target creature an opponent doesn't control. That creature
///    can't be the target of spells or abilities your opponents control
///    this turn. If this spell was kicked, that creature gets +4/+4 until
///    end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {G}. CR 702.33 — Kicker is
///   modelled as an <see cref="IAdditionalCost"/> primitive
///   (<see cref="KickerAdditionalCost"/>). The factory exposes
///   <see cref="BuildAdditionalCost"/> for callers (tests, bot decision
///   layer) and is registered with
///   <see cref="Majik.Core.Players.Agents.KickerAltCostProbe.DefaultLookup"/>
///   so the bot's kicker probe recognises Vines of Vastwood by name.
/// - "Kicker" keyword marker attached via <see cref="CardDefBuilder.WithKeyword"/>
///   for shape observability (same posture as Burst Lightning's kicker shape).
/// - <see cref="BuildDefinition"/> wires the resolve body:
///   1..1 "target creature an opponent doesn't control"
///   <see cref="TargetRequest"/>. On resolve:
///   1. CR 608.2b — if the target isn't a Creature on the battlefield, the
///      whole effect no-ops.
///   2. Grant "Hexproof" until end of turn (CR 702.11b — Hexproof =
///      "can't be the target of spells or abilities your opponents control").
///      The engine's printed oracle wording resolves through
///      <see cref="Majik.Core.Targeting.TargetLegality"/>, which honours
///      the Hexproof keyword. Registered as a
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
///      <see cref="Creature.ActiveEffects"/>.
///   3. If the spell was kicked (read via <see cref="Card.WasKicked"/> on
///      the passed-in card reference, CR 702.33b — checked at resolve
///      time), additionally register a <see cref="PumpUntilEndOfTurnEffect"/>
///      (+4, +4) on the same target.
///
/// <para>The Hexproof grant is a slight strengthening of the printed
/// wording: Hexproof on the target also blocks opponent-controlled
/// abilities from non-spell sources (e.g. activated abilities of
/// opponents' permanents), which is exactly what Vines' "can't be the
/// target of spells or abilities your opponents control" expresses.</para>
///
/// ## Deferred (v1 gaps)
/// - <b>Target-selection controller predicate</b>: the
///   <see cref="TargetRequest.Description"/> conveys "creature an
///   opponent doesn't control" but the engine's structural target
///   filtering does not enforce control predicates from the description
///   string. Callers (tests, bot) supply legal candidates per the
///   description; the resolve body still no-ops on illegal targets via
///   the standard CR 608.2b guard.
/// </summary>
[CardName("Vines of Vastwood")]
public static class VinesOfVastwoodFactory
{
    public const string CardName = "Vines of Vastwood";
    public const string PrintedManaCost = "{G}";
    public const string KickerCostText = "{G}";

    /// <summary>Layer 7c +P/+T magnitude when kicked (CR 613.1g).</summary>
    public const int KickedPumpAmount = 4;

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>CardDef DSL — card shape only. The Kicker structural
    /// marker is attached via <see cref="CardDefBuilder.WithKeyword"/>;
    /// the resolve SpellDefinition lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Kicker");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Construct Vines of Vastwood's kicker <see cref="IAdditionalCost"/>
    /// for the supplied <paramref name="card"/> instance. Convenience
    /// builder for callers (tests, bot decision layer) that have already
    /// decided to pay the kicker; layer the returned cost onto the cast
    /// via <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
    /// parameter. Mirrors <see cref="BurstLightningFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature an opponent doesn't control" request. On
    /// resolution:
    /// <list type="bullet">
    /// <item>If the target is no longer a <see cref="Creature"/> on the
    ///   battlefield, the whole effect no-ops (CR 608.2b).</item>
    /// <item>Otherwise register a <see cref="GrantKeywordUntilEndOfTurnEffect"/>
    ///   granting <see cref="GrantedHexproof"/> until end of turn — this
    ///   delivers the printed "can't be target of opponents' spells or
    ///   abilities this turn" clause via the engine's existing Hexproof
    ///   handling in <see cref="Majik.Core.Targeting.TargetLegality"/>
    ///   (CR 702.11b).</item>
    /// <item>If the spell was kicked (<see cref="Card.WasKicked"/> on
    ///   <paramref name="card"/>, CR 702.33b — checked at resolve), also
    ///   register a <see cref="PumpUntilEndOfTurnEffect"/>(+4, +4).</item>
    /// </list>
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body
    /// reads <see cref="Card.WasKicked"/> off this same reference so the
    /// kicker branch fires only when the cast actually paid the rider
    /// (CR 702.33b).</param>
    public static SpellDefinition BuildDefinition(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature an opponent doesn't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                bool wasKicked = card is Card concrete && concrete.WasKicked;
                return new IEffect[]
                {
                    new Effect(
                        "Vines of Vastwood — hexproof EOT" + (wasKicked ? " + kicked +4/+4 EOT" : string.Empty),
                        () => Resolve(raw, wasKicked)),
                };
            });
    }

    private static void Resolve(object raw, bool wasKicked)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 702.11b — Hexproof = "can't be the target of spells or abilities
        // your opponents control". Registered as a Layer-6 keyword grant with
        // EOT expiry (CR 514.2).
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedHexproof));

        // CR 702.33b — "If this spell was kicked" is sampled at resolve time.
        // KickerAdditionalCost.Pay stamps Card.WasKicked at cast-announcement;
        // SpellCastFlow appends a post-resolve cleanup effect that clears it.
        if (wasKicked)
        {
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, KickedPumpAmount, KickedPumpAmount));
        }
    }
}
