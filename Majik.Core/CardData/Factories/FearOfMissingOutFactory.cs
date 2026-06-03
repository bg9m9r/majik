using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fear of Missing Out (Duskmourn, {1}{R}).
///
/// Enchantment Creature — Nightmare 2/3. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, discard a card, then draw a card.
///    Delirium — Whenever this creature attacks for the first time each turn,
///    if there are four or more card types among cards in your graveyard,
///    untap target creature. After this phase, there is an additional combat
///    phase."
///
/// ## Implementation
///
/// - 2/3 red Nightmare Enchantment Creature, mana cost {1}{R}, via
///   <see cref="PermanentBuilders.EnchantmentCreature"/> (CR 301.1 / 302.1 —
///   dual Creature + Enchantment type, same posture as the other enchantment
///   creatures, deferral #10).
/// - <b>ETB rummage (CR 603.2)</b>: a <see cref="TriggeredAbility"/> on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — "discard a card, then draw
///   a card." NOT a "you may" / "if you do": the discard is mandatory (when the
///   hand is non-empty) and the draw is unconditional. v1 deterministic discard
///   picks the last card in hand (matching Artist's Talent / Faithless Looting).
/// - <b>Delirium combat trigger (CR 506.4 + CR 702.105 + CR 603.2-3)</b>: a
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnAttackSelf"/>,
///   gated "first time each turn" (a boxed once-per-turn cell reset by
///   <see cref="TurnStartedEvent"/>) and by an intervening-if that there are
///   four or more card types among the controller's graveyard
///   (<see cref="UnholyHeatFactory.IsDeliriumActive"/>, CR 702.105). On
///   resolution it:
///     * untaps the chosen <c>target creature</c> (CR 701.20a) — modelled as a
///       <see cref="TargetRequest"/> with MinTargets 0 so the additional-combat
///       half still lands when no creature is available / chosen to untap;
///     * enqueues an additional combat phase on the per-game
///       <see cref="AdditionalCombatRegistryProvider"/> queue — the SAME
///       instance <see cref="TurnDriver"/> drains after the current combat
///       (CR 506.4 — "After this phase, there is an additional combat phase").
///       This is the card-triggered insertion path for an extra combat (the
///       Aggravated Assault / Combat Celebrant family).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (the <see cref="NamedCardFactory"/>
///   dispatch target). Both triggers attached; without a
///   <see cref="TriggerManager"/> the bus won't fire them.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully wired.
/// </summary>
[CardName("Fear of Missing Out")]
public static class FearOfMissingOutFactory
{
    public const string CardName = "Fear of Missing Out";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Fear of Missing Out with no live runtime wiring (the dispatch
    /// target). Both triggers are attached to the card shape.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Fear of Missing Out with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both triggers are registered.</param>
    /// <param name="eventBus">When supplied, the Delirium trigger's
    /// once-per-turn gate is reset on each <see cref="TurnStartedEvent"/>.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = PermanentBuilders.EnchantmentCreature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Nightmare });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB — "discard a card, then draw a card." (CR 603.2.)
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: discard a card, then draw a card (when this enters)",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 701.16 — discard a card. v1 deterministic pick = last in
                // hand; empty hand discards nothing (CR 608.2b — do as much as
                // possible).
                var pick = controller.Zones.Hand.GetCards().LastOrDefault();
                if (pick != null)
                {
                    controller.Zones.Hand.RemoveCard(pick);
                    controller.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }

                // CR 121.1 — "then draw a card" (unconditional).
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
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
        // Delirium combat trigger — "Whenever this creature attacks for the
        // first time each turn, if there are four or more card types among
        // cards in your graveyard, untap target creature. After this phase,
        // there is an additional combat phase." (CR 506.4 / 702.105 / 603.2-3.)
        // ----------------------------------------------------------------
        // CR 603.2 — "for the first time each turn." Boxed cell shared by the
        // condition (sets it on the first matching attack) + the
        // TurnStartedEvent reset handler.
        var firedThisTurn = new bool[] { false };

        var deliriumCondition = new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
        {
            if (firedThisTurn[0]) return false;
            if (!ReferenceEquals(e.Attacker, card)) return false;
            firedThisTurn[0] = true;
            return true;
        });

        TriggeredAbility? deliriumTrigger = null;

        var deliriumEffect = new Effect(
            $"{CardName}: untap target creature; after this phase, an additional combat phase",
            () =>
            {
                // CR 701.20a — untap the chosen target creature (if any was
                // available / chosen; MinTargets 0 lets the extra-combat half
                // land regardless).
                var chosen = deliriumTrigger?.ChosenTargets;
                if (chosen is { Count: > 0 } && chosen[0].Count > 0
                    && chosen[0][0] is Creature target && target.IsTapped)
                {
                    target.Untap();
                }

                // CR 506.4 — "After this phase, there is an additional combat
                // phase." Enqueue on the per-game queue TurnDriver drains; the
                // turn loop re-runs combat (and the postcombat main) for it.
                AdditionalCombatRegistryProvider.Current.EnqueueAdditional();
            });

        deliriumTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: deliriumCondition,
            effects: new IEffect[] { deliriumEffect },
            // CR 702.105 — Delirium intervening-if: four-or-more card types in
            // the controller's graveyard, checked both on trigger and on
            // resolution (CR 603.4).
            interveningIf: () => UnholyHeatFactory.IsDeliriumActive(card.Controller ?? owner),
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // "untap target creature" — any creature on the battlefield
                // (CR 115). MinTargets 0 so the additional-combat half still
                // lands when there is no creature to untap.
                new TargetRequest(
                    Description: "target creature to untap",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(deliriumTrigger);
        triggers?.RegisterTriggeredAbility(deliriumTrigger);

        // CR 603.2 — reset the once-per-turn gate at the start of each turn.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => firedThisTurn[0] = false);
        }

        return card;
    }
}
