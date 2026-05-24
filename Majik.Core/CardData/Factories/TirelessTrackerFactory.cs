using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tireless Tracker (Shadows over Innistrad, {2}{G}).
///
/// Creature — Human Scout 3/2. Oracle text:
///   "Whenever a land enters under your control, create a Clue token.
///    ({2}, Sacrifice this artifact: Draw a card. Clue is an artifact type.)
///    {2}, Sacrifice a Clue: Put a +1/+1 counter on Tireless Tracker."
///
/// ## Implemented (v1)
/// - 3/2 Creature — Human Scout, mana cost {2}{G}.
/// - <b>Landfall-style triggered ability</b> (CR 603.1 / 603.6a) over
///   <see cref="CardMovedEvent"/>: fires when any land enters the
///   battlefield under the controller's control. The resolved effect
///   creates a Clue token via <see cref="TokenFactory.CreateClue"/>.
///   Tireless Tracker is NOT a printed Landfall card — its trigger is
///   shaped identically though (CR 614.6 — the trigger sees the live
///   battlefield state because ZoneService publishes CardMovedEvent
///   AFTER the move completes). Token creation routes through the
///   supplied <see cref="ZoneService"/> when present so the Clue's own
///   ETB CardMovedEvent fires.
/// - <b>Activated ability {2}, Sacrifice a Clue: +1/+1 counter</b>. The
///   sacrifice cost is a <see cref="SacrificeAClueCost"/> instance,
///   exposed on the returned card via
///   <see cref="TirelessTrackerActivatedAbility.SacrificeChoice"/> so a
///   caller can pre-select the Clue to sac (mirrors Phyrexian Tower's
///   <c>SacrificeChoice</c> pattern). Resolution places a +1/+1 counter
///   on the Tracker itself (CR 122).
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt for the Clue to sacrifice</b>: v1 picks
///   the first Clue on the controller's battlefield deterministically.
///   Awaits the agent-prompt targeting system.
/// - <b>"Activate only as a sorcery"</b> — Tireless Tracker's printed
///   activated ability has NO sorcery-speed restriction (instant speed
///   on the official card), so nothing is deferred here for this card.
/// - <b>Sacrifice cost zone movement</b>: handled directly by
///   <see cref="SacrificeAClueCost"/> rather than relying on the
///   <see cref="AdditionalCost.Sacrifice"/> stub (whose Pay is a TODO).
/// </summary>
[CardName("Tireless Tracker")]
public static class TirelessTrackerFactory
{
    public const string CardName = "Tireless Tracker";
    public const string Cost = "{2}{G}";

    /// <summary>
    /// Construct Tireless Tracker with no live ZoneService / TriggerManager
    /// wiring. The landfall trigger is attached for shape but is not
    /// registered with a bus, and Clue tokens created by it bypass
    /// ZoneService. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Tireless Tracker. When <paramref name="zoneService"/> is
    /// supplied the Clue tokens are placed onto the battlefield via
    /// ZoneService so CardMovedEvent fires (other ETB-trigger subscribers
    /// observe the Clue's arrival). When <paramref name="triggers"/> is
    /// supplied the landfall trigger is registered with the bus so a
    /// CardMovedEvent for a land automatically queues the ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        AttachLandfallClueTrigger(card, owner, zoneService, triggers);
        AttachSacClueGrowAbility(card, owner);

        return card;
    }

    /// <summary>
    /// "Whenever a land enters under your control, create a Clue token."
    /// (CR 603.1 / 603.6a — ETB-style trigger over CardMovedEvent.)
    /// </summary>
    private static void AttachLandfallClueTrigger(
        Creature card,
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Land)) return false;
            return ReferenceEquals(e.Card.Controller, owner);
        });

        var clueEffect = new Effect(
            "Tireless Tracker — create a Clue token",
            () => TokenFactory.CreateClue(owner, zoneService));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { clueEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    /// <summary>
    /// "{2}, Sacrifice a Clue: Put a +1/+1 counter on Tireless Tracker."
    /// </summary>
    private static void AttachSacClueGrowAbility(Creature card, Player owner)
    {
        var sacCost = new SacrificeAClueCost();

        var counterEffect = new Effect(
            "Tireless Tracker — put a +1/+1 counter on itself",
            () => card.Counters.Add(CounterType.PlusOnePlusOne, 1));

        var ability = new TirelessTrackerActivatedAbility(
            source: card,
            controller: owner,
            sacrificeCost: sacCost,
            counterEffect: counterEffect);

        card.AddAbility(ability);
    }
}

/// <summary>
/// Specialised <see cref="ActivatedAbility"/> for Tireless Tracker's
/// "{2}, Sacrifice a Clue: +1/+1 counter" so callers can pre-select the
/// Clue to sacrifice via <see cref="SacrificeChoice"/> (mirrors the
/// <c>PhyrexianTowerManaAbility.SacrificeChoice</c> pattern).
/// </summary>
public sealed class TirelessTrackerActivatedAbility : ActivatedAbility
{
    /// <summary>
    /// Set the <see cref="SacrificeAClueCost.Target"/> before activation to
    /// choose which Clue to sacrifice. Defaults to the first Clue on the
    /// controller's battlefield when left unset (deterministic v1 pick).
    /// </summary>
    public SacrificeAClueCost SacrificeChoice { get; }

    public TirelessTrackerActivatedAbility(
        Cards.Permanent source,
        Player controller,
        SacrificeAClueCost sacrificeCost,
        IEffect counterEffect)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                sacrificeCost,
            },
            effects: new IEffect[] { counterEffect })
    {
        SacrificeChoice = sacrificeCost;
    }
}

/// <summary>
/// "Sacrifice a Clue" — activated-ability cost requiring the controller
/// to sacrifice a permanent they control with the
/// <see cref="CardSubtype.Clue"/> subtype (Clue is an artifact subtype,
/// CR 205.3g). Implements <see cref="ICost"/> so it slots into
/// <see cref="ActivatedAbility"/>'s cost list.
///
/// <see cref="Target"/> may be pre-set by the agent; otherwise the cost
/// picks the first eligible Clue deterministically (same v1 pattern as
/// <see cref="SacrificeAnotherCreatureCost"/>).
/// </summary>
public sealed class SacrificeAClueCost : ICost
{
    /// <summary>
    /// Optionally set by the agent / caller to indicate which Clue to
    /// sacrifice. When null the cost falls back to the first Clue on
    /// the controller's battlefield.
    /// </summary>
    public Cards.Permanent? Target { get; set; }

    public string Description => "sacrifice a Clue";

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Cards.Permanent>()
            .Any(p => p.HasSubtype(CardSubtype.Clue));
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target ?? player.Zones.Battlefield.GetCards()
            .OfType<Cards.Permanent>()
            .FirstOrDefault(p => p.HasSubtype(CardSubtype.Clue));

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no Clue on the controller's battlefield.");

        player.Zones.Battlefield.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
