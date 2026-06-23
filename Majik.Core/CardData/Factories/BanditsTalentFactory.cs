using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bandit's Talent (Outlaws of Thunder Junction,
/// {1}{B}, Enchantment — Class).
///
/// Oracle text (verified vs Scryfall):
///   "(Gain the next level as a sorcery to add its ability.)
///    When this Class enters, each opponent discards two cards unless they
///      discard a nonland card.
///    {B}: Level 2
///    At the beginning of each opponent's upkeep, if that player has one or
///      fewer cards in hand, they lose 2 life.
///    {3}{B}: Level 3
///    At the beginning of your draw step, draw an additional card for each
///      opponent who has one or fewer cards in hand."
///
/// ## Implementation (full Class leveling — CR 716)
/// Mirrors <see cref="StormchasersTalentFactory"/> / <see cref="ArtistsTalentFactory"/>:
/// an Enchantment — Class shell with a <see cref="ClassState"/> side-table
/// (MaxLevel=3, per-level costs {B} / {3}{B}) and two sorcery-speed level-up
/// activated abilities (CR 716.3). Bandit's Talent's three printed abilities:
///
/// - <b>Level 1 — ETB discard</b> (CR 603.6a): "When this Class enters, each
///   opponent discards two cards unless they discard a nonland card." A
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnEnterBattlefieldSelf"/>.
///   The each-opponent set is read from the LIVE resolution context via
///   <see cref="ContextOpponents"/> (the resolver-null bug-class fix —
///   #2540 / #2549). Per CR 701.8 each opponent makes the discard choice;
///   v1 deterministic policy: an opponent that holds a nonland card discards
///   exactly one nonland (strictly less loss than discarding two cards);
///   an opponent with only lands discards two cards. Real agent-driven choice
///   awaits the prompt surface (same posture as the Talent-family "may"s).
///
/// - <b>Level 2 — each-opponent's-upkeep punisher</b> (CR 603.1): "At the
///   beginning of each opponent's upkeep, if that player has one or fewer
///   cards in hand, they lose 2 life." A <see cref="TriggeredAbility"/> over
///   <see cref="StepStartedEvent"/> for the Upkeep step of ANY player other
///   than the controller (an opponent — CR 102.1). The triggering opponent is
///   captured off the condition predicate (mirrors
///   <see cref="SheoldredWhisperingOneFactory"/>). Gated on
///   <see cref="ClassState.CurrentLevel"/> &gt;= 2 (CR 716.2 — the level-N
///   ability is inactive below level N). The "one or fewer cards in hand"
///   clause is an intervening-if RE-CHECKED at resolution (CR 603.4) inside
///   the effect body, reading that opponent's current hand size.
///
/// - <b>Level 3 — draw-step card advantage</b> (CR 603.1): "At the beginning
///   of your draw step, draw an additional card for each opponent who has one
///   or fewer cards in hand." A <see cref="TriggeredAbility"/> over the
///   controller's own Draw step (<see cref="Triggers.OnStepBegin"/>), gated on
///   level &gt;= 3. On resolution it counts opponents (read from the live
///   context) with ≤1 card in hand and draws that many additional cards
///   (<see cref="Fx.DrawCards"/>).
///
/// ## Deferred (v1 gaps — shared with the Class / discard family)
/// - <b>ETB discard choice</b>: deterministic (discard one nonland if able,
///   else two cards). Real opponent-agent choice (which card, lands-vs-pay)
///   awaits the agent prompt surface — same posture as Sheoldred's
///   sacrifice-a-creature pick and the Talent-family "may"s.
/// </summary>
[CardName("Bandit's Talent")]
public static class BanditsTalentFactory
{
    public const string CardName = "Bandit's Talent";
    public const string PrintedManaCost = "{1}{B}";
    public const string Level2Cost = "{B}";
    public const string Level3Cost = "{3}{B}";
    public const int LifeLossPerUpkeep = 2;

    /// <summary>
    /// Construct Bandit's Talent with no live TriggerManager / EventBus wiring.
    /// All three triggered abilities + the two level-up activated abilities are
    /// attached to the card for shape inspection; tests resolve them through a
    /// live <see cref="Majik.Core.Game.GameContext"/>.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Bandit's Talent with optional runtime services. When
    /// <paramref name="triggers"/> is supplied all three triggered abilities
    /// (ETB + Level-2 upkeep + Level-3 draw step) are registered for bus-driven
    /// firing. When <paramref name="eventBus"/> is supplied, level-up
    /// resolutions publish <see cref="ClassLevelUpEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Class });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Class state binder (CR 716). MaxLevel=3, per-level costs {B} / {3}{B}.
        // ----------------------------------------------------------------
        var classState = new ClassState(
            maxLevel: 3,
            levelUpCosts: new[]
            {
                ManaCost.Parse(Level2Cost),
                ManaCost.Parse(Level3Cost),
            });

        if (eventBus != null)
        {
            classState.OnLevelUp = (from, to) =>
                eventBus.Publish(new ClassLevelUpEvent(card, card.Controller ?? owner, from, to));
        }

        card.AttachClassState(classState);

        // ----------------------------------------------------------------
        // Level 1 — ETB discard (CR 603.6a).
        //   "When this Class enters, each opponent discards two cards unless
        //    they discard a nonland card."
        // Each-opponent set read from the live resolution context
        // (ContextOpponents) — the resolver-null fix. v1 deterministic
        // discard choice: discard one nonland if able, else two cards.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: each opponent discards two cards unless they discard a nonland",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                foreach (var opponent in ContextOpponents.Of(ctx, controller))
                {
                    // CR 701.8 — each opponent makes the choice. v1: the
                    // opponent discards one nonland card if they have one
                    // (strictly less loss than discarding two), otherwise two
                    // cards. "Last in hand" pick mirrors the Talent family's
                    // deterministic discard policy.
                    var nonland = opponent.Zones.Hand.GetCards()
                        .LastOrDefault(c => !c.HasType(CardType.Land));

                    if (nonland != null)
                    {
                        Fx.DiscardCard(opponent, nonland, wasCost: false);
                    }
                    else
                    {
                        Fx.Discard(opponent, 2);
                    }
                }

                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Level-up activated abilities — CR 716.4 (sequential), sorcery speed
        // (CR 716.3). Mirrors StormchasersTalentFactory.
        // ----------------------------------------------------------------
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 2));
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 3));

        // ----------------------------------------------------------------
        // Level 2 — each-opponent's-upkeep punisher (CR 603.1).
        //   "At the beginning of each opponent's upkeep, if that player has
        //    one or fewer cards in hand, they lose 2 life."
        // Fires on the Upkeep step of ANY player other than the controller
        // (CR 102.1). The triggering opponent is captured off the condition
        // predicate (mirrors SheoldredWhisperingOneFactory). Gated on level
        // >= 2 (CR 716.2). The "one or fewer cards" clause is an
        // intervening-if RE-CHECKED at resolution (CR 603.4) in the body.
        // ----------------------------------------------------------------
        Player? upkeepOpponent = null;

        var upkeepCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
        {
            if (e.StepType != StepStateType.Upkeep) return false;
            // CR 102.1 — an opponent is any player other than the controller.
            if (ReferenceEquals(e.Player, card.Controller ?? owner)) return false;
            // CR 716.2 — the Level-2 ability is inactive below level 2.
            if (classState.CurrentLevel < 2) return false;
            upkeepOpponent = e.Player;
            return true;
        });

        var upkeepEffect = new Effect(
            $"{CardName}: that opponent loses {LifeLossPerUpkeep} life if they have ≤1 card in hand",
            ctx =>
            {
                // CR 716.2 — the Level-2 ability is inactive below level 2.
                // Re-checked in the body (in addition to the condition gate) so
                // a directly-resolved effect still respects the level gate.
                if (classState.CurrentLevel < 2) return ValueTask.CompletedTask;

                // Prefer the triggering player threaded through the context
                // (prod path), falling back to the closure-captured opponent
                // (factory-direct tests). CR 603.3 — "that player".
                var victim = ctx?.TriggeringPlayer ?? upkeepOpponent;
                if (victim == null) return ValueTask.CompletedTask;

                // CR 603.4 — re-check the intervening-if at resolution: only
                // lose life if the player still has one or fewer cards in hand.
                if (victim.Zones.Hand.GetCards().Count() > 1) return ValueTask.CompletedTask;

                victim.LoseLife(LifeLossPerUpkeep);
                return ValueTask.CompletedTask;
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: upkeepCondition,
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Level 3 — draw-step card advantage (CR 603.1).
        //   "At the beginning of your draw step, draw an additional card for
        //    each opponent who has one or fewer cards in hand."
        // Fires on the controller's OWN Draw step. Gated on level >= 3
        // (CR 716.2). Counts opponents (read from the live context) with ≤1
        // card in hand and draws that many additional cards.
        // ----------------------------------------------------------------
        var drawStepEffect = new Effect(
            $"{CardName}: draw an additional card per opponent with ≤1 card in hand",
            ctx =>
            {
                // CR 716.2 — the Level-3 ability is inactive below level 3.
                // Re-checked in the body (in addition to the trigger's
                // interveningIf) so a directly-resolved effect still respects
                // the level gate.
                if (classState.CurrentLevel < 3) return ValueTask.CompletedTask;

                var controller = card.Controller ?? owner;
                var extra = ContextOpponents.Of(ctx, controller)
                    .Count(o => o.Zones.Hand.GetCards().Count() <= 1);

                if (extra > 0)
                {
                    Fx.DrawCards(controller, extra);
                }

                return ValueTask.CompletedTask;
            });

        var drawStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.Draw),
            effects: new IEffect[] { drawStepEffect },
            interveningIf: () => classState.CurrentLevel >= 3,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawStepTrigger);
        triggers?.RegisterTriggeredAbility(drawStepTrigger);

        return card;
    }

    /// <summary>
    /// Build the "Level up to <paramref name="targetLevel"/>" sorcery-speed
    /// activated ability (CR 716.3 / 716.4). Mirrors
    /// <see cref="StormchasersTalentFactory"/>.
    /// </summary>
    private static ActivatedAbility BuildLevelUpAbility(
        Enchantment card, Player owner, ClassState classState, int targetLevel)
    {
        var cost = classState.CostFor(targetLevel);

        var effect = new Effect(
            $"{CardName}: level up to {targetLevel}",
            () => classState.LevelUpTo(targetLevel));

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: new IEffect[] { effect },
            sorcerySpeed: true);
    }
}
