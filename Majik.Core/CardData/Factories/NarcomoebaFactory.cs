using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Narcomoeba (Future Sight, {1}{U}).
///
/// Creature — Illusion 1/1 Flying. Oracle text:
///   "Flying.
///    When this creature is put into your graveyard from your library,
///    you may put it onto the battlefield."
///
/// ## Implemented (v1)
/// - 1/1 Illusion with mana cost {1}{U}, owner/controller assigned.
/// - <see cref="KeywordAbility"/> marker for Flying (CR 702.9).
/// - <b>Mill-trigger (CR 603.6c — graveyard-resident trigger via the
///   library → graveyard zone change)</b>: a <see cref="TriggeredAbility"/>
///   watches <see cref="CardMovedEvent"/> filtered to
///   <c>FromZone == Library &amp;&amp; ToZone == Graveyard</c> for THIS
///   card (reference identity — Narcomoeba's trigger is self-referential,
///   not a generalised "whenever a card is milled"). Active in
///   <see cref="ZoneType.Graveyard"/> so it fires after the mill move
///   completes (<see cref="ZoneService.MoveCard"/> sets the destination
///   zone before publishing the event — see also Arclight Phoenix's
///   graveyard-resident posture).
/// - <b>"You may" prompt</b>: when an <see cref="IPlayerAgent"/> is
///   supplied, the trigger resolution consults
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> with
///   <see cref="BotIntent.Reanimate"/>. Null agent → legacy auto-accept
///   (same posture as Bloodghast / Arclight Phoenix single-arg dispatch).
/// - <b>Graveyard → Battlefield move</b>: when a
///   <see cref="ZoneService"/> is wired, the move is routed through
///   <see cref="ZoneService.MoveCard"/> so ETB triggers + replacement
///   effects fire (CR 603.6a). Without a service the factory performs a
///   raw zone mutation (shape-only path).
///
/// ## Deferred (v1 gaps)
/// - The trigger doesn't currently care WHO milled Narcomoeba — any path
///   that takes the printed library → graveyard move (mill, Dredge skip,
///   Surveil, draw-replacement) fires it. This matches the printed text.
/// - Cast-marker (CR 113.5): the battlefield return is a non-cast move,
///   so <see cref="Card.WasCast"/> stays false on the return — a future
///   Containment Priest sitting opposite would correctly exile.
/// </summary>
[CardName("Narcomoeba")]
public static class NarcomoebaFactory
{
    public const string CardName = "Narcomoeba";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Construct Narcomoeba with no runtime service wiring. The card has
    /// the correct shape (name, type, P/T, mana cost, subtypes, Flying
    /// marker) and the mill-trigger is attached for structural inspection,
    /// but the trigger is not registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, agent: null);

    /// <summary>
    /// Construct Narcomoeba with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone-service used by the mill-trigger to
    /// move Narcomoeba from graveyard to battlefield so ETB triggers fire
    /// (CR 603.6a). May be null — raw zone move is performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident
    /// trigger registration (CR 603.6c). May be null — trigger is attached
    /// structurally but not registered with the bus.</param>
    /// <param name="agent">Optional agent for the "you may" prompt
    /// (<see cref="BotIntent.Reanimate"/>). Null → legacy auto-accept.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Illusion });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Mill-trigger — CR 603.1 + CR 603.6c.
        //   "When this creature is put into your graveyard from your
        //    library, you may put it onto the battlefield."
        // Self-referential (reference identity), gated on Library →
        // Graveyard for this specific card. ActiveZones = {Graveyard}
        // matches the live card.Zone at publish time (ZoneService sets
        // the destination zone before publishing CardMovedEvent).
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: put onto the battlefield from graveyard (mill trigger)",
            () =>
            {
                // CR 608.2b — re-check the zone at resolution. If the
                // card has already left the graveyard, the effect no-ops.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                // "You may" — consult agent when wired; else auto-accept.
                if (agent != null)
                {
                    var yes = agent.ChooseYesNoAsync(
                        "Put Narcomoeba onto the battlefield?",
                        BotIntent.Reanimate).GetAwaiter().GetResult();
                    if (!yes) return;
                }

                if (zoneService != null)
                {
                    // CR 603.6a — ETB triggers + replacements fire.
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                }
                else
                {
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                    card.SetController(owner);
                }
            });

        var millCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, card)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Graveyard);

        var millTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: millCondition,
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(millTrigger);
        triggers?.RegisterTriggeredAbility(millTrigger);

        return card;
    }
}
