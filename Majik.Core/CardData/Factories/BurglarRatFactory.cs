using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burglar Rat (Dominaria, {1}{B}). Creature — Rat 1/1.
///
/// ## Card text (Scryfall verified)
///   "When this creature enters, each opponent discards a card."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Rat, mana cost {1}{B}, owner / controller wired from the
///   embedded JSON definition (<see cref="Slug"/>).
/// - <b>ETB triggered ability (CR 603.6a)</b> — wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>, the same self-ETB trigger
///   shape as <see cref="RavenousChupacabraFactory"/>. On resolution every
///   opponent of Burglar Rat's controller discards one card of their choice
///   (CR 701.8). "Each opponent" is read from the LIVE
///   <see cref="ResolutionContext"/> via <see cref="ContextOpponents.Of"/> —
///   NOT a captured resolver, which was null on the routed prod build and made
///   the discard inert in real games (resolver-null bug class; mirrors
///   <see cref="KroxaTitanFactory"/>'s discard plumbing).
/// - Discard pick mirrors Kroxa's <c>OpponentDiscardsOne</c>: when an
///   <see cref="IPlayerAgent"/> is supplied each opponent chooses what to
///   discard (CR 701.8 — the discarding player chooses); otherwise a
///   deterministic first-card pick is used. An empty hand is a clean no-op
///   (CR 701.8 — a player with no cards cannot discard).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached, no
///   TriggerManager / agent wiring; suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager, IPlayerAgent)"/> — full wiring:
///   the ETB trigger is registered so the opponent-discard lands on the stack
///   automatically, and each opponent's discard pick routes through the agent.
/// </summary>
[CardName("Burglar Rat")]
public static class BurglarRatFactory
{
    public const string CardName = "Burglar Rat";
    public const string Slug = "burglar-rat";

    /// <summary>
    /// Construct Burglar Rat with the ETB trigger attached for shape
    /// inspection. Trigger is NOT registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, opponentAgent: null);

    /// <summary>
    /// Construct Burglar Rat with optional runtime services. "Each opponent" is
    /// read from the live resolution context at resolution
    /// (<see cref="ContextOpponents.Of"/>), so the discard is correct on the
    /// production routed build.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied the ETB trigger is
    /// registered so the enters domain event lands the discard on the stack
    /// automatically.</param>
    /// <param name="opponentAgent">Optional agent for each opponent's discard
    /// pick (CR 701.8 — the discarding player chooses). Null falls back to a
    /// deterministic first-card pick.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IPlayerAgent? opponentAgent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Rat,
        // {1}{B}, 1/1). No abilities in the JSON — the ETB is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a):
        //   "When this creature enters, each opponent discards a card."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: each opponent discards a card",
            ctx =>
            {
                ResolveOpponentsDiscard(owner, card, ctx, opponentAgent);
                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // -----------------------------------------------------------------------
    // Each opponent discards a card — CR 701.8.
    //
    // "Each opponent" is read from the LIVE resolution context (NOT a captured
    // resolver, which was null on the routed prod build). Mirrors
    // KroxaTitanFactory's discard plumbing minus the conditional drain.
    // -----------------------------------------------------------------------
    private static void ResolveOpponentsDiscard(
        Player owner,
        Creature card,
        ResolutionContext ctx,
        IPlayerAgent? opponentAgent)
    {
        var controller = card.Controller ?? owner;

        foreach (var opp in ContextOpponents.Of(ctx, controller))
        {
            OpponentDiscardsOne(opp, opponentAgent);
        }
    }

    /// <summary>
    /// CR 701.8 — <paramref name="opponent"/> discards one card of their
    /// choice. An empty hand → no discard (a player with no cards cannot
    /// discard). When an agent is supplied the discarding player chooses;
    /// otherwise a deterministic first-card pick is used.
    /// </summary>
    private static void OpponentDiscardsOne(Player opponent, IPlayerAgent? opponentAgent)
    {
        var hand = opponent.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // couldn't discard at all.

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
    }
}
