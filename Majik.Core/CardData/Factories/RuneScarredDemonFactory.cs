using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rune-Scarred Demon (Magic 2011 / reprints,
/// {5}{B}{B}).
///
/// Creature — Demon 6/6. Oracle text (Scryfall, verified):
///   "Flying
///    When this creature enters, search your library for a card, put it
///    into your hand, then shuffle."
///
/// ## Shape source
/// Card identity (name, {5}{B}{B}, 6/6, Creature — Demon, Flying) is loaded
/// from <c>Majik.Core/CardData/Cards/rune-scarred-demon.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (Flying is a printed keyword line in
/// the JSON — CR 702.9, rendered as a plain
/// <see cref="Majik.Core.Abilities.KeywordAbility"/> by the def factory). The
/// single ETB triggered ability is attached in code below: the JSON ability
/// schema does not yet express a "search for ANY card → hand → shuffle"
/// effect, so it is hand-rolled here — same posture as the analogue
/// <see cref="FierceEmpathFactory"/> (which tutors a big creature to hand
/// with the same reveal/shuffle shape) and
/// <see cref="ImperialRecruiterFactory"/>, differing only in the predicate
/// (here: none — any library card is eligible) and that this trigger is
/// mandatory rather than "you may".
///
/// ## Implemented (v1)
/// - 6/6 Demon (CR 205.3m) at {5}{B}{B} with <b>Flying</b> (CR 702.9, from
///   the JSON keyword line).
/// - <b>ETB trigger (CR 603.1)</b>: "search your library for a card, put it
///   into your hand, then shuffle." Unfiltered — ANY card in the library is
///   a legal pick. Consults the registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; deterministic
///   first-match fallback when no agent is registered (same posture as
///   <see cref="FierceEmpathFactory"/>). This is a <b>mandatory</b> search
///   (the printed oracle is "search", not "you may search"); CR 701.19a
///   still permits failing to find, so a null agent pick / empty library
///   collapses to the same no-op shape. Moves the pick Library → Hand and
///   shuffles ONCE via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a — one shuffle whether or not a card was found).
///
/// ## Deferred (v1)
/// - <b>Reveal step</b>: this card's oracle has no "reveal it" clause, so no
///   reveal signal is expected; the tutored card moves Library → Hand
///   directly. (The shared search helpers still publish a reveal only when a
///   reveal was requested — not the case here.)
/// </summary>
[CardName("Rune-Scarred Demon")]
public static class RuneScarredDemonFactory
{
    public const string CardName = "Rune-Scarred Demon";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("rune-scarred-demon");

    /// <summary>
    /// Construct Rune-Scarred Demon with its ETB trigger attached to the
    /// card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Rune-Scarred Demon with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger
    /// is registered so the relevant <c>CardMovedEvent</c> places it on the
    /// stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When this creature enters, search your library for a card, put
        //    it into your hand, then shuffle."
        // Unfiltered (any library card is eligible). Mandatory search; CR
        // 701.19a still permits failing to find. CR 701.20a shuffle wired
        // via LibraryShuffle.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search your library for a card -> hand, then shuffle",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return TutorAnyCardToHandAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE card with no
    /// predicate (any card is eligible), consult the agent (which may
    /// decline per CR 701.19a; deterministic first-match fallback when no
    /// agent is registered), move the pick Library → Hand, then shuffle once
    /// (CR 701.20a) whether or not a card was found.
    /// </summary>
    private static async ValueTask TutorAnyCardToHandAsync(Player player, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "a card to put into your hand")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "rune-scarred-demon");
    }
}
