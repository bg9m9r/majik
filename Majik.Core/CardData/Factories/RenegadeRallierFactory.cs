using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Renegade Rallier (Aether Revolt, {1}{G}{W}).
///
/// Creature — Human Warrior 3/2. Oracle text:
///   "Revolt — When Renegade Rallier enters, if a permanent you controlled
///    left the battlefield this turn, you may return target permanent card
///    with mana value 2 or less from your graveyard to the battlefield."
///
/// ## Implemented (v1)
/// - 3/2 Creature — Human Warrior, mana cost {1}{G}{W}, owner/controller
///   stamped to the supplied <see cref="Player"/>.
/// - <b>Revolt-gated ETB triggered ability (CR 603.1 / CR 603.4 /
///   CR 702.104a)</b>: the trigger fires whenever Renegade Rallier enters
///   the battlefield, but an intervening-if predicate
///   (<see cref="ITriggeredAbility.InterveningIf"/>) re-checks at
///   put-on-stack time and at resolution that revolt is active for the
///   controller — i.e. at least one permanent the controller controlled
///   left the battlefield this turn (<see cref="TurnState.RevoltActive"/>).
///   The intervening-if is null-safe: when no <see cref="TurnState"/> is
///   wired (shape / dispatcher tests), revolt is treated as inactive and
///   the trigger never places its effect on the stack.
/// - <b>Resolve effect</b>: returns the first eligible <i>permanent</i>
///   card with mana value ≤ 2 in the controller's graveyard to the
///   battlefield (same Sun Titan reanimate shape, gated to mv ≤ 2 instead
///   of ≤ 3). Routes through <see cref="ZoneService.MoveCard"/> when
///   supplied so ETB triggers / replacements on the reanimated permanent
///   fire (CR 603.6a); falls back to raw zone manipulation when no
///   service is supplied (shape-only path).
/// - <b>"You may"</b>: auto-accepted (same posture as Sun Titan,
///   Bloodghast — v1 simplification; an explicit yes/no prompt is
///   deferred until the agent-prompt surface exists).
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: v1 picks the first eligible permanent
///   card with mv ≤ 2 deterministically (printed-iteration order over the
///   controller's graveyard). Mirrors Sun Titan / Priest of Fell Rites
///   — the targeting subsystem cannot yet prompt the controller for one
///   of several candidates.
/// - <b>"You may"</b>: auto-accept rather than yes/no prompt (same gap
///   as Sun Titan / Bloodghast / Tireless Tracker).
/// </summary>
[CardName("Renegade Rallier")]
public static class RenegadeRallierFactory
{
    public const string CardName = "Renegade Rallier";
    public const string PrintedManaCost = "{1}{G}{W}";

    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Renegade Rallier with no live ZoneService / TurnState /
    /// TriggerManager wiring. The ETB trigger is attached for shape so
    /// structural tests can observe it, but the intervening-if reads no
    /// TurnState (revolt always inactive on this path) and the trigger is
    /// not registered with a bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, turnStateResolver: null, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Renegade Rallier with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at intervening-if / resolution time. When
    /// the callback is null or returns null, revolt is treated as inactive
    /// (the trigger never resolves into a reanimation).</param>
    /// <param name="zoneService">When supplied, the graveyard → battlefield
    /// move is routed through <see cref="ZoneService.MoveCard"/> so ETB
    /// triggers on the reanimated permanent fire (CR 603.6a). When null,
    /// the move is a raw zone manipulation suitable for shape tests.</param>
    /// <param name="triggers">When supplied, the ETB trigger is registered
    /// with the bus so a CardMovedEvent for Renegade Rallier entering the
    /// battlefield surfaces the ability as pending.</param>
    public static Creature Create(
        Player owner,
        Func<TurnState?>? turnStateResolver,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Revolt ETB trigger — CR 603.1 (ETB shape), CR 603.4 (intervening-
        // if checked at put-on-stack + resolution), CR 702.104a (revolt
        // active when a permanent you controlled left the battlefield this
        // turn).
        //
        // The effect itself doesn't re-check the revolt gate — the
        // intervening-if is the authoritative gate; by the time we reach
        // resolution the ability has already passed CR 603.4 (twice).
        // ----------------------------------------------------------------
        var reanimateEffect = new Effect(
            $"{CardName}: revolt — return target permanent card (mv ≤ 2) from graveyard",
            () => ReanimatePermanentCardWithMaxManaValue(
                owner,
                zoneService,
                maxManaValue: 2));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { reanimateEffect },
            interveningIf: () => IsRevoltActive(owner, turnStateResolver),
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// CR 702.104a — revolt is active for <paramref name="controller"/>
    /// when at least one permanent they controlled left the battlefield
    /// this turn. Null-safe: when no <see cref="TurnState"/> is wired,
    /// revolt is inactive.
    /// </summary>
    public static bool IsRevoltActive(
        Player controller,
        Func<TurnState?>? turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (turnStateResolver == null) return false;
        var turnState = turnStateResolver();
        return turnState != null && turnState.RevoltActive(controller);
    }

    /// <summary>
    /// Pick the first permanent card in <paramref name="controller"/>'s
    /// graveyard with mana value ≤ <paramref name="maxManaValue"/> and
    /// return it to the battlefield. Mirrors
    /// <c>SunTitanFactory.ReanimatePermanentPick</c> shape; gated to
    /// "permanent card" (CR 110.4 — artifact / creature / enchantment /
    /// land / planeswalker) by the <see cref="Permanent"/> filter.
    ///
    /// CR 117.x — a "may"/target-required effect with no valid candidate
    /// resolves as a no-op.
    /// </summary>
    private static void ReanimatePermanentCardWithMaxManaValue(
        Player controller,
        ZoneService? zoneService,
        int maxManaValue)
    {
        var pick = controller.Zones.Graveyard.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(c => c.ManaCostValue.TotalValue <= maxManaValue);

        if (pick == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Graveyard, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }
    }
}
