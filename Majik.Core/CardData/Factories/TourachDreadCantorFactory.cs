using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tourach, Dread Cantor (Modern Horizons 2,
/// {1}{B}). Legendary Creature — Human Cleric 2/1.
///
/// ## Oracle text (Scryfall verified 2026-06)
///   "Kicker {B}{B} (You may pay an additional {B}{B} as you cast this
///    spell.)
///    Protection from white
///    Whenever an opponent discards a card, put a +1/+1 counter on Tourach.
///    When Tourach enters, if it was kicked, target opponent discards two
///    cards at random."
///
/// ## Base shape
/// Name / Legendary / Creature / Human Cleric / {1}{B} / 2/1 are
/// materialised from the embedded JSON definition
/// (<c>tourach-dread-cantor.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="ScourgeOfTheSkyclavesFactory"/> / <see cref="LoamLionFactory"/>.
/// The JSON carries no abilities; the four riders below are layered on here.
///
/// ## Implemented (v1)
/// - <b>Protection from white (CR 702.16)</b> — a single
///   <see cref="ProtectionAbility"/>("white") marker, read by
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> on the
///   combat / damage / target / attach gates (same shape as
///   <see cref="StormbreathDragonFactory"/>'s white half).
/// - <b>Kicker {B}{B} (CR 702.33)</b> — a real
///   <see cref="KickerAdditionalCost"/> via <see cref="BuildAdditionalCost"/>;
///   paying it during the cast flow stamps <see cref="Card.WasKicked"/> = true.
///   Registered in <see cref="KickerAltCostProbe.DefaultLookup"/> for bot
///   discovery (mirrors <see cref="ScourgeOfTheSkyclavesFactory"/>).
/// - <b>Opponent-discard trigger (CR 603.1 / CR 701.16a)</b> — "Whenever an
///   opponent discards a card, put a +1/+1 counter on Tourach." The engine
///   has no dedicated <c>DiscardedEvent</c>; discards funnel through
///   <see cref="CardMovedEvent"/> with <c>FromZone == Hand &amp;&amp;
///   ToZone == Graveyard</c> (same posture as
///   <see cref="ContainmentConstructFactory"/> / Necropotence). This trigger
///   gates to discards whose discarder (the moved card's owner) is NOT
///   Tourach's controller — i.e. an opponent (CR 102.1). On resolution one
///   <see cref="CounterType.PlusOnePlusOne"/> counter is placed via
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///   replacements (CR 614) can rewrite the count. CR 122.1 — fires once per
///   discarded card.
/// - <b>Kicked-ETB trigger (CR 603.1 / CR 603.4 / CR 702.33b)</b> — "When
///   Tourach enters, if it was kicked, target opponent discards two cards at
///   random." Keyed on <see cref="Triggers.OnEnterBattlefieldSelf"/>;
///   intervening-if on <see cref="Card.WasKicked"/> (CR 603.4 — not kicked =
///   the trigger never goes on the stack). One <see cref="TargetRequest"/>
///   ("target opponent"); on resolution the chosen opponent discards two
///   cards at random (CR 701.16e — discarder chooses nothing; the engine
///   picks uniformly from the per-game <see cref="GameRandom"/>, seedable /
///   replayable per CR 100.6). Fewer than two cards in hand discards what is
///   there (CR 701.16a).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + Protection marker only; the
///   two triggers are attached for shape tests but not registered with a
///   <see cref="TriggerManager"/>. This is the overload the dispatcher uses.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> — fully
///   wired: both triggers register so qualifying events auto-queue them, and
///   the +1/+1 counter routes through the replacement bus.
///
/// ## Notes
/// - "An opponent" / "you" (CR 102.1 / 109.5) — the discard trigger reads
///   Tourach's live <see cref="Permanent.Controller"/>, so a control-changing
///   effect re-homes the "you" reference and the opponent test follows it.
/// </summary>
[CardName("Tourach, Dread Cantor")]
public static class TourachDreadCantorFactory
{
    public const string CardName = "Tourach, Dread Cantor";
    public const string Slug = "tourach-dread-cantor";
    public const string KickerCostText = "{B}{B}";
    public const int DiscardAtRandomCount = 2;

    /// <summary>
    /// Construct Tourach with no live <see cref="TriggerManager"/> wiring.
    /// The Protection marker + both triggers are attached to the card shape
    /// for structural / dispatch tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Tourach with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the opponent-discard trigger
    /// and the kicked-ETB trigger register so qualifying events auto-queue
    /// them. When <paramref name="replacements"/> is supplied the +1/+1
    /// counter placement routes through the replacement bus (CR 614).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Human Cleric, {1}{B}, 2/1). The JSON carries no abilities.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Protection from white — CR 702.16. Quality marker read by the
        // Rules.Protection helpers (same shape as Stormbreath Dragon).
        // ----------------------------------------------------------------
        card.AddAbility(new ProtectionAbility("white"));

        // ----------------------------------------------------------------
        // Opponent-discard trigger — CR 603.1 / CR 701.16a / CR 122.1.
        //   "Whenever an opponent discards a card, put a +1/+1 counter on
        //    Tourach."
        // No dedicated DiscardedEvent exists; discards funnel through
        // CardMovedEvent (Hand → Graveyard). Gate to discards by a player
        // who is NOT Tourach's controller (an opponent — CR 102.1).
        // ----------------------------------------------------------------
        var discardCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Hand) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            var discarder = e.Card.Owner;
            if (discarder == null) return false;
            // "An opponent discards" — the discarder is anyone who is NOT
            // Tourach's controller (CR 102.1 / 109.5).
            return !ReferenceEquals(discarder, card.Controller ?? owner);
        });

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (an opponent discarded a card)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: discardCondition,
            effects: new IEffect[] { counterEffect },
            // CR 113.6 — the ability functions only while Tourach is on the
            // battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(discardTrigger);
        triggers?.RegisterTriggeredAbility(discardTrigger);

        // ----------------------------------------------------------------
        // Kicked-ETB trigger — CR 603.1 / CR 603.4 / CR 702.33b.
        //   "When Tourach enters, if it was kicked, target opponent discards
        //    two cards at random."
        // Fires on CardMovedEvent → Battlefield for this card; intervening-if
        // on Card.WasKicked (not kicked → never goes on the stack). One
        // "target opponent" request; the chosen opponent discards two cards
        // at random (CR 701.16e).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: target opponent discards two cards at random (kicked ETB)",
            () => ResolveKickedEtb(etbTrigger, card));

        var targetRequest = new TargetRequest(
            Description: "target opponent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.4 — queue-time intervening-if. Not kicked = the trigger
            // doesn't go on the stack at all.
            interveningIf: () => card.WasKicked,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// CR 702.33 — construct Tourach's kicker rider ({B}{B}) for the supplied
    /// <paramref name="card"/> instance. Layer the returned cost onto the
    /// cast to pay the kicker (same wiring shape as Scourge of the Skyclaves).
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    // --- Kicked-ETB resolution (CR 701.16e — discard at random) -----------

    /// <summary>
    /// Resolve the kicked-ETB trigger: the chosen target opponent discards
    /// two cards at random. CR 701.16e — the discarder chooses nothing; the
    /// engine picks uniformly at random from that player's current hand using
    /// the per-game RNG (CR 100.6 — seedable / replayable). Fewer than two
    /// cards in hand discards what is there (CR 701.16a).
    /// </summary>
    private static void ResolveKickedEtb(TriggeredAbility? trigger, Creature card)
    {
        // CR 603.4 — defensive re-check at resolution (mirrors Scourge /
        // Goblin Bushwhacker). A trigger that lost its kicked state between
        // queue and resolution does nothing.
        if (!card.WasKicked) return;

        var opponent = ResolveTargetOpponent(trigger);
        if (opponent is null) return; // no legal target chosen → no-op.

        var rng = GameRandomRegistry.Get(opponent);
        for (var i = 0; i < DiscardAtRandomCount; i++)
        {
            var hand = opponent.Zones.Hand.GetCards().ToList();
            if (hand.Count == 0) break;
            var pick = hand[rng.Next(hand.Count)];
            opponent.Zones.Hand.RemoveCard(pick);
            opponent.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        }
    }

    private static Player? ResolveTargetOpponent(TriggeredAbility? trigger)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }
        return trigger.ChosenTargets[0][0] as Player;
    }
}
