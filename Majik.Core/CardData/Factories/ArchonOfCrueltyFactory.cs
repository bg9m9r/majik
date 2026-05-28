using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Archon of Cruelty (Modern Horizons 2, {6}{B}{B}).
///
/// Creature — Archon 6/6. Oracle text (Scryfall verified):
///   "Flying.
///    Whenever this creature enters or attacks, target opponent sacrifices a
///    creature or planeswalker of their choice, discards a card, and loses 3
///    life. You draw a card and gain 3 life."
///
/// ## Implemented (v1)
/// - 6/6 Creature — Archon, mana cost {6}{B}{B} (mana value 8, black).
///   <see cref="CardSubtype.Archon"/> added to the enum for this factory.
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker — same
///   shape as <see cref="AirElementalFactory"/> / <see cref="CloudkinSeerFactory"/>.
/// - <b>ETB + attack triggered ability</b> (CR 603.1 / CR 603.6a / CR 508.1f):
///   "Whenever this creature enters or attacks, …" Implemented as TWO separate
///   <see cref="TriggeredAbility"/> instances sharing the same effect body —
///   one keyed on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> — the standard pattern for this
///   phrasing when no combined "enters or attacks" helper exists in
///   <see cref="Triggers"/>. Both register against the same
///   <see cref="TriggerManager"/> when provided.
///
/// ## Trigger effect (CR 701.16 / 701.8 / 119.3 / 120.1)
/// Target opponent's choice:
///   1. Sacrifice a creature or planeswalker they control (CR 701.16 — choice
///      belongs to the opponent; the filter includes cards of CardType.Creature
///      OR CardType.Planeswalker). No creatures/planeswalkers → no-op for this
///      step only; the remaining effect steps still execute.
///   2. Discard a card (CR 701.8 — choice belongs to the opponent; agent-driven
///      when available, deterministic first-card fallback otherwise).
///   3. Loses 3 life (CR 119.3).
/// Archon's controller:
///   4. Draws 1 card (CR 120.1).
///   5. Gains 3 life (CR 119.3).
///
/// ## Trigger shape — targeted
/// The printed oracle says "target opponent" — one chosen opponent is the
/// victim. Implemented as a 1..1 <see cref="TargetRequest"/> for "target
/// opponent" populated from the live game context. The target is set via
/// <see cref="TriggeredAbility.SetChosenTargets"/> before resolution.
/// When no target is chosen (shape-only / test fixtures) the effect body
/// no-ops cleanly.
///
/// ## Deferred (v1 gaps)
/// - <b>Opponent-chooses sac prompt UI</b>: agent receives the full
///   creature/planeswalker list. Portal decision panel wiring deferred.
/// - <b>Multi-opponent targeting</b>: "target opponent" limits the trigger to
///   exactly one chosen opponent. In multiplayer the caster picks which
///   opponent to target; v1 test fixtures target a single opponent directly.
///
/// CR references: 702.9 (Flying), 603.1 / 603.6a (triggered abilities),
/// 508.1f (attack trigger), 701.16 (sacrifice), 701.8 (discard), 119.3
/// (life loss / gain), 120.1 (draw).
/// </summary>
[CardName("Archon of Cruelty")]
public static class ArchonOfCrueltyFactory
{
    public const string CardName = "Archon of Cruelty";
    public const string PrintedManaCost = "{6}{B}{B}";
    public const int Power = 6;
    public const int Toughness = 6;
    public const int LifeSwing = 3;

    /// <summary>
    /// Construct Archon of Cruelty with ETB and attack triggers attached for
    /// shape inspection. Triggers are NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, targetAgent: null);

    /// <summary>
    /// Construct Archon of Cruelty with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus (parity with other ETB
    /// factories; reserved for future event routing).</param>
    /// <param name="triggers">TriggerManager — when supplied, both the ETB
    /// and attack triggers are registered so the appropriate domain events
    /// land them on the stack automatically.</param>
    /// <param name="targetAgent">Optional agent for the TARGET opponent's
    /// sacrifice and discard picks. When non-null each pick is agent-driven;
    /// null falls back to deterministic first-item picks.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        IPlayerAgent? targetAgent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Archon });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9). Keyword marker read by CombatAbilities.HasFlying
        // for evasion in the combat validator (same shape as AirElemental /
        // CloudkinSeer).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB + attack triggers share a single BuildEffect factory so the
        // body is defined once (CR 603.1 / 508.1f).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        TriggeredAbility? attackTrigger = null;

        IEffect BuildEffect(Func<TriggeredAbility?> getTrigger, string label) =>
            new Effect(
                $"{CardName}: {label} — target opponent sacs creature/planeswalker, discards, loses {LifeSwing}; you draw, gain {LifeSwing}",
                () => ResolveTriggerBody(getTrigger(), card, owner, targetAgent));

        // ----------------------------------------------------------------
        // ETB trigger — "Whenever this creature enters, …"
        // CR 603.1 / CR 603.6a. Fires on CardMovedEvent → Battlefield for
        // this card specifically.
        // ----------------------------------------------------------------
        var targetRequest = new TargetRequest(
            Description: "target opponent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { BuildEffect(() => etbTrigger, "ETB") },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — "Whenever this creature attacks, …"
        // CR 508.1f. Fires on CreatureAttacksEvent for this card.
        // ----------------------------------------------------------------
        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { BuildEffect(() => attackTrigger, "attack") },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    // --- Trigger body (CR 701.16 / 701.8 / 119.3 / 120.1) -----------------
    private static void ResolveTriggerBody(
        TriggeredAbility? trigger,
        Creature card,
        Player owner,
        IPlayerAgent? targetAgent)
    {
        var controller = card.Controller ?? owner;
        var opponent = ResolveTargetOpponent(trigger);
        if (opponent is null) return; // no target chosen → no-op

        // Step 1: opponent sacrifices a creature or planeswalker (CR 701.16).
        SacrificeCreatureOrPlaneswalker(opponent, targetAgent);

        // Step 2: opponent discards a card (CR 701.8).
        OpponentDiscards(opponent, targetAgent);

        // Step 3: opponent loses 3 life (CR 119.3).
        Fx.LoseLife(opponent, LifeSwing);

        // Step 4: controller draws 1 card (CR 120.1).
        Fx.DrawCards(controller, 1);

        // Step 5: controller gains 3 life (CR 119.3).
        Fx.GainLife(controller, LifeSwing);
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

    private static void SacrificeCreatureOrPlaneswalker(Player opponent, IPlayerAgent? targetAgent)
    {
        // CR 101.1 / 305.1 — "creature or planeswalker" = battlefield permanents
        // with CardType.Creature OR CardType.Planeswalker controlled by opponent.
        var sacCandidates = opponent.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker))
            .Cast<ICard>()
            .ToList();
        if (sacCandidates.Count == 0) return; // no-op for this step only.

        var sacPick = PickSacrificeTarget(opponent, sacCandidates, targetAgent);
        // CR 701.16 — sacrifice bypasses Indestructible (CR 702.12) and
        // regeneration (CR 701.15).
        Fx.Sacrifice(sacPick);
    }

    private static ICard PickSacrificeTarget(
        Player opponent,
        List<ICard> sacCandidates,
        IPlayerAgent? targetAgent)
    {
        if (targetAgent == null) return sacCandidates[0];

        var pick = targetAgent
            .ChooseFromBattlefieldAsync(opponent, sacCandidates, BotIntent.Removal)
            .GetAwaiter().GetResult();

        if (pick == null
            || pick.Zone != ZoneType.Battlefield
            || (!pick.HasType(CardType.Creature) && !pick.HasType(CardType.Planeswalker))
            || !ReferenceEquals(pick.Controller, opponent))
        {
            return sacCandidates[0];
        }
        return pick;
    }

    private static void OpponentDiscards(Player opponent, IPlayerAgent? targetAgent)
    {
        // CR 701.8 — opponent chooses what to discard.
        var hand = opponent.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return;

        ICard discardPick;
        if (targetAgent != null)
        {
            var pick = targetAgent
                .ChooseFromHandAsync(opponent, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            discardPick = (pick != null && pick.Zone == ZoneType.Hand) ? pick : hand[0];
        }
        else
        {
            discardPick = hand[0];
        }

        opponent.Zones.Hand.RemoveCard(discardPick);
        opponent.Zones.Graveyard.AddCard(discardPick);
        discardPick.SetZone(ZoneType.Graveyard);
    }
}
