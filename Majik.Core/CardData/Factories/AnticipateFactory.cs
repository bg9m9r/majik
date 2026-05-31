using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anticipate (Magic Origins / Fate Reforged, {1}{U}).
///
/// Instant. Oracle text (Scryfall):
///   "Look at the top three cards of your library. Put one of them into
///    your hand and the rest on the bottom of your library in any order."
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {1}{U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) peeks the top
///   three cards of the controller's library via <see cref="ScryAction.Peek"/>,
///   asks the registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (kind label "card to
///   put into your hand") which one to grab, moves that card to the hand, and
///   puts the rest on the bottom of the library in order (consistent with the
///   "in any order" text — agent ordering of the rest is a future improvement;
///   v1 preserves the order they were peeked in, which is legal for a
///   non-agentified caller).
/// - With no agent registered, the deterministic pre-agent default keeps the
///   first-peeked card (top of library) and bottoms the remainder — same
///   posture as <see cref="SleightOfHandFactory"/>.
/// - Short-library edge cases:
///   - Two-card library: peek returns 2; one to hand, one to bottom.
///   - One-card library: peek returns 1; that card to hand; nothing to bottom.
///   - Empty library: peek returns empty list; effect is a no-op (no "draw"
///     clause so the empty-library SBA does not fire — CR 704.5b).
///
/// ## Key differences from Sleight of Hand
///
/// Sleight of Hand ({U}, Sorcery) peeks 2; Anticipate ({1}{U}, Instant) peeks 3.
/// Otherwise the logic is identical: agent pick, deterministic fallback, rest
/// to bottom. No instant/sorcery filter (unlike Augur of Bolas — any card
/// type qualifies for the hand).
///
/// CR rule references: 701.18 (look at), CR 601.2 (casting), CR 501.4 (instants
/// in any phase/step).
/// </summary>
[CardName("Anticipate")]
public static class AnticipateFactory
{
    public const string CardName = "Anticipate";
    public const string PrintedManaCost = "{1}{U}";
    private const int LookAtCount = 3;

    /// <summary>CardDef DSL — card shape only. Resolve effect lives in
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Anticipate's resolve effect — peek top 3, agent picks one for
    /// the hand, the rest go to the bottom of the library.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Anticipate: look at top 3, put 1 in hand, rest to bottom.", async ctx =>
            {
                // Peek up to 3 cards. ScryAction.Peek tolerates short
                // libraries (returns up to N) so empty- and single-card
                // library handling falls out for free.
                var peeked = ScryAction.Peek(caster, LookAtCount).ToList();
                if (peeked.Count == 0)
                {
                    // Nothing to do; oracle text has no draw clause so the
                    // empty-library SBA does not fire (CR 704.5b).
                    return;
                }

                // Controller chooses which card goes to hand. Agent path:
                // ChooseLibraryPickAsync over the peeked list (kind label
                // surfaced verbatim to remote-agent UIs). Pre-agent
                // fallback: first peeked card (deterministic; matches
                // SleightOfHandFactory / every other look-and-pick factory's
                // default).
                ICard pickForHand;
                var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    var chosen = (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates: peeked,
                        kindLabel: "card to put into your hand").ConfigureAwait(false));

                    // Defensive: if the agent returns null (declines) or a
                    // card the peek didn't surface, fall back to the
                    // deterministic first-card pick. Anticipate is mandatory
                    // — the controller MUST put one of them into their hand.
                    pickForHand = chosen != null && peeked.Contains(chosen)
                        ? chosen
                        : peeked[0];
                }
                else
                {
                    pickForHand = peeked[0];
                }

                // Move the pick Library → Hand.
                caster.Zones.Library.RemoveCard(pickForHand);
                caster.Zones.Hand.AddCard(pickForHand);
                pickForHand.SetZone(ZoneType.Hand);

                // Move the REST of the peeked cards to the bottom of the
                // library ("in any order" — v1 preserves peek order which
                // is valid). Library order: index 0 is the top; AddCard
                // appends, so the existing library tail is unchanged and
                // the bottomed cards sit at the very end (CR 701.18).
                foreach (var other in peeked)
                {
                    if (ReferenceEquals(other, pickForHand))
                    {
                        continue;
                    }
                    caster.Zones.Library.RemoveCard(other);
                    caster.Zones.Library.AddCard(other);
                    other.SetZone(ZoneType.Library);
                }
            }),
        };
    }
}
