using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kroxa, Titan of Death's Hunger (Theros Beyond
/// Death, {B}{R}). Legendary Creature — Elder Giant 6/6.
///
/// ## Card text (Scryfall verified)
/// "When Kroxa enters, sacrifice it unless it escaped.
///  Whenever Kroxa enters or attacks, each opponent discards a card, then
///  each opponent who didn't discard a nonland card this way loses 3 life.
///  Escape—{B}{B}{R}{R}, Exile five other cards from your graveyard. (You
///  may cast this card from your graveyard for its escape cost.)"
///
/// ## Base shape
/// Name / Creature / Elder Giant subtypes / {B}{R} / 6/6 / Legendary are
/// materialised from the embedded JSON definition
/// (<c>kroxa-titan-of-deaths-hunger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="StormscaleScionFactory"/>. The three printed behaviours are
/// layered on here because the JSON ability schema doesn't yet express
/// self-sacrifice triggers, each-opponent discard/drain, or Escape.
///
/// ## Implemented (v1)
/// - <b>Self-sacrifice ETB trigger (CR 603.1 / CR 701.16 / CR 702.138b)</b>:
///   "When Kroxa enters, sacrifice it unless it escaped." Reads the
///   cast-time <see cref="Card.WasCastForEscape"/> stamp set by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used
///   <see cref="EscapeAlternativeCost"/> — hardcast Kroxa is sacrificed,
///   escaped Kroxa stays. Identical shape to
///   <see cref="UroTitanFactory"/>'s sac trigger.
/// - <b>Enters-or-attacks triggered ability (CR 603.1 + CR 508.1f)</b>:
///   "Whenever Kroxa enters or attacks, each opponent discards a card, then
///   each opponent who didn't discard a nonland card this way loses 3 life."
///   Two <see cref="TriggeredAbility"/> instances sharing one effect body —
///   one keyed on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> — the standard pattern for
///   "enters or attacks" (same as <see cref="ArchonOfCrueltyFactory"/>).
///   Each opponent is enumerated via the <c>opponentResolver</c> delegate
///   (same "each opponent" pattern as
///   <see cref="VitoThornOfTheDuskRoseFactory"/>). For each opponent:
///     1. They discard a card (CR 701.8 — the discarding player chooses;
///        agent-driven when supplied, deterministic first-card fallback).
///     2. The drain (CR 119.3) hits IFF they did NOT discard a nonland card
///        this way — i.e. they discarded a land, or had an empty hand and
///        couldn't discard at all.
///
/// - <b>Escape (CR 702.138)</b>: cast-from-graveyard alt cost
///   ({B}{B}{R}{R}, exile five OTHER graveyard cards), wired via
///   <see cref="EscapeAlternativeCost"/>. <see cref="BuildAlternativeCost"/>
///   returns the bound instance; the bot's cast enumeration discovers it
///   through <see cref="EscapeAltCostProbe.DefaultLookup"/> (Kroxa is in the
///   ship list there). The sacrifice rider consults
///   <see cref="Card.WasCastForEscape"/> exactly like Uro/Phlage.
///
/// ## Deferred (v1 gaps)
/// - <b>Discard-choice prompt UI</b>: the opponent picks what to discard.
///   v1 is agent-driven when an <c>opponentAgent</c> is supplied, else
///   deterministic first-card — same gap as
///   <see cref="ArchonOfCrueltyFactory"/>.
/// </summary>
[CardName("Kroxa, Titan of Death's Hunger")]
public static class KroxaTitanFactory
{
    public const string CardName = "Kroxa, Titan of Death's Hunger";
    public const string Slug = "kroxa-titan-of-deaths-hunger";

    /// <summary>CR 702.138 — printed Escape mana cost: {B}{B}{R}{R}.</summary>
    public const string EscapeManaCost = "{B}{B}{R}{R}";

    /// <summary>CR 702.138a — Escape rider: exile five OTHER cards from
    /// your graveyard.</summary>
    public const int EscapeExileCount = 5;

    /// <summary>CR 119.3 — drain amount for an opponent who didn't discard
    /// a nonland card this way.</summary>
    public const int LifeLoss = 3;

    /// <summary>
    /// CR 702.138 — Kroxa's printed Escape alt-cost ({B}{B}{R}{R}, exile
    /// five OTHER graveyard cards). Discovered by the cast pipeline via
    /// <see cref="EscapeAltCostProbe.DefaultLookup"/>; the ETB sacrifice
    /// trigger reads the resulting <see cref="Card.WasCastForEscape"/>
    /// stamp to skip the sacrifice when Kroxa escaped.
    /// </summary>
    public static EscapeAlternativeCost BuildAlternativeCost() =>
        new(ManaCost.Parse(EscapeManaCost), EscapeExileCount);

    /// <summary>
    /// Construct Kroxa with no live wiring. Both triggers are attached for
    /// shape inspection (not registered with a <see cref="TriggerManager"/>);
    /// the enters-or-attacks body no-ops cleanly without an opponent
    /// resolver. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, triggers: null, opponentAgent: null);

    /// <summary>
    /// Construct Kroxa with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Returns the live opponent list at
    /// resolution time. "Each opponent" iterates whatever this yields,
    /// excluding the controller (CR 800.4). Null → the discard/drain body
    /// no-ops (shape path).</param>
    /// <param name="triggers">TriggerManager — when supplied both the sac
    /// trigger and the enters-or-attacks triggers are registered so the
    /// appropriate domain events land them on the stack automatically.</param>
    /// <param name="opponentAgent">Optional agent for each opponent's
    /// discard pick (CR 701.8 — the discarding player chooses). Null falls
    /// back to a deterministic first-card pick.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        TriggerManager? triggers,
        IPlayerAgent? opponentAgent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elder Giant, {B}{R}, 6/6, Legendary). No abilities in the JSON —
        // the three printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Self-sacrifice ETB trigger — CR 603.1 / CR 701.16 / CR 702.138b.
        //   "When Kroxa enters, sacrifice it unless it escaped."
        // Hardcast Kroxa lacks the WasCastForEscape stamp → sacrificed;
        // escaped Kroxa skips the sacrifice and stays on the battlefield.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice unless escaped",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.WasCastForEscape) return; // CR 702.138b — escaped gate.
                // CR 701.16 — sacrifice bypasses Indestructible / regeneration.
                Fx.Sacrifice(card);
            });

        var sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        // ----------------------------------------------------------------
        // Shared enters-or-attacks body — each opponent discards, then each
        // opponent who didn't discard a nonland card this way loses 3 life.
        // CR 701.8 (discard) + CR 119.3 (life loss).
        // ----------------------------------------------------------------
        IEffect BuildDiscardDrainEffect(string label) =>
            new Effect(
                $"{CardName}: {label} — each opponent discards a card, then each opponent who didn't discard a nonland card this way loses {LifeLoss} life",
                () => ResolveDiscardDrain(owner, card, opponentResolver, opponentAgent));

        // ETB trigger — CR 603.1.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildDiscardDrainEffect("ETB") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f. Same body as ETB.
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildDiscardDrainEffect("attack") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    // -----------------------------------------------------------------------
    // Discard + conditional drain — CR 701.8 / CR 119.3.
    // "each opponent discards a card, then each opponent who didn't discard a
    //  nonland card this way loses 3 life."
    //
    // CR 608.2 sequencing: ALL opponents discard first, THEN the drain is
    // applied — the "this way" clause keys off the discards from THIS
    // resolution. We snapshot each opponent's discard result, then apply the
    // life loss in a second pass.
    // -----------------------------------------------------------------------
    private static void ResolveDiscardDrain(
        Player owner,
        Creature card,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IPlayerAgent? opponentAgent)
    {
        var controller = card.Controller ?? owner;

        var opponents = opponentResolver?.Invoke();
        if (opponents == null) return; // shape path — no opponents wired.

        // Snapshot, excluding the controller (CR 800.4 — "each opponent").
        var targets = opponents
            .Where(p => p != null && !ReferenceEquals(p, controller))
            .ToList();
        if (targets.Count == 0) return;

        // Pass 1 — each opponent discards a card (CR 701.8). Record whether
        // each discarded a NONLAND card "this way".
        var discardedNonland = new Dictionary<Player, bool>(targets.Count);
        foreach (var opp in targets)
        {
            discardedNonland[opp] = OpponentDiscardsOne(opp, opponentAgent);
        }

        // Pass 2 — each opponent who didn't discard a nonland card this way
        // loses 3 life (CR 119.3). Empty-hand opponents (couldn't discard)
        // and land-discarders both fall here.
        foreach (var opp in targets)
        {
            if (!discardedNonland[opp])
            {
                Fx.LoseLife(opp, LifeLoss);
            }
        }
    }

    /// <summary>
    /// CR 701.8 — <paramref name="opponent"/> discards one card of their
    /// choice. Returns true IFF the discarded card was a NONLAND card.
    /// An empty hand → no discard → returns false (they didn't discard a
    /// nonland card this way).
    /// </summary>
    private static bool OpponentDiscardsOne(Player opponent, IPlayerAgent? opponentAgent)
    {
        var hand = opponent.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return false; // couldn't discard at all.

        ICard pick;
        if (opponentAgent != null)
        {
            var chosen = opponentAgent
                .ChooseFromHandAsync(opponent, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick = (chosen != null && chosen.Zone == ZoneType.Hand) ? chosen : hand[0];
        }
        else
        {
            pick = hand[0];
        }

        opponent.Zones.Hand.RemoveCard(pick);
        opponent.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);

        // CR 305.1 — "nonland card" = a card that is NOT a land.
        return !pick.HasType(CardType.Land);
    }
}
