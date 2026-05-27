using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Carrion Feeder (Scourge / Onslaught block, {B}).
///
/// Creature — Zombie 1/1. Oracle text:
///   "Carrion Feeder can't block.
///    Sacrifice another creature: Put a +1/+1 counter on Carrion Feeder."
///
/// ## Implemented (v1)
/// - 1/1 Zombie at {B}, owner/controller assigned.
/// - <b>Can't block (CR 509.1c)</b> — registered as a permanent
///   <see cref="CombatRestrictionEffect"/>
///   (<see cref="CombatRestriction.CannotBlock"/>,
///   <c>expiresAtEndOfTurn = false</c>) when a
///   <see cref="ContinuousEffectsService"/> is supplied. Without it the
///   restriction is omitted — shape tests only (same pattern as
///   <see cref="BloodghastFactory"/>).
/// - <b>Activated ability</b>: cost =
///   <see cref="SacrificeAnotherCreatureCost"/>, effect = add 1
///   <see cref="CounterType.PlusOnePlusOne"/> counter to Carrion Feeder via
///   <see cref="CountersService.Add"/> (replacement-aware: Hardened Scales /
///   Doubling Season rewrite the count — CR 614).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>:
///   <see cref="SacrificeAnotherCreatureCost.Target"/> must be set by the
///   agent before <c>Pay</c>; v1 falls back to the first eligible creature
///   (deterministic — same gap as Goblin Bombardment / Yawgmoth).
/// - Can't-block enforcement without a ContinuousEffectsService: the
///   restriction is not registered on the single-arg dispatcher path, so
///   shape tests that need it should use the full-wiring overload.
/// </summary>
[CardName("Carrion Feeder")]
public static class CarrionFeederFactory
{
    public const string CardName = "Carrion Feeder";

    /// <summary>
    /// Construct Carrion Feeder with no runtime service wiring. The card
    /// has the correct shape and the activated ability is attached for
    /// structural inspection, but the can't-block restriction is not
    /// registered and the counter add is not replacement-aware.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Carrion Feeder with optional <see cref="ContinuousEffectsService"/>
    /// (registers the can't-block restriction) and optional
    /// <see cref="ReplacementBus"/> (routes the +1/+1 counter through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season etc. can rewrite the count — CR 614).
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{B}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Can't block — CR 509.1c.
        // Permanent restriction so CombatValidator.CanBlock returns false.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new CombatRestrictionEffect(
                CombatRestriction.CannotBlock,
                target: card,
                expiresAtEndOfTurn: false));
        }

        // ----------------------------------------------------------------
        // Sacrifice another creature: Put a +1/+1 counter on Carrion Feeder.
        // CR 602 (activated abilities) + CR 614 (replacement effects on
        // counter placement).
        // ----------------------------------------------------------------
        var sacrificeCost = new SacrificeAnotherCreatureCost(card);

        var ability = new CarrionFeederAbility(
            source: card,
            controller: owner,
            sacrificeCost: sacrificeCost,
            counterEffect: new Effect(
                "Carrion Feeder: put a +1/+1 counter on it",
                () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements)));

        card.AddAbility(ability);
        return card;
    }
}

/// <summary>
/// Carrion Feeder's sole activated ability — sacrifice another creature to
/// place a +1/+1 counter on itself. Subclasses <see cref="ActivatedAbility"/>
/// so the sacrifice cost is reachable from tests / bots that want to
/// pre-set <see cref="SacrificeAnotherCreatureCost.Target"/>.
/// </summary>
public sealed class CarrionFeederAbility : ActivatedAbility
{
    /// <summary>
    /// The sacrifice cost on the ability — exposed so callers can pre-set
    /// <see cref="SacrificeAnotherCreatureCost.Target"/> before activation.
    /// </summary>
    public SacrificeAnotherCreatureCost SacrificeChoice { get; }

    internal CarrionFeederAbility(
        Creature source,
        Player controller,
        SacrificeAnotherCreatureCost sacrificeCost,
        IEffect counterEffect)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[] { sacrificeCost },
            effects: new[] { counterEffect })
    {
        SacrificeChoice = sacrificeCost;
    }
}
