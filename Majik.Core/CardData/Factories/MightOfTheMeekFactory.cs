using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Might of the Meek (Bloomburrow, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gains trample until end of turn. It also gets +1/+0 until
///    end of turn if you control a Mouse.
///    Draw a card."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {R}; mana value 1</item>
///   <item>Type line: Instant; colors: R</item>
/// </list>
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R} (red). The card shape is loaded from the
///   embedded JSON definition (<c>might-of-the-meek.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> and built
///   through <see cref="CardDefinitionFactory"/> — same posture as the other
///   single-target combat-trick + cantrip factories (e.g.
///   <see cref="FlareOfFaithFactory"/>, <see cref="BalefulMasteryFactory"/>).
///   The resolve-time body lives in <see cref="BuildDefinition"/> because a
///   <see cref="SpellDefinition"/> carries a target request not expressible in
///   the data-only JSON schema.
/// - <see cref="BuildDefinition"/> declares one 1..1 "target creature" request
///   (any creature on any battlefield — combat tricks can pump an opponent's
///   creature). On resolution (CR 608.2b illegal-target guard first):
///   <list type="bullet">
///   <item>The target gains <see cref="GrantedTrample"/> until end of turn —
///     UNCONDITIONAL (CR 702.19 Trample; Layer 6 keyword grant, CR 514.2 EOT
///     expiry).</item>
///   <item>It ALSO gets <see cref="ConditionalPumpPower"/>/+0 until end of turn
///     — but ONLY when the caster controls a Mouse at resolution (CR 205.3m
///     creature subtype; CR 613.1g Layer 7c; CR 514.2 EOT expiry). "you control"
///     reads the CASTER's battlefield (CR 109.5 — "you" = the spell's
///     controller), so an opponent's Mouse does not enable the pump.</item>
///   </list>
/// - <b>Cantrip tail</b> — "Draw a card." The caster draws one card on
///   resolution (CR 121.1) via <see cref="Fx.DrawCards"/>. This is an
///   independent printed sentence, so it fires even when the buff half fizzles
///   on an illegal target (CR 608.2b).
///
/// ## Named-factory vs production binder
/// As with the other bespoke single-target tricks, production card LOAD goes
/// through <see cref="CardDefinitionFactory"/> (this factory's <c>Create</c>),
/// and <see cref="CardFactoryContractTests"/> asserts dispatch + well-formedness
/// for every implemented card automatically. The bespoke
/// <see cref="BuildDefinition"/> resolution shape — a target request plus the
/// "if you control a Mouse" subtype-conditional pump that the oracle-text
/// <c>ClauseCompositionTemplate</c> does not model — is exercised by the
/// per-card unique-behaviour tests (mirrors <see cref="FlareOfFaithFactory"/>'s
/// "if it's a Human" conditional).
///
/// ## Rules citations
/// - CR 702.19 — Trample.
/// - CR 205.3m / CR 109.5 — "a Mouse you control".
/// - CR 613.1g — Layer 7c +P/+T (+1/+0).
/// - CR 514.2 — until-end-of-turn effects expire in the cleanup step.
/// - CR 121.1 — draw a card.
/// - CR 608.2b — illegal target at resolution → that part of the effect does
///   nothing (here: the buff clause no-ops; the independent draw clause fires).
/// </summary>
[CardName("Might of the Meek")]
public static class MightOfTheMeekFactory
{
    public const string CardName = "Might of the Meek";
    public const string Slug = "might-of-the-meek";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 702.19 — the unconditional keyword grant.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>CR 613.1g — Layer 7c +1 power, conditional on controlling a Mouse.</summary>
    public const int ConditionalPumpPower = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request, no X. On resolution see the EffectFactory body.
    /// </summary>
    /// <param name="caster">The controller of Might of the Meek — used both for
    /// the "a Mouse you control" subtype check (CR 109.5) and as the recipient
    /// of the "Draw a card." cantrip (CR 121.1).</param>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff | BotIntent.CombatTrick,
                    // Any creature on any battlefield is a legal target (a combat
                    // trick may pump an opponent's blocker/attacker).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target creature gains trample (and +1/+0 if you " +
                        "control a Mouse) until end of turn; draw a card",
                        () => Resolve(raw, caster)),
                };
            });
    }

    private static void Resolve(object raw, Player caster)
    {
        // CR 121.1 — "Draw a card." Independent printed sentence: fires before
        // the (possibly-fizzled) buff clause is even reached, so the cantrip is
        // never lost to an illegal target (CR 608.2b).
        Fx.DrawCards(caster, 1);

        // CR 608.2b — the target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // "Target creature gains trample until end of turn" — UNCONDITIONAL.
        // CR 702.19 Trample; Layer 6 keyword grant, CR 514.2 EOT expiry.
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedTrample));

        // "It also gets +1/+0 until end of turn if you control a Mouse."
        // CR 109.5 — "you control" = the spell's controller's battlefield.
        // CR 205.3m — Mouse creature subtype (effective subtypes, CR 613 — honour
        // type-changing effects). CR 613.1g Layer 7c +1/+0; CR 514.2 EOT expiry.
        var controlsMouse = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(p => p.GetEffectiveSubtypes().Contains(CardSubtype.Mouse));
        if (controlsMouse)
        {
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, ConditionalPumpPower, 0));
        }
    }
}
