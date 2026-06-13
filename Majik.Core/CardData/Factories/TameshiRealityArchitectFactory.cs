using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tameshi, Reality Architect (Kamigawa: Neon Dynasty,
/// {2}{U}). Legendary Creature — Moonfolk Wizard 2/3. Oracle text (verified
/// against Scryfall):
///   "Whenever one or more noncreature permanents are returned to hand, draw a
///    card. This ability triggers only once each turn.
///    {X}{W}, Return a land you control to its owner's hand: Return target
///    artifact or enchantment card with mana value X or less from your
///    graveyard to the battlefield. Activate only as a sorcery."
///
/// ## Implemented (v1)
/// - 2/3 Legendary Creature — Moonfolk Wizard at {2}{U}, owner/controller
///   wired.
/// - <b>Once-each-turn noncreature-bounce draw trigger</b>
///   (CR 603.2 / 603.3 / 121.1): a <see cref="TriggeredAbility"/> over
///   <see cref="CardMovedEvent"/> that matches when a permanent moves to the
///   Hand zone (ToZone = <see cref="ZoneType.Hand"/>) from the battlefield and
///   the moved card is NOT a creature (CR 110.4 — "noncreature permanent").
///   The "one or more ... returned to hand" + "triggers only once each turn"
///   wording is modelled with a <c>firedThisTurn</c> flag held in a closure
///   private to this card instance: the predicate fires only on the first
///   qualifying bounce each turn and arms the flag, so a second qualifying
///   bounce (or a second noncreature in a separate move-event the same turn)
///   does not re-trigger. The flag resets on a <see cref="TurnStartedEvent"/>
///   (CR 500.1) when an event bus is supplied. Effect = the controller draws
///   one card (CR 121.1). Mirrors the per-turn-gated-draw shape of
///   <see cref="FaerieMastermindFactory"/> (closure counter + TurnStarted
///   reset), keyed on a bounce (CardMovedEvent→Hand) instead of a draw.
///
/// ## Deferred (v1 gap — see <see cref="KnownPartialImplementations"/>)
/// - <b>"{X}{W}, Return a land you control to its owner's hand: Return target
///   artifact or enchantment card with mana value X or less from your
///   graveyard to the battlefield. Activate only as a sorcery."</b> — NOT
///   emitted. The activated-ability path has no per-activation X ledger
///   (<see cref="ActivatedAbility"/> stores no chosen X, and
///   <see cref="Game.AbilityActivationFlow"/> never prompts for one — only the
///   spell cast path does, via <c>ChosenSpellParams.X</c> /
///   <c>SpellCastFlow.PromptForXAsync</c>). Existing {X}-cost activated
///   abilities (Steel Hellkite, Blast Zone, Lair of the Hydra) take a
///   caller-supplied <c>Func&lt;int&gt; xValueProvider</c> that resolves to 0
///   on the production routed build (the dispatcher calls the single-arg
///   <c>Create(owner)</c>). Wiring this ability now would emit a sorcery-speed
///   reanimation that, in real play, only ever returns mana-value-0 artifacts
///   /enchantments — a silent partial. The clause is therefore deferred until
///   the engine grows a per-activation X ledger (prompt X during
///   AbilityActivationFlow + thread it to resolution). A second, smaller piece
///   it needs — a "return a CHOSEN land you control to hand" additional cost —
///   is card-local-authorable (cf. SteelshaperApprenticeFactory's
///   ReturnSelfToHandCost) and is NOT the blocker; the X ledger is.
/// </summary>
[CardName("Tameshi, Reality Architect")]
public static class TameshiRealityArchitectFactory
{
    public const string CardName = "Tameshi, Reality Architect";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Tameshi with no live runtime wiring (the dispatcher / shape
    /// path). The bounce-draw trigger is attached for shape observability but
    /// not registered with a <see cref="TriggerManager"/>, and its once-per-turn
    /// flag is never reset (no event bus). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tameshi with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus. When supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the once-per-turn
    /// bounce-draw gate (CR 500.1). May be null.</param>
    /// <param name="triggers">TriggerManager the bounce-draw trigger registers
    /// with so a <see cref="CardMovedEvent"/> lands it on the stack. May be
    /// null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Moonfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        AddNoncreatureBounceDrawTrigger(card, owner, eventBus, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // "Whenever one or more noncreature permanents are returned to hand, draw
    // a card. This ability triggers only once each turn." (CR 603.2 / 603.3 /
    // 121.1.)
    // -----------------------------------------------------------------------
    private static void AddNoncreatureBounceDrawTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        // Once-per-turn gate. Shared between the trigger predicate (sets it on
        // the first qualifying bounce) and the TurnStartedEvent reset handler.
        var firedThisTurn = false;

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // CR 603.3 — the ability "triggers only once each turn": once it
            // has fired this turn, further qualifying bounces (including a
            // second noncreature in a separate move-event) do not re-trigger.
            if (firedThisTurn) return false;

            // "returned to hand" — a battlefield → Hand zone change
            // (CR 400.7 / bounce). The engine fires a CardMovedEvent per card
            // moved; "one or more ... are returned to hand" collapses to "a
            // noncreature permanent is returned to hand" gated to once a turn.
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Hand) return false;

            // "noncreature permanents" (CR 110.4) — a creature returned to hand
            // does not satisfy the trigger.
            if (e.Card.HasType(CardType.Creature)) return false;

            firedThisTurn = true;
            return true;
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (a noncreature permanent was returned to hand this turn)",
            () => DrawCard(card.Controller ?? owner));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drawEffect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 500.1 — re-arm the once-per-turn gate when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => firedThisTurn = false);
        }
    }

    /// <summary>
    /// Direct Library → Hand zone-move (CR 121.1). No-op on an empty library;
    /// does not route through a unified draw-replacement bus / set the
    /// empty-library loss flag (same shortcut as
    /// <see cref="FaerieMastermindFactory"/>'s draw helper).
    /// </summary>
    private static void DrawCard(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — see helper xmldoc.
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
