using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vengevine (Rise of the Eldrazi, {2}{G}{G}).
///
/// Creature — Plant Elemental 4/3. Oracle text:
///   "Haste.
///    Whenever you cast a creature spell, if it's the second creature spell
///    you cast this turn, you may return Vengevine from your graveyard to
///    the battlefield."
///
/// ## Implemented (v1)
///
/// - 4/3 Plant Elemental with mana cost {2}{G}{G}, owner / controller stamped.
/// - <see cref="KeywordAbility"/> marker for Haste (CR 702.10).
/// - <b>Graveyard-resident creature-cast trigger (CR 603.6d)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> filtered
///   to (a) the spell's controller is Vengevine's owner AND (b) the spell's
///   card has the Creature card type. A per-turn integer closure increments
///   on every controller creature spell; the printed "second creature spell"
///   gate matches on the exact transition to 2 (same shape as
///   <see cref="LedgerShredderFactory"/> / <see cref="CoriSteelCutterFactory"/>'s
///   "second spell each turn" predicates). <c>ActiveZones = {Graveyard}</c>
///   so the trigger only fires while Vengevine sits in its owner's graveyard
///   (mirrors Bloodghast / Narcomoeba / Arclight Phoenix posture). When an
///   <see cref="IEventBus"/> is supplied the per-turn count resets on
///   <see cref="TurnStartedEvent"/> (CR 500.1).
/// - <b>"You may" prompt</b>: at resolution the trigger consults
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> with
///   <see cref="BotIntent.Reanimate"/> when an agent is supplied; null agent
///   falls back to the legacy auto-accept posture (same shape as
///   Bloodghast / Narcomoeba).
/// - <b>Graveyard → Battlefield return</b>: when a <see cref="ZoneService"/>
///   is wired the return routes through <see cref="ZoneService.MoveCard"/>
///   so ETB triggers + replacement effects fire (CR 603.6a). Without a
///   service the factory performs the raw zone mutation (shape-only path).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Order-of-operations vs. SpellCastFlow</b>: the per-turn counter
///   increments inside the trigger's <c>EventTriggerCondition</c> predicate
///   evaluation. This means the trigger sees the count AFTER incrementing,
///   so the "second" check matches the spell-cast that pushes the count to
///   2 (Vengevine itself being cast cannot fire the trigger because
///   ActiveZones = {Graveyard}, but a creature spell cast WHILE Vengevine
///   is in the graveyard does increment + check correctly). Same predicate-
///   side-effect shape as <see cref="LedgerShredderFactory"/>.
/// - <b>Cast-marker reset (CR 113.5)</b>: Vengevine's battlefield return
///   is a non-cast move so <see cref="Card.WasCast"/> stays false on the
///   return (mirrors Bloodghast / Narcomoeba). A future Containment Priest
///   opposite would correctly exile.
/// </summary>
[CardName("Vengevine")]
public static class VengevineFactory
{
    public const string CardName = "Vengevine";
    public const string PrintedManaCost = "{2}{G}{G}";
    public const int Power = 4;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Vengevine with no runtime service wiring. The card has the
    /// correct shape (name, type, P/T, mana cost, subtypes, Haste marker)
    /// and the creature-cast trigger is attached for structural inspection,
    /// but the trigger is not registered with a <see cref="TriggerManager"/>
    /// and the per-turn count never resets (no <see cref="TurnStartedEvent"/>
    /// subscription).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null, agent: null);

    /// <summary>
    /// Construct Vengevine with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone-service used by the return effect to
    /// move Vengevine from graveyard to battlefield so ETB triggers fire
    /// (CR 603.6a). May be null — raw zone move is performed instead.</param>
    /// <param name="eventBus">Event bus used to subscribe a
    /// <see cref="TurnStartedEvent"/> handler that resets the per-turn
    /// creature-spell count (CR 500.1). May be null — count persists across
    /// turns.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident
    /// trigger registration (CR 603.6d). May be null — trigger is attached
    /// to the card for shape but not registered with the bus.</param>
    /// <param name="agent">Optional agent for the "you may return" prompt
    /// (<see cref="BotIntent.Reanimate"/>). Null → legacy auto-accept.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Plant, CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste keyword marker.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Creature-cast trigger — CR 603.1 / 603.6d.
        //   "Whenever you cast a creature spell, if it's the second creature
        //    spell you cast this turn, you may return Vengevine from your
        //    graveyard to the battlefield."
        // Per-turn count closure shared between the predicate and the
        // TurnStartedEvent reset (mirrors LedgerShredder / Cori-Steel
        // Cutter's "second spell each turn" predicate, narrowed here to
        // creature spells only).
        // ----------------------------------------------------------------
        var creatureSpellsCastThisTurn = new int[] { 0 };

        var secondCreatureCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 112.1 — test the spell's card type as it exists on the
            // stack (i.e. the spell as cast). Creature spells fire even
            // when they share secondary types (DFCs, adventures resolving
            // as creatures), matching the printed predicate.
            if (!ReferenceEquals(e.Spell.Controller, owner)) return false;
            if (!e.Spell.Card.HasType(CardType.Creature)) return false;
            creatureSpellsCastThisTurn[0]++;
            return creatureSpellsCastThisTurn[0] == 2;
        });

        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to battlefield (second creature spell)",
            () =>
            {
                // CR 608.2b — re-check the zone at resolution. If Vengevine
                // has already left the graveyard (replay, second copy in
                // the graveyard, etc.), the effect no-ops.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                // "You may" — consult agent when wired; else auto-accept
                // (same posture as Bloodghast / Narcomoeba).
                if (agent != null)
                {
                    var yes = agent.ChooseYesNoAsync(
                        "Return Vengevine from graveyard to the battlefield?",
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

        var secondCreatureTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: secondCreatureCondition,
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(secondCreatureTrigger);
        triggers?.RegisterTriggeredAbility(secondCreatureTrigger);

        // CR 500.1 — reset the per-turn creature-spell count when a new
        // turn starts (same posture as LedgerShredder / Cori-Steel Cutter).
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => creatureSpellsCastThisTurn[0] = 0);
        }

        return card;
    }
}
