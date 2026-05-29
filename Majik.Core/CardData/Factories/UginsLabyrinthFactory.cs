using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ugin's Labyrinth (The Brothers' War).
///
/// Land. Oracle text:
///   "Imprint — When this land enters, you may exile a colorless card with
///    mana value 7 or greater from your hand.
///    {T}: Add {C}. If a card is exiled with this land, add {C}{C} instead.
///    {T}: Return the exiled card to its owner's hand."
///
/// ## Implemented (v1)
/// - Land identity (no printed supertypes / subtypes — non-basic).
/// - <b>Imprint ETB trigger (CR 603.1 / CR 702.49)</b> over
///   <see cref="CardMovedEvent"/> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolve the
///   controller "may exile a colorless card with mana value 7 or greater
///   from your hand". The optional choice is routed through the registered
///   <see cref="IPlayerAgent"/>:
///     - <see cref="IPlayerAgent.ChooseYesNoAsync"/> tagged
///       <see cref="BotIntent.CheatIntoPlay"/> gates the "you may" (CR 117.x);
///       agent-less callers default to YES (the upside branch — doubling the
///       land's mana — is strictly beneficial; mirrors the auto-accept
///       posture of <see cref="SanctumOfUginFactory"/> / MentorOfTheMeek).
///     - <see cref="IPlayerAgent.ChooseFromHandAsync"/> picks which eligible
///       card to exile; the candidate list is pre-filtered to colorless
///       (CR 105 — <c>CardColors.GetColors(c).Count == 0</c>) cards with
///       mana value ≥ 7 (CR 202.3 — <c>ManaCostValue.TotalValue >= 7</c>).
///   The chosen card moves Hand → Exile and is recorded on the land via
///   <see cref="Permanent.AddImprinted"/> (CR 702.49 imprint storage).
///   When no eligible card exists, or the agent declines, nothing is exiled.
/// - <b>{T}: Add {C} / {C}{C}</b> — a single conditional
///   <see cref="ManaAbility"/> built from a <c>Func&lt;ManaCost&gt;</c>
///   generator (CR 605.1 — mana ability, doesn't use the stack). The
///   generator inspects <see cref="Permanent.ImprintedCards"/> at activation
///   time: empty ⇒ {C}; non-empty ("if a card is exiled with this land") ⇒
///   {C}{C}. {C} folds into the generic bucket per
///   <see cref="ManaCost.Parse"/>.
/// - <b>{T}: Return the exiled card to its owner's hand</b> —
///   an <see cref="ActivatedAbility"/> (CR 605 — not a mana ability; uses
///   the stack) with a {T} cost. On resolve it moves every imprinted card
///   from Exile to its owner's Hand and clears the imprint
///   (<see cref="Permanent.ClearImprinted"/>), so the mana ability reverts
///   to {C}.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached, not
///   registered (no event bus). Suitable for identity / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. ETB trigger registered with <paramref name="triggers"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Spend-restriction tagging</b>: the produced {C}/{C}{C} carries no
///   provenance tag (the card has no spend restriction anyway — colorless
///   mana is unrestricted), so nothing to defer here.
/// - <b>Owner-vs-controller of returned card</b>: "to its owner's hand"
///   uses each imprinted card's <see cref="Card.Owner"/> directly (correct
///   per CR 109.5 / 400.3), which differs from the controller only if the
///   card was exiled from an opponent — impossible here since the card is
///   exiled "from your hand", so owner == controller in practice.
/// </summary>
[CardName("Ugin's Labyrinth")]
public static class UginsLabyrinthFactory
{
    public const string CardName = "Ugin's Labyrinth";

    /// <summary>
    /// Construct Ugin's Labyrinth with no live event-bus / trigger wiring.
    /// The ETB trigger is attached but not registered. Suitable for
    /// identity / dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Ugin's Labyrinth with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the Imprint ETB trigger is
    /// registered so <see cref="CardMovedEvent"/>s published on the bus
    /// automatically route it to the stack.
    /// </summary>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}. If a card is exiled with this land, add {C}{C}
        //   instead.
        //
        // CR 605.1 — mana ability; doesn't use the stack. The conditional
        // amount is computed at activation time from the land's imprint
        // store (CR 702.49): empty ⇒ {C}, non-empty ⇒ {C}{C}.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            land,
            owner,
            manaGenerator: () => land.ImprintedCards.Count > 0
                ? ManaCost.Parse("CC")
                : ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Return the exiled card to its owner's hand.
        //
        // CR 605 — NOT a mana ability (it doesn't add mana); uses the
        // stack. {T} is the only cost. On resolve, move each imprinted card
        // Exile → owner's Hand and clear the imprint so the mana ability
        // reverts to {C}.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return the exiled card to its owner's hand",
            () =>
            {
                if (land.ImprintedCards.Count == 0) return;

                // Snapshot — ClearImprinted mutates the backing list.
                foreach (var exiled in land.ImprintedCards.ToList())
                {
                    var cardOwner = exiled.Owner;
                    if (cardOwner == null) continue;

                    // Move from wherever it sits (Exile) to owner's hand.
                    // CR 400.7 — only act if the card is still in exile;
                    // guards against the card having been moved by another
                    // effect since it was imprinted.
                    if (exiled.Zone != ZoneType.Exile) continue;
                    cardOwner.Zones.Exile.RemoveCard(exiled);
                    cardOwner.Zones.Hand.AddCard(exiled);
                    exiled.SetZone(ZoneType.Hand);
                }

                land.ClearImprinted();
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { returnEffect }));

        // ----------------------------------------------------------------
        // Imprint — When this land enters, you may exile a colorless card
        //   with mana value 7 or greater from your hand.
        //
        // CR 603.1 — ETB triggered ability over CardMovedEvent.
        // CR 702.49 — the exiled card is "exiled with" this land (imprint).
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(land);

        var etbEffect = new Effect(
            $"{CardName}: you may exile a colorless MV7+ card from your hand",
            () =>
            {
                var controller = land.Controller ?? owner;
                var agent = AgentRegistry.Get(controller);

                // Eligible: colorless (CR 105 — empty colour set) hand cards
                // with mana value ≥ 7 (CR 202.3 — printed-cost total value).
                var candidates = controller.Zones.Hand.GetCards()
                    .Where(c => CardColors.GetColors(c).Count == 0
                                && ManaValueOf(c) >= 7)
                    .ToList();

                if (candidates.Count == 0) return; // nothing legal to exile

                // "You may" — CR 117.x. Default YES when agent-less (the
                // mana upside is strictly beneficial).
                bool wantsToExile = agent == null
                    ? true
                    : agent.ChooseYesNoAsync(
                        $"Exile a colorless card (MV 7+) from your hand to imprint on {CardName}?",
                        BotIntent.CheatIntoPlay).GetAwaiter().GetResult();

                if (!wantsToExile) return;

                ICard? pick = agent != null
                    ? agent.ChooseFromHandAsync(
                            controller, candidates, BotIntent.CheatIntoPlay)
                        .GetAwaiter().GetResult()
                    : candidates[0];

                if (pick == null) return;

                controller.Zones.Hand.RemoveCard(pick);
                controller.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);

                // CR 702.49 — record the card as "exiled with" the land.
                land.AddImprinted(pick);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return land;
    }

    /// <summary>
    /// CR 202.3 — mana value of a card derived from its printed mana cost.
    /// The <see cref="ICard"/> interface exposes the cost as a string only,
    /// so parse it (same approach <see cref="SanctumOfUginFactory"/> uses for
    /// its spell-cast filter). Empty cost ⇒ mana value 0.
    /// </summary>
    private static int ManaValueOf(ICard card)
    {
        var cost = card.ManaCost;
        return string.IsNullOrEmpty(cost) ? 0 : ManaCost.Parse(cost).TotalValue;
    }
}
