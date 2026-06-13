using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
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
/// - <b>"{X}{W}, Return a land you control to its owner's hand: Return target
///   artifact or enchantment card with mana value X or less from your
///   graveyard to the battlefield. Activate only as a sorcery."</b> — EMITTED
///   (GAP 2, per-activation X ledger). Sorcery-speed
///   <see cref="ActivatedAbility"/>; cost <c>{X}{W}</c> +
///   <see cref="ReturnALandCost"/> (card-local "return a land you control to
///   hand", routed through <see cref="Zones.ZoneService"/> so the resulting
///   <see cref="CardMovedEvent"/> fires this card's own bounce-draw trigger).
///   The chosen X is read at resolution from
///   <see cref="Abilities.ResolutionContext.ChosenX"/> (threaded by
///   <see cref="ActivatedAbility.ResolveAsync"/> after the activation flow
///   prompts <see cref="Players.Agents.IPlayerAgent.ChooseXAsync"/> and pays
///   {X} expanded to X generic via the spell path's
///   <see cref="ValueObjects.ManaCost.AddGenericCost"/> machinery). The target
///   is an artifact/enchantment card in the controller's graveyard; the
///   <c>mv ≤ X</c> gate is re-validated at resolution (CR 608.2b — an over-mv
///   pick fizzles).
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
        AddReanimationAbility(card, owner);

        return card;
    }

    // -----------------------------------------------------------------------
    // "{X}{W}, Return a land you control to its owner's hand: Return target
    //  artifact or enchantment card with mana value X or less from your
    //  graveyard to the battlefield. Activate only as a sorcery."
    //
    // GAP 2 (per-activation X ledger) — the chosen X is read at resolution from
    // ResolutionContext.ChosenX (threaded by ActivatedAbility.ResolveAsync after
    // the activation flow prompts for X and pays {X} expanded to X generic via
    // the spell path's ManaCost.AddGenericCost machinery). Before GAP 2 this
    // ability could only ever fetch mana-value-0 cards, so it was deferred
    // (v1-#15); the ledger makes it real.
    //
    // The "Return a land you control to hand" additional cost is card-local-
    // authorable (ReturnALandCost below; cf. SteelshaperApprentice's
    // ReturnSelfToHandCost) — it routes the bounce through ZoneService so the
    // resulting CardMovedEvent (battlefield → hand of a noncreature land) fires
    // this very card's own once-per-turn bounce-draw trigger.
    // -----------------------------------------------------------------------
    private static void AddReanimationAbility(Creature card, Player owner)
    {
        // CR 601.2c — the chosen target is read at resolution from
        // ResolutionContext.ChosenTargets[0][0]; the candidate pool is the
        // controller's graveyard artifact/enchantment cards (the mv ≤ X gate is
        // re-checked at resolution against the chosen X — GAP 2 collects X after
        // targets in the activated-ability flow, so the choice-time pool is
        // X-agnostic and the resolution validates mv ≤ ChosenX, fizzling an
        // over-mv pick per CR 608.2b).
        var targetRequest = new TargetRequest(
            Description: "target artifact or enchantment card with mana value X or less from your graveyard",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: ctx => GraveyardArtifactsAndEnchantments(card.Controller ?? owner));

        var reanimateEffect = new Effect(
            $"{CardName}: return target artifact/enchantment card with mv ≤ X from your graveyard to the battlefield",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                var x = ctx.ChosenX ?? 0;

                // CR 608.2b — read the chosen target; re-validate at resolution.
                var pick = ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0
                    ? ctx.ChosenTargets[0][0] as ICard
                    : null;
                if (pick == null) return System.Threading.Tasks.ValueTask.CompletedTask;

                // Still in the controller's graveyard, still an artifact or
                // enchantment, and mv ≤ chosen X (CR 107.3 — X is the chosen
                // value). An over-mv pick is now illegal → no reanimation.
                if (pick.Zone != ZoneType.Graveyard) return System.Threading.Tasks.ValueTask.CompletedTask;
                if (!controller.Zones.Graveyard.ContainsCard(pick)) return System.Threading.Tasks.ValueTask.CompletedTask;
                if (!(pick.HasType(CardType.Artifact) || pick.HasType(CardType.Enchantment)))
                    return System.Threading.Tasks.ValueTask.CompletedTask;
                if (ManaValueOf(pick) > x) return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 701.20 — reanimate to the controller's battlefield, routed
                // through ZoneService when one is registered so ETB triggers fire.
                var zones = ZoneServiceRegistry.Get(controller);
                Fx.ReturnFromGraveyardToBattlefield(pick, controller, zones);
                return System.Threading.Tasks.ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{X}{W}"),
                new ReturnALandCost(owner),
            },
            effects: new IEffect[] { reanimateEffect },
            targetRequests: new[] { targetRequest },
            // "Activate only as a sorcery." (CR 117.1a / 307.5)
            sorcerySpeed: true));
    }

    /// <summary>
    /// Candidate pool for the reanimation target — artifact/enchantment CARDS in
    /// the controller's graveyard (CR 110.4 — a card in a graveyard, not a
    /// permanent). The mv ≤ X gate is applied at resolution against the chosen X.
    /// </summary>
    private static IReadOnlyList<object> GraveyardArtifactsAndEnchantments(Player controller) =>
        controller.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Artifact) || c.HasType(CardType.Enchantment))
            .Cast<object>()
            .ToList();

    /// <summary>
    /// CR 202.3 — mana value of a graveyard card. Reads the concrete
    /// <see cref="Card.ManaCostValue"/> when available, else parses the printed
    /// cost string (the <see cref="ICard"/> surface exposes only the string).
    /// </summary>
    private static int ManaValueOf(ICard card) =>
        card is Card concrete
            ? concrete.ManaCostValue.TotalValue
            : Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost).TotalValue;

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

/// <summary>
/// GAP 2 — "Return a land you control to its owner's hand" additional
/// activation cost for Tameshi's reanimation ability (CR 118 / 701.10). This is
/// the card-local-authorable cost the v1-#15 deferral noted is NOT the engine
/// blocker (the X ledger was). It auto-selects a land the paying player controls
/// (v1: deterministic first-land pick — a reasonable choice that keeps the cost
/// payable without an agent prompt at the <see cref="ICost"/> seam; cf.
/// <see cref="SteelshaperApprenticeFactory.ReturnSelfToHandCost"/> which returns
/// a fixed permanent). The bounce is routed through <see cref="ZoneService"/>
/// when registered so the resulting <see cref="CardMovedEvent"/> (a noncreature
/// land battlefield → hand) fires Tameshi's own once-per-turn bounce-draw
/// trigger.
/// </summary>
public sealed class ReturnALandCost : ICost
{
    private readonly Player _controller;

    public ReturnALandCost(Player controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <inheritdoc/>
    public string Description => "Return a land you control to its owner's hand";

    private static Permanent? PickLand(Player player) =>
        player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(c => c.HasType(CardType.Land));

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return PickLand(player) != null;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var land = PickLand(player)
            ?? throw new Majik.Core.Domain.Exceptions.InvalidPlayerActionException(
                $"Cannot pay {Description}: {player.Name} controls no land.");

        // CR 701.10 — returned permanents go to their OWNER's hand.
        var owner = land.Owner ?? player;
        var holder = land.Controller ?? owner;

        var zones = ZoneServiceRegistry.Get(holder);
        if (zones != null)
        {
            zones.MoveCard(land, ZoneType.Battlefield, ZoneType.Hand, owner);
        }
        else
        {
            holder.Zones.Battlefield.RemoveCard(land);
            owner.Zones.Hand.AddCard(land);
            land.SetZone(ZoneType.Hand);
        }
    }
}
