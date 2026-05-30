using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lotleth Troll (Return to Ravnica, {B}{G}).
///
/// Creature — Zombie Troll 2/1. Oracle text (Scryfall, verified):
///   "Trample
///    Discard a creature card: Put a +1/+1 counter on this creature.
///    {B}: Regenerate this creature."
///
/// Golgari graveyard-aggro one-of-a-kind: dump creature cards from hand to
/// grow it (feeding the graveyard for Golgari recursion in the bargain),
/// then keep it alive through removal with the {B} regenerate. Combines the
/// "discard a card: +1/+1 counter" activated shape from
/// <see cref="PsychicFrogFactory"/> (here filtered to creature cards) with
/// the {B}-mana regenerate shield used by
/// <see cref="TwistedAbominationFactory"/>'s printed regenerate clause and
/// the regenerate-shield primitive exercised by
/// <see cref="ExperimentOneFactory"/>.
///
/// ## Implemented (v1)
///
/// - <b>2/1 Creature — Zombie Troll at {B}{G}.</b> (CR 205.3m — Zombie +
///   Troll subtypes.)
///
/// - <b>Trample</b> — wired as a <see cref="KeywordAbility"/> marker so the
///   combat-damage-assignment code (CR 702.19) reads it the same way it
///   reads every other printed Trample creature (mirrors
///   <see cref="BallLightningFactory"/>).
///
/// - <b>"Discard a creature card: Put a +1/+1 counter on this creature."</b>
///   — activated ability (CR 602.1). The sole activation cost is a
///   <see cref="DiscardACreatureCardCost"/> (a creature-card-restricted
///   <see cref="ICost"/>; no mana), so it is repeatable while the controller
///   holds a creature card. On resolution one
///   <see cref="CounterType.PlusOnePlusOne"/> counter is placed on Lotleth
///   Troll via <see cref="CountersService.Add"/> so a controlled Hardened
///   Scales / Doubling Season can rewrite the amount (CR 614) and the
///   post-commit <see cref="Majik.Core.Events.CounterAddedEvent"/> fires.
///   Exact parallel of <see cref="PsychicFrogFactory"/>'s discard-pump, with
///   the cost narrowed to creature cards.
///
/// - <b>"{B}: Regenerate this creature."</b> — activated ability
///   (CR 602.1 / CR 701.18). The sole cost is a <see cref="ManaCostCost"/>
///   ("{B}"); on resolution the effect calls
///   <see cref="Permanent.AddRegenerationShield"/>, creating a regeneration
///   shield (CR 701.15a) consumed by the next destroy this turn — tapping
///   Lotleth Troll, removing it from combat, and healing its damage
///   (CR 701.18). Same shield primitive as
///   <see cref="ExperimentOneFactory"/> (paid with mana here instead of
///   counters).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Both activated abilities are
///   fully attached and exercisable; the discard-pump counter placement uses
///   the direct <see cref="CountersService.Add"/> fallthrough (no
///   replacement-bus rewrites, no event publish).
/// - <see cref="Create(Player, ReplacementBus?)"/> — when a
///   <see cref="ReplacementBus"/> is supplied the discard-pump counter
///   placement routes through it so Hardened Scales / Doubling Season can
///   rewrite the count (CR 614).
/// </summary>
[CardName("Lotleth Troll")]
public static class LotlethTrollFactory
{
    public const string CardName = "Lotleth Troll";
    public const string PrintedManaCost = "{B}{G}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>{B} regenerate cost.</summary>
    public const string RegenerateCost = "{B}";

    /// <summary>+1/+1 counters placed per discard-pump activation.</summary>
    public const int PumpCounters = 1;

    /// <summary>
    /// Construct Lotleth Troll with no <see cref="ReplacementBus"/> wiring.
    /// Both activated abilities are fully attached; the discard-pump counter
    /// placement uses the direct <see cref="CountersService.Add"/>
    /// fallthrough (no replacement rewrites, no event publish).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Lotleth Troll. When <paramref name="replacements"/> is
    /// supplied the discard-pump +1/+1 counter placement routes through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season replacements can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Troll });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trample — CR 702.19. KeywordAbility marker; combat-damage
        // assignment reads it (mirrors BallLightningFactory).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // "Discard a creature card: Put a +1/+1 counter on this creature."
        // CR 602.1 — repeatable while the controller holds a creature card.
        // DiscardACreatureCardCost is the sole activation cost (no mana).
        // +1/+1 counter via CountersService.Add (CR 614 replacements +
        // CounterAddedEvent when a ReplacementBus/EventBus is wired).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, PumpCounters, replacements));

        var pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardACreatureCardCost() },
            effects: new IEffect[] { pumpEffect });

        card.AddAbility(pumpAbility);

        // ----------------------------------------------------------------
        // "{B}: Regenerate this creature." CR 602.1 / CR 701.18.
        // The only cost is {B}. On resolve a regeneration shield is created
        // (Permanent.AddRegenerationShield — CR 701.15a), consumed by the
        // next destroy this turn (tap, remove from combat, heal damage —
        // CR 701.18). Same shield primitive as ExperimentOneFactory.
        // ----------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self (CR 701.18)",
            () => card.AddRegenerationShield());

        var regenerateAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(RegenerateCost) },
            effects: new IEffect[] { regenerateEffect });

        card.AddAbility(regenerateAbility);

        return card;
    }
}
