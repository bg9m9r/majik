using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Combustion (Core Set 2020,
/// {2}{R}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall + the embedded seed):
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Whenever you and/or at least one permanent you control becomes the
///    target of a spell or ability an opponent controls, this enchantment
///    deals 2 damage to that player."
///
/// ## Implemented
/// - Enchantment shape with mana cost {2}{R}{R}, owner / controller wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Combustion up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
/// - <b>"Whenever you and/or at least one permanent you control becomes the
///   target of a spell or ability an opponent controls..."</b>
///   (CR 603.6c / 109.5 / 102.1) — a <see cref="TargetsChosenEvent"/> trigger
///   gated to (a) a targeting stack object whose controller is an OPPONENT of
///   Combustion's controller, AND (b) at least one chosen target being the
///   controller (a player) or a permanent that player controls. Same hook +
///   "you or a permanent you control" predicate as
///   <see cref="UnsettledMarinerFactory"/>; <see cref="TargetsChosenEvent"/>
///   is published by both <see cref="Majik.Core.Services.SpellCaster"/> and
///   <see cref="Majik.Core.Services.AbilityActivator"/>, so "a spell or
///   ability" is covered automatically. On resolution Combustion deals 2
///   damage to "that player" — the controller of the targeting spell/ability
///   (the opponent) — via <see cref="Fx.DealDamageAny(object,int)"/>.
/// </summary>
[CardName("Leyline of Combustion")]
public static class LeylineOfCombustionFactory
{
    public const string CardName = "Leyline of Combustion";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>
    /// Constructs Leyline of Combustion with no live runtime wiring (the
    /// shape / dispatcher path). The becomes-targeted trigger is attached to
    /// the card for shape observability but not registered with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs Leyline of Combustion. When <paramref name="triggers"/> is
    /// supplied the becomes-targeted trigger is registered so a matching
    /// <see cref="TargetsChosenEvent"/> surfaces it as pending.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.95 — Leyline keyword marker.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        // ----------------------------------------------------------------
        // "Whenever you and/or at least one permanent you control becomes the
        //  target of a spell or ability an opponent controls, this enchantment
        //  deals 2 damage to that player." (CR 603.6c / 109.5 / 102.1).
        //
        // Fires on TargetsChosenEvent where:
        //   (a) the targeting stack object's controller is an OPPONENT of
        //       Combustion's controller (CR 102.1 — "an opponent controls"),
        //   (b) some chosen target is Combustion's controller (the player) OR
        //       a permanent that player controls (CR 109.5).
        // "that player" = the opponent who controls the targeting object.
        // ----------------------------------------------------------------
        Player? capturedOpponent = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;

            // (a) opponent-controlled spell/ability gate (CR 102.1).
            var sourceController = e.StackObject.Controller;
            if (sourceController == null) return false;
            if (ReferenceEquals(sourceController, controller)) return false;

            // (b) target is you, or a permanent you control (CR 109.5).
            foreach (var t in e.Targets)
            {
                if (TargetMatchesYouOrYours(t, controller))
                {
                    capturedOpponent = sourceController;
                    return true;
                }
            }

            return false;
        });

        var damageEffect = new Effect(
            $"{CardName}: deal 2 damage to that player",
            () =>
            {
                var opponent = capturedOpponent;
                capturedOpponent = null;
                if (opponent == null) return;

                // CR 119 — 2 damage to "that player" (the controller of the
                // targeting spell/ability).
                Fx.DealDamageAny(opponent, 2);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 109.5 — does <paramref name="target"/> match "you or a permanent
    /// you control" for <paramref name="controller"/>? True when the target
    /// is the controller (the player) or a permanent that player controls.
    /// Mirrors <see cref="UnsettledMarinerFactory"/>'s predicate.
    /// </summary>
    private static bool TargetMatchesYouOrYours(ITarget target, Player controller)
    {
        if (target is not Target concrete) return false;

        switch (concrete.TargetType)
        {
            case TargetType.Player:
                return ReferenceEquals(concrete.GetPlayer(), controller);

            case TargetType.Permanent:
            case TargetType.Card:
                return concrete.TargetObject is Permanent perm
                       && ReferenceEquals(perm.Controller, controller);

            default:
                return false;
        }
    }
}
