using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tenacious Underdog (Streets of New Capenna, {1}{B}).
///
/// Creature — Human Warrior 3/2. Oracle text (verified against Scryfall):
///   "Blitz—{2}{B}{B}, Pay 2 life. (If you cast this spell for its blitz cost,
///    it gains haste and "When this creature dies, draw a card." Sacrifice it
///    at the beginning of the next end step.)
///    You may cast this card from your graveyard using its blitz ability."
///
/// ## Mechanic — Blitz (CR 702.152)
/// Blitz is the net-new keyword subsystem this card unblocks. It is wired from
/// three already-built engine seams, mirroring the Evoke posture:
///   - <see cref="BlitzAlternativeCost"/> — an <see cref="IAlternativeCost"/>
///     that replaces the printed mana cost with the blitz cost {2}{B}{B} and,
///     on resolution, stamps <see cref="Creature.BlitzWasPaid"/> (mirror of
///     <c>EvokeWasPaid</c>). For THIS card the alt-cost is legal from the
///     GRAVEYARD ("cast this card from your graveyard using its blitz
///     ability"); use <see cref="BlitzAlternativeCost.FromGraveyard"/>.
///   - <see cref="PayLifeAdditionalCost"/> — the "Pay 2 life" portion, fed as
///     an additional cost alongside the alt-cost through
///     <see cref="Majik.Core.Game.SpellCastFlow"/> so it's paid as part of
///     casting (CR 601.2f).
///   - <see cref="BlitzFactory"/> — the three printed riders (haste,
///     "when this creature dies, draw a card", delayed end-step sacrifice),
///     each gated on <see cref="Creature.BlitzWasPaid"/> so a card returned to
///     play any OTHER way gets none of them (CR 702.152c).
///
/// ## Implemented (v1)
/// - 3/2 Creature — Human Warrior, mana cost {1}{B}.
/// - The blitz dies-draw trigger is attached on the shape (and registered when
///   a <see cref="TriggerManager"/> is supplied). It only fires when the card
///   was cast for its blitz cost (intervening-if on
///   <see cref="Creature.BlitzWasPaid"/>).
/// - A blitz ETB rider trigger (<see cref="Triggers.OnEnterBattlefieldSelf"/>)
///   applies the haste grant + registers the delayed end-step sacrifice when
///   blitz was paid (no-op otherwise).
/// - <see cref="BuildBlitzCost"/> exposes the graveyard blitz alt-cost +
///   the bundled "Pay 2 life" additional cost so the cast pipeline / bot
///   probes can offer it.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (dies + ETB riders attached
///   structurally, not registered). The overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully wired.
/// </summary>
[CardName("Tenacious Underdog")]
public static class TenaciousUnderdogFactory
{
    public const string CardName = "Tenacious Underdog";
    public const string PrintedManaCost = "{1}{B}";
    public const string BlitzManaCost = "{2}{B}{B}";
    public const int BlitzLifeCost = 2;
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>Construct Tenacious Underdog with riders attached to the shape
    /// but NOT registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.</summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>Construct Tenacious Underdog with optional runtime wiring.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager the blitz dies + ETB-rider
    /// triggers register with. May be null — attached structurally only.</param>
    /// <param name="zoneService">Zone service the delayed blitz sacrifice
    /// routes through so the dies-draw trigger fires. May be null.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 / 702.152b — Haste marker. The continuous "has haste" source
        // the combat path reads when the creature was cast for its blitz cost.
        // The summoning-sickness clear that makes the grant matter is applied by
        // BlitzFactory.ApplyEntersRiders only when BlitzWasPaid is true.
        card.AddAbility(new KeywordAbility(BlitzFactory.HasteKeyword, card, owner));

        // CR 702.152b — "When this creature dies, draw a card." Gated on
        // BlitzWasPaid (intervening-if).
        var diesTrigger = BlitzFactory.BuildDiesTrigger(card);
        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // CR 702.152b — ETB rider: when this enters AND blitz was paid, apply
        // haste (clear summoning sickness) and register the delayed end-step
        // sacrifice. ApplyEntersRiders no-ops when BlitzWasPaid is false.
        var etbRider = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: apply blitz enters-riders (haste + delayed sac)",
                    () => BlitzFactory.ApplyEntersRiders(card, triggers, zoneService)),
            },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbRider);
        triggers?.RegisterTriggeredAbility(etbRider);

        return card;
    }

    /// <summary>
    /// CR 702.152 — build the graveyard blitz alternative cost for this card,
    /// together with the bundled "Pay 2 life" additional cost. The caller drops
    /// the alt-cost into <see cref="Majik.Core.Game.SpellCastFlow"/>'s
    /// <c>alternativeCost</c> argument and the life cost into its
    /// <c>additionalCosts</c> list.
    /// </summary>
    public static (BlitzAlternativeCost AltCost, PayLifeAdditionalCost LifeCost) BuildBlitzCost() =>
        (BlitzAlternativeCost.FromGraveyard(ManaCost.Parse(BlitzManaCost)),
         new PayLifeAdditionalCost(BlitzLifeCost));
}
