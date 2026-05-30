using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Priest of Forgotten Gods (War of the Spark, {1}{B}).
///
/// Creature — Human Cleric 1/2.
/// Oracle text (Scryfall, verified):
///   "{T}, Sacrifice two other creatures: Any number of target players each
///    lose 2 life and sacrifice a creature of their choice. You add {B}{B}
///    and draw a card."
///
/// ## Implemented (v1)
/// - 1/2 Human Cleric, mana cost {1}{B}.
/// - One activated ability (CR 602.1) whose cost is:
///     - {T} (CR 602.5e, <see cref="AdditionalCost.Tap"/>); and
///     - Sacrifice two other creatures (CR 118.4) — two
///       <see cref="SacrificeAnotherCreatureCost"/> instances, each requiring
///       a creature other than the Priest itself.
/// - Resolution, in oracle order:
///     1. Each affected player loses 2 life (CR 119.3).
///     2. Each affected player sacrifices a creature of their choice
///        (CR 701.16 — sacrifice bypasses Indestructible / regeneration). The
///        affected player's agent drives the "of their choice" pick
///        (<see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>, intent
///        <see cref="BotIntent.Removal"/>), mirroring
///        <see cref="DiabolicEdictFactory"/>; deterministic fallback (no agent,
///        or an illegal pick) = first creature in battlefield order. A player
///        controlling no creature sacrifices nothing (no-op).
///     3. You add {B}{B} (CR 106.1) — <see cref="Player.AddManaToPool"/>.
///     4. You draw a card (CR 120.1).
///
/// ## Why a named factory
/// Shape is the sac-engine Cleric pattern already in the engine: this mirrors
/// <see cref="YawgmothFactory"/> (sacrifice-another-creature cost, an
/// each-other-player rider, "you draw a card", plus an
/// <c>opponentsResolver</c> injected at factory time) extended with a second
/// sacrifice cost, a per-player "sacrifice a creature of their choice" body
/// (from <see cref="DiabolicEdictFactory"/>), and an add-mana rider. No new
/// engine mechanic is introduced — every building block (tap cost, sacrifice-
/// another-creature cost, life loss, agent-driven sacrifice, add-mana, draw)
/// pre-exists.
///
/// ## Deferred (v1 gaps)
/// - <b>"Any number of target players" targeting</b>: the engine's
///   targeting / target-count prompt for players is not wired for this card
///   shape (same gap as Yawgmoth's "each other player"). v1 affects every
///   other player supplied by <paramref name="opponentsResolver"/>. For the
///   common two-player game this is identical to choosing "the one opponent";
///   the optional multi-player downside of being forced to hit every opponent
///   is the same deferral posture as Yawgmoth.
/// - <b>Sacrifice-cost target prompt</b>: <see cref="SacrificeAnotherCreatureCost.Target"/>
///   must be set by the agent before payment; v1 falls back to the first
///   eligible creature on the battlefield (deterministic).
/// - <b>Forced sacrifice prompt UI</b>: the affected player's agent receives
///   the full creature list; surfacing the choice to the portal decision
///   panel is deferred (same queue as Diabolic Edict).
/// </summary>
[CardName("Priest of Forgotten Gods")]
public static class PriestOfForgottenGodsFactory
{
    public const string CardName = "Priest of Forgotten Gods";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>
    /// Construct Priest of Forgotten Gods with no opponent resolver (test /
    /// vanilla path). The per-player lose-life / sacrifice rider is a no-op in
    /// this mode; the controller's add-mana + draw still execute.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentsResolver: null, sacrificeAgent: null);

    /// <summary>
    /// Construct Priest of Forgotten Gods with a runtime opponent resolver so
    /// the activated ability can iterate the affected players.
    /// </summary>
    public static Creature Create(Player owner, Func<IReadOnlyList<Player>>? opponentsResolver) =>
        Create(owner, opponentsResolver, sacrificeAgent: null);

    /// <summary>
    /// Construct Priest of Forgotten Gods.
    /// </summary>
    /// <param name="owner">Owner and initial controller of the card.</param>
    /// <param name="opponentsResolver">Called at ability resolution time to
    /// obtain the full player list (including the controller). Pass the game's
    /// player collection here. When null the per-player rider is skipped.</param>
    /// <param name="sacrificeAgent">Optional agent used to drive each affected
    /// player's "sacrifice a creature of their choice" pick. When null the pick
    /// falls back deterministically to the first creature in battlefield
    /// order (mirrors <see cref="DiabolicEdictFactory"/>).</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentsResolver,
        IPlayerAgent? sacrificeAgent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 2,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // Activated ability (CR 602.1):
        //   Cost: {T}, Sacrifice two other creatures
        //   Effect: Any number of target players each lose 2 life and
        //           sacrifice a creature of their choice. You add {B}{B} and
        //           draw a card.
        // --------------------------------------------------------------------

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                // {T} (CR 602.5e).
                AdditionalCost.Tap(card),
                // "Sacrifice two other creatures" (CR 118.4) — two distinct
                // sacrifice-another-creature costs. Each excludes the Priest
                // itself; CostPayment pays them in sequence, so the second
                // cannot re-pick the creature the first already sacrificed.
                new SacrificeAnotherCreatureCost(card),
                new SacrificeAnotherCreatureCost(card),
            },
            effects: new IEffect[]
            {
                // Effect 1+2: each affected player loses 2 life (CR 119.3),
                // then sacrifices a creature of their choice (CR 701.16).
                new Effect(
                    $"{CardName}: each affected player loses 2 life and sacrifices a creature",
                    () =>
                    {
                        var players = opponentsResolver?.Invoke();
                        if (players == null) return;

                        foreach (var p in players)
                        {
                            // "Any number of target players" — v1 affects every
                            // other player (see class xmldoc deferral). The
                            // controller is never a legal pick here.
                            if (ReferenceEquals(p, owner)) continue;

                            // CR 119.3 — life loss happens regardless of whether
                            // the player controls a creature to sacrifice.
                            p.LoseLife(2);

                            // CR 701.16 — "sacrifice a creature of their choice".
                            var creatures = p.Zones.Battlefield.GetCards()
                                .OfType<Creature>()
                                .Cast<ICard>()
                                .ToList();
                            if (creatures.Count == 0) continue;

                            ICard pick;
                            if (sacrificeAgent != null)
                            {
                                var chosen = sacrificeAgent
                                    .ChooseFromBattlefieldAsync(p, creatures, BotIntent.Removal)
                                    .GetAwaiter().GetResult();

                                // Validate: a creature still on this player's
                                // battlefield. Invalid → deterministic fallback.
                                pick = (chosen != null
                                        && chosen.Zone == ZoneType.Battlefield
                                        && chosen.HasType(CardType.Creature)
                                        && ReferenceEquals(chosen.Controller, p))
                                    ? chosen
                                    : creatures[0];
                            }
                            else
                            {
                                pick = creatures[0];
                            }

                            OracleSpellBinder.MoveToGraveyard(pick, ZoneMoveReason.Sacrifice);
                        }
                    }),

                // Effect 3: you add {B}{B} (CR 106.1).
                new Effect(
                    $"{CardName}: you add {{B}}{{B}}",
                    () => owner.AddManaToPool(ManaCost.Parse("{B}{B}"))),

                // Effect 4: you draw a card (CR 120.1).
                new Effect(
                    $"{CardName}: you draw a card",
                    () =>
                    {
                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // CR 120.3 — drawing from an empty library is noted;
                            // the SBA handles loss at the next opportunity.
                            owner.MarkTriedToDrawFromEmptyLibrary();
                            return;
                        }
                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }),
            });

        card.AddAbility(ability);
        return card;
    }
}
