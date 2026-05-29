using System.Runtime.CompilerServices;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bomat Courier (Kaladesh, <c>{1}</c>).
/// Artifact Creature — Construct. 1/1.
///
/// Oracle text (Scryfall-verified):
/// <list type="number">
///   <item>Haste</item>
///   <item>"Whenever this creature attacks, exile the top card of your
///       library face down. (You can't look at it.)"</item>
///   <item>"<c>{R}</c>, Discard your hand, Sacrifice this creature: Put all
///       cards exiled with this creature into their owners' hands."</item>
/// </list>
///
/// ## Implementation
/// <list type="bullet">
///   <item><b>Haste</b> (CR 702.10) — <see cref="KeywordAbility"/> marker,
///   read by combat helpers the same way every other haste-bearing factory
///   in this repo attaches it (Goblin Chieftain, Bloodbraid Elf, …).</item>
///
///   <item><b>Attacks trigger</b> (CR 508.1f) — <see cref="TriggeredAbility"/>
///   over <see cref="Triggers.OnAttackSelf"/> firing on
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/> for
///   this creature. Resolution exiles the top card of the controller's
///   library to their own exile zone and records it in the per-Courier
///   <see cref="BomatCourierState.ExiledWith"/> ledger — the
///   "exiled with this creature" relationship the game tracks (CR 400.7).
///   "Face down / you can't look at it" is informational hidden-zone
///   bookkeeping; the engine has no look-at surface to suppress yet, so the
///   card is simply moved (same posture as Emperor of Bones' exile-and-track
///   begin-combat trigger). Empty library is a clean no-op.</item>
///
///   <item><b>Activated ability</b> (CR 602.1) — <c>{R}</c>, Discard your
///   hand, Sacrifice this creature. Costs:
///     <list type="bullet">
///       <item><c>{R}</c> via <see cref="ManaCostCost"/>.</item>
///       <item>"Discard your hand" (CR 701.16) via the inline
///       <see cref="DiscardYourHandCost"/> — moves every card currently in
///       the activating player's hand Hand → Graveyard through the supplied
///       <see cref="ZoneService"/> (so <see cref="CardMovedEvent"/>
///       publishes), raw zone manipulation otherwise.</item>
///       <item>"Sacrifice this creature" (CR 701.17) via
///       <see cref="AdditionalCost.Sacrifice"/>.</item>
///     </list>
///   Resolution puts every card in the ledger into its owner's hand
///   (CR 109.5 — "their owners' hands", not the controller's). The ledger
///   is drained as cards return.</item>
/// </list>
///
/// <para>
/// <b>Wiring overloads</b>: <see cref="Create(Player)"/> attaches the full
/// ability shape (Haste marker, attacks trigger, activated ability) with no
/// live bus / trigger / zone wiring — suitable for dispatcher / shape tests.
/// <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?)"/>
/// registers the attacks trigger with <paramref name="triggers"/> and routes
/// zone moves through <paramref name="zones"/> when supplied. Same two-mode
/// posture as <see cref="EmperorOfBonesFactory"/> / <see cref="BedlamRevelerFactory"/>.
/// </para>
/// </summary>
[CardName("Bomat Courier")]
public static class BomatCourierFactory
{
    public const string CardName = "Bomat Courier";
    public const string PrintedManaCost = "{1}";
    public const string ActivationManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Per-Courier "exiled with this creature" ledger. Keyed off the Courier
    /// card instance via <see cref="ConditionalWeakTable{TKey,TValue}"/> so
    /// multiple Couriers in the same game keep separate ledgers (mirrors
    /// <see cref="EmperorOfBonesFactory"/>).
    /// </summary>
    private static readonly ConditionalWeakTable<Card, BomatCourierState> _state = new();

    /// <summary>
    /// Retrieve the <see cref="BomatCourierState"/> attached to a Courier
    /// instance produced by this factory. Returns null when the card was
    /// not built by this factory.
    /// </summary>
    public static BomatCourierState? GetState(Card courier)
    {
        ArgumentNullException.ThrowIfNull(courier);
        return _state.TryGetValue(courier, out var s) ? s : null;
    }

    /// <summary>
    /// Construct Bomat Courier for the dispatcher / shape-test path: no
    /// <see cref="IEventBus"/>, <see cref="TriggerManager"/>, or
    /// <see cref="ZoneService"/> wired. Identity + ability shape are fully
    /// populated; live bus-driven trigger firing is a no-op.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zones: null);

    /// <summary>
    /// Construct Bomat Courier with optional engine plumbing.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers.
    /// Not used directly today.</param>
    /// <param name="triggers">When supplied, the attacks trigger registers
    /// so a <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
    /// for this creature lands the trigger on the stack (CR 508.1f).</param>
    /// <param name="zones">When supplied, the exile / discard / return zone
    /// moves route through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for zone-change subscribers.
    /// Raw zone manipulation otherwise.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Construct });

        card.SetOwner(owner);
        card.SetController(owner);

        // Artifact Creature — add the secondary Artifact type (Creature is
        // primary so combat / P-T plumbing treats it as a creature).
        card.AddCardType(CardType.Artifact);

        var state = new BomatCourierState();
        _state.AddOrUpdate(card, state);

        // ----------------------------------------------------------------
        // CR 702.10 — Haste. KeywordAbility marker; combat helpers read it
        // the same way they do for every other haste-bearing factory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // CR 508.1f — "Whenever this creature attacks, exile the top card
        // of your library face down." Fires on the per-attacker
        // CreatureAttacksEvent for this card. Active zone is Battlefield —
        // a creature only attacks from the battlefield.
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: exile the top card of your library face down",
            () => ResolveAttackExile(card, owner, state, zones));

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // CR 602.1 — "{R}, Discard your hand, Sacrifice this creature: Put
        // all cards exiled with this creature into their owners' hands."
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: put all cards exiled with this creature into their owners' hands",
            () => ResolveReturnExiled(state, zones));

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                new DiscardYourHandCost(owner, zones),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { returnEffect });

        card.AddAbility(ability);

        return card;
    }

    // ------------------------------------------------------------------------
    // Resolution helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Resolve the attacks trigger: exile the top card of the controller's
    /// library to their own exile zone and record it in the ledger. Empty
    /// library is a clean no-op.
    /// </summary>
    private static void ResolveAttackExile(
        Card courier, Player controller, BomatCourierState state, ZoneService? zones)
    {
        // The Courier must still be on the battlefield (CR 603.4 — the
        // attacks trigger resolves later; defensive guard).
        if (courier.Zone != ZoneType.Battlefield) return;

        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return;

        if (zones != null)
        {
            zones.MoveCard(top, ZoneType.Library, ZoneType.Exile, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Exile.AddCard(top);
            top.SetZone(ZoneType.Exile);
        }

        // Per-Courier "exiled with this creature" ledger (CR 400.7).
        state.AddExiledWith(top);
    }

    /// <summary>
    /// Resolve the activated ability: put every card in the ledger into its
    /// owner's hand (CR 109.5 — "their owners' hands"). The ledger is drained
    /// as cards return. Cards no longer in the exile zone are skipped (the
    /// "exiled with" relationship ends when a card changes zones).
    /// </summary>
    private static void ResolveReturnExiled(BomatCourierState state, ZoneService? zones)
    {
        // Snapshot — the ledger is mutated as we drain it.
        foreach (var exiled in state.ExiledWith.ToList())
        {
            state.RemoveExiledWith(exiled);

            if (exiled.Zone != ZoneType.Exile) continue;
            var cardOwner = exiled.Owner;
            if (cardOwner == null) continue;

            if (zones != null)
            {
                zones.MoveCard(exiled, ZoneType.Exile, ZoneType.Hand, cardOwner);
            }
            else
            {
                cardOwner.Zones.Exile.RemoveCard(exiled);
                cardOwner.Zones.Hand.AddCard(exiled);
                exiled.SetZone(ZoneType.Hand);
            }
        }
    }
}

/// <summary>
/// Per-Courier "exiled with this creature" ledger. Tracks the order of
/// exile so the return is deterministic.
/// </summary>
public sealed class BomatCourierState
{
    private readonly List<ICard> _exiledWith = new();

    /// <summary>All cards currently exiled with this Courier, in insertion
    /// order.</summary>
    public IReadOnlyList<ICard> ExiledWith => _exiledWith;

    /// <summary>Record <paramref name="card"/> as exiled with this Courier.
    /// Idempotent.</summary>
    public void AddExiledWith(ICard card)
    {
        if (card == null) return;
        if (_exiledWith.Contains(card)) return;
        _exiledWith.Add(card);
    }

    /// <summary>Remove <paramref name="card"/> from the ledger. Returns true
    /// if the card was in the ledger.</summary>
    public bool RemoveExiledWith(ICard card)
    {
        if (card == null) return false;
        return _exiledWith.Remove(card);
    }
}

/// <summary>
/// "Discard your hand" activation cost (CR 701.16). Moves every card
/// currently in the activating player's hand Hand → Graveyard. Routes
/// through <see cref="ZoneService"/> when supplied (so
/// <see cref="CardMovedEvent"/> publishes for zone-change subscribers),
/// raw zone manipulation otherwise. An empty hand is a payable, no-op cost
/// (CR 701.16c — discarding zero cards is a legal payment).
/// </summary>
internal sealed class DiscardYourHandCost : ICost
{
    private readonly Player _expected;
    private readonly ZoneService? _zones;

    public DiscardYourHandCost(Player expected, ZoneService? zones)
    {
        _expected = expected ?? throw new ArgumentNullException(nameof(expected));
        _zones = zones;
    }

    public string Description => "Discard your hand";

    /// <summary>
    /// Always payable by the ability's controller — discarding an empty hand
    /// is legal (zero cards discarded).
    /// </summary>
    public bool CanPay(Player player) => player != null;

    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        // Snapshot before mutating to avoid collection-modified-during-
        // enumeration.
        var hand = player.Zones.Hand.GetCards().ToList();
        foreach (var c in hand)
        {
            if (_zones != null)
            {
                _zones.MoveCard(c, ZoneType.Hand, ZoneType.Graveyard, player);
            }
            else
            {
                player.Zones.Hand.RemoveCard(c);
                player.Zones.Graveyard.AddCard(c);
                if (c is Card concrete) concrete.SetZone(ZoneType.Graveyard);
            }
        }
    }
}
