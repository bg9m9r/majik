using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Walking Ballista (Kaladesh, {X}{X}).
///
/// Walking Ballista is an Artifact Creature — Construct 0/0.
/// Oracle text:
///   "Walking Ballista enters the battlefield with X +1/+1 counters on it.
///    {4}: Put a +1/+1 counter on Walking Ballista. Activate only as a sorcery.
///    Remove a +1/+1 counter from Walking Ballista: It deals 1 damage to any target."
///
/// ## Implemented (v1)
/// - Artifact Creature (multi-type) with Construct subtype and 0/0 base stats
/// - {4}: Put a +1/+1 counter — cost wired; resolves correctly
/// - Remove a +1/+1 counter: 1 damage — <see cref="RemovePlusOnePlusOneCounterCost"/>
///   deducts the counter; effect is a stub (no targeting prompt yet)
///
/// ## Deferred (v1 gaps, see linked issues)
/// - **ETB X counters**: requires plumbing ChosenSpellParams.X through the
///   ZoneMoveIntent / ETB hook layer. Until that infrastructure exists,
///   Walking Ballista enters as a 0/0 with zero counters (state-based actions
///   will immediately put it in the graveyard — acceptable for unit tests
///   that pre-seed counters manually).
/// - **Sorcery-speed restriction on {4}**: ActivatedAbility has no
///   IsSorcerySpeed flag yet. Restriction is documented here but not enforced.
/// - **Target prompt for ping damage**: Effect stub fires but does not route
///   damage to a chosen target. Full targeting requires the active prompt
///   system (ITarget / TargetResolver).
/// </summary>
public static class WalkingBallistaFactory
{
    /// <summary>
    /// Construct a Walking Ballista for the given owner.
    /// The returned <see cref="Creature"/> also carries <see cref="CardType.Artifact"/>
    /// (multi-type — CR 301.1 / 302.1).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Build as Creature first; the Artifact type is added below via
        // Card.AddCardType (the multi-type seam we added for this factory).
        var card = new Creature(
            name: "Walking Ballista",
            manaCost: "{X}{X}",
            power: 0,
            toughness: 0,
            subtypes: new[] { CardSubtype.Construct });

        // Walking Ballista is also an Artifact (CR 301.1).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Ability 1: {4}: Put a +1/+1 counter on Walking Ballista.
        //            Activate only as a sorcery.  (CR 606 / Rule 702)
        // Sorcery-speed restriction is deferred — see class xmldoc.
        // ----------------------------------------------------------------
        var growAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("4"),
            },
            effects: new IEffect[]
            {
                new Effect(
                    "Walking Ballista: put a +1/+1 counter",
                    () => card.Counters.Add(CounterType.PlusOnePlusOne, 1)),
            });
        card.AddAbility(growAbility);

        // ----------------------------------------------------------------
        // Ability 2: Remove a +1/+1 counter from Walking Ballista:
        //            It deals 1 damage to any target.
        // Target selection is deferred — see class xmldoc.
        // ----------------------------------------------------------------
        var pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new RemovePlusOnePlusOneCounterCost(card, 1),
            },
            effects: new IEffect[]
            {
                // TODO: Route 1 damage to the chosen target via ITarget /
                // TargetResolver once the targeting prompt is plumbed.
                new Effect(
                    "Walking Ballista: deal 1 damage to any target (stub — no targeting yet)",
                    () => { /* target damage deferred */ }),
            });
        card.AddAbility(pingAbility);

        return card;
    }
}
