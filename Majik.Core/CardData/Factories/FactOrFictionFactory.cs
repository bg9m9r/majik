using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fact or Fiction (Invasion / reprints, {3}{U}).
///
/// Instant. Oracle text:
///   "Reveal the top five cards of your library. An opponent separates
///    those cards into two piles. Put one pile into your hand and the
///    other into your graveyard."
///
/// ## Shape source
/// Card identity (name, {3}{U}, Instant) is loaded from
/// <c>Majik.Core/CardData/Cards/fact-or-fiction.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The reveal / pile-split resolve body
/// is hand-rolled here (the JSON ability schema does not express a
/// reveal-top-N + opponent-pile-split effect) — same posture as the suggested
/// analogue <see cref="GiftsUngivenFactory"/> (which reveals a tutored pile and
/// has the opponent split it) and <see cref="ConsiderFactory"/> (reveal-top-N
/// card-advantage).
///
/// ## Implemented (v1)
/// - Instant {3}{U} (Blue) card shape with owner / controller wired.
/// - <b>1..1 "target opponent"</b> <see cref="TargetRequest"/> — the printed
///   text says "an opponent", which is not a targeted choice (CR 115.2 — "an
///   opponent" is a player-chosen-on-resolution reference, not a target).
///   v1 models the chosen opponent the same way Gifts Ungiven models its
///   "target opponent" splitter: a single 1..1 player slot resolved at
///   resolution time, so the same agent that performs the split is identified
///   deterministically. CR 608.2b — an illegal / non-Player resolution voids
///   the whole effect as a clean no-op (nothing is revealed).
/// - <b>Reveal top five</b> (CR 701.16a — "reveal"): the top five cards of the
///   caster's library are snapshotted as the revealed pile (clamped to library
///   size when fewer than five remain — CR 121.4 / do-as-much-as-possible).
///   The reveal is a no-op UI signal in v1 (same gap as every reveal/tutor
///   factory — Gifts Ungiven, Borderland Ranger, Mystical Tutor); the cards
///   still reach hand / graveyard so the observable game state is correct.
/// - <b>Opponent separates into two piles</b> (CR 700.4 — partition into two
///   piles, either of which may be empty): the chosen opponent's agent builds
///   "pile A" by selecting revealed cards one at a time via
///   <see cref="IPlayerAgent.ChooseFromPileAsync"/> (same prompt Gifts Ungiven
///   uses for its opponent split), stopping when it declines (returns null) or
///   when every revealed card has been assigned. Pile B is the remainder. The
///   partition is always valid — a stop on the first prompt yields an empty
///   pile A (all cards in pile B), which is legal.
/// - <b>Caster puts one pile into hand, the other into the graveyard</b>
///   (CR 700.4 — the controller, not the opponent, chooses which pile goes
///   where): the caster's agent is asked via
///   <see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,CancellationToken)"/>
///   whether to take pile A into hand (BotIntent.CardAdvantage — the caster
///   keeps the pile it values more; the deterministic default takes pile A).
///   The taken pile goes Library → Hand; the other pile goes Library →
///   Graveyard. CR 401.4 — both halves happen.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the five cards move Library → Hand / Graveyard
///   without publishing a reveal event — same gap as every reveal/tutor
///   factory (Gifts Ungiven, Borderland Ranger, Mystical Tutor, Consider).
/// - <b>"An opponent" multi-opponent choice</b>: in multiplayer the active
///   player chooses which opponent separates the piles (CR 115.2). v1 resolves
///   the single supplied opponent slot verbatim — sufficient for the 1v1
///   Modern pool, same posture as Gifts Ungiven's "target opponent".
/// - <b>No shuffle</b>: Fact or Fiction does not search, so there is no shuffle
///   step (contrast Gifts Ungiven, which tutors and therefore shuffles).
/// </summary>
[CardName("Fact or Fiction")]
public static class FactOrFictionFactory
{
    public const string CardName = "Fact or Fiction";
    public const int RevealCount = 5;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("fact-or-fiction");

    /// <summary>
    /// Construct Fact or Fiction as an Instant card. Card shape only — the
    /// resolve effect is wired via <see cref="BuildDefinition"/> which the cast
    /// flow / tests drive.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the Fact or Fiction SpellDefinition (1..1 opponent slot + reveal
    /// top five + opponent pile-split + caster keeps one pile).
    /// </summary>
    /// <param name="controller">The caster — the reveal reads from this
    /// player's library and the piles go to their hand / graveyard (CR 401 —
    /// "your" library / hand / graveyard).</param>
    /// <param name="targetResolver">Resolves the raw opponent token chosen by
    /// the caster (expected to yield a <see cref="Player"/>). When the resolver
    /// returns anything that isn't a <see cref="Player"/> the entire effect
    /// no-ops per CR 608.2b — nothing is revealed.</param>
    public static SpellDefinition BuildDefinition(
        Player controller,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — reveal top {RevealCount}; opponent splits into two piles, caster keeps one pile (hand) and the other (graveyard)",
                        () => Resolve(controller, resolved as Player)),
                };
            });
    }

    /// <summary>
    /// Drive the Fact or Fiction resolution against live agents.
    /// CR 608.2b — illegal opponent at resolution: whole effect no-ops.
    /// </summary>
    internal static void Resolve(Player controller, Player? opponent)
    {
        if (opponent == null)
        {
            // CR 608.2b — illegal target (resolver returned non-Player).
            // Fact or Fiction's whole printed resolution depends on the
            // opponent splitting the pile, so we no-op cleanly: no reveal.
            return;
        }

        // CR 701.16a — reveal the top five cards (clamped to library size;
        // CR 121.4 do-as-much-as-possible when fewer than five remain).
        var revealed = controller.Zones.Library.GetCards().Take(RevealCount).ToList();
        if (revealed.Count == 0)
        {
            return;
        }

        // CR 700.4 — the opponent separates the revealed cards into two piles
        // (either pile may be empty).
        var (pileA, pileB) = SplitIntoTwoPiles(opponent, revealed);

        // CR 700.4 — the CASTER (not the opponent) chooses which pile goes to
        // hand and which to the graveyard.
        var toHand = ChooseHandPile(controller, pileA, pileB);
        var toGraveyard = ReferenceEquals(toHand, pileA) ? pileB : pileA;

        // CR 401.4 — both halves happen.
        MovePile(controller, toHand, ZoneType.Hand);
        MovePile(controller, toGraveyard, ZoneType.Graveyard);
    }

    // --- Opponent separates the revealed cards into two piles (CR 700.4) -----
    private static (List<ICard> PileA, List<ICard> PileB) SplitIntoTwoPiles(
        Player opponent,
        List<ICard> revealed)
    {
        var opponentAgent = AgentRegistry.Get(opponent);
        var pileA = new List<ICard>();
        var remaining = new List<ICard>(revealed);

        // The opponent assigns cards to pile A one at a time; declining (null)
        // ends the assignment and leaves the rest in pile B. A stop on the very
        // first prompt is legal — pile A is empty, pile B holds all five
        // (CR 700.4 — a pile may be empty). The loop is bounded by the revealed
        // count, so it always terminates.
        while (remaining.Count > 0)
        {
            ICard? pick = opponentAgent?
                .ChooseFromPileAsync(
                    opponent,
                    remaining,
                    $"card to place in pile A (of {revealed.Count} revealed); decline to stop",
                    Majik.Core.Cards.BotIntent.Removal)
                .GetAwaiter().GetResult();

            // No agent → deterministic default: put the first card in pile A
            // then stop (a simple, legal 1/{n-1} split). Decline / out-of-list
            // → stop assigning to pile A.
            if (pick == null || !remaining.Contains(pick))
            {
                if (opponentAgent == null && pileA.Count == 0 && remaining.Count > 0)
                {
                    pileA.Add(remaining[0]);
                    remaining.RemoveAt(0);
                }
                break;
            }

            pileA.Add(pick);
            remaining.Remove(pick);
        }

        return (pileA, remaining);
    }

    // --- Caster chooses which pile goes to hand (CR 700.4) -------------------
    private static List<ICard> ChooseHandPile(
        Player controller,
        List<ICard> pileA,
        List<ICard> pileB)
    {
        var casterAgent = AgentRegistry.Get(controller);
        if (casterAgent == null)
        {
            // Deterministic default: take pile A into hand.
            return pileA;
        }

        // BotIntent.CardAdvantage — the caster keeps the pile it values more.
        // The default heuristic returns true (take pile A); smart / remote
        // agents override to pick the better pile.
        var takePileA = casterAgent
            .ChooseYesNoAsync(
                $"Put pile A ({pileA.Count} cards) into your hand? (No = pile B into hand instead)",
                Majik.Core.Cards.BotIntent.CardAdvantage)
            .GetAwaiter().GetResult();

        return takePileA ? pileA : pileB;
    }

    // --- Move a pile Library → destination zone (CR 401.4) ------------------
    private static void MovePile(Player controller, List<ICard> pile, ZoneType destination)
    {
        foreach (var card in pile)
        {
            if (!controller.Zones.Library.GetCards().Contains(card)) continue;
            controller.Zones.Library.RemoveCard(card);

            if (destination == ZoneType.Hand)
            {
                controller.Zones.Hand.AddCard(card);
            }
            else
            {
                controller.Zones.Graveyard.AddCard(card);
            }
            card.SetZone(destination);
        }
    }
}
