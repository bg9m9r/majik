using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sleight of Hand (Portal / Modern Horizons 3, {U}).
///
/// Sorcery. Oracle text:
///   "Look at the top two cards of your library. Put one of them into your
///    hand and the other on the bottom of your library."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) peeks the top
///   two cards of the controller's library, asks the registered
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (kind label
///   "card to put into your hand") which one to grab, moves that card
///   to the hand, and bottoms the other. Unlike Ponder / Brainstorm,
///   the controller's choice is mandatory (no "may" — both cards
///   resolve into known zones).
/// - With no agent registered, the default keeps the deterministic
///   pre-agent posture: the top card goes to hand, the next card goes
///   to the bottom (matches <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>'s
///   "pick first candidate" default for un-agentified callers).
/// - One-card library: peek returns just that card; it goes to hand
///   (no "other" to bottom).
/// - Empty library: peek returns empty list; effect is a no-op (no
///   draw-from-empty SBA fires because the card never says "draw").
///
/// CR rule references: 701.18 (look at) — Sleight of Hand pre-dates
/// modern scry / surveil templating; the engine maps it onto the same
/// peek-and-rearrange primitives the scry pipeline uses.
/// </summary>
[CardName("Sleight of Hand")]
public static class SleightOfHandFactory
{
    public const string CardName = "Sleight of Hand";
    public const string PrintedManaCost = "{U}";
    private const int LookAtCount = 2;

    /// <summary>CardDef DSL — card shape only. Resolve effect lives in
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Sleight of Hand's resolve effect — peek top 2, agent picks
    /// one for the hand, the other goes to the bottom of the library.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Sleight of Hand: look at top 2, put 1 in hand, bottom the other.", async ctx =>
            {
                // Peek up to 2 cards. ScryAction.Peek tolerates short
                // libraries (returns up to N) so empty- and single-card
                // library handling falls out for free.
                var peeked = ScryAction.Peek(caster, LookAtCount).ToList();
                if (peeked.Count == 0)
                {
                    // Nothing to do; oracle text never reaches a draw clause.
                    return;
                }

                // Controller chooses which card goes to hand. Agent path:
                // ChooseLibraryPickAsync over the peeked list (kind label
                // surfaced verbatim to remote-agent UIs). Pre-agent
                // fallback: first peeked card (deterministic; matches
                // every other look-and-pick factory's default — e.g.
                // MagmaticChannelerFactory).
                ICard pickForHand;
                var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    var chosen = (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates: peeked,
                        kindLabel: "card to put into your hand").ConfigureAwait(false));

                    // Defensive: if the agent returns null (declines) or
                    // a card the peek didn't surface, fall back to the
                    // deterministic first-card pick. Sleight of Hand is
                    // mandatory — the controller MUST put one of them
                    // into their hand — so a null agent return is a
                    // mis-wired agent, not a legal decline.
                    pickForHand = chosen != null && peeked.Contains(chosen)
                        ? chosen
                        : peeked[0];
                }
                else
                {
                    pickForHand = peeked[0];
                }

                // Move the pick Library → Hand. Raw zone manipulation
                // (matches MagmaticChannelerFactory / ConsiderFactory's
                // post-decision move shape).
                caster.Zones.Library.RemoveCard(pickForHand);
                caster.Zones.Hand.AddCard(pickForHand);
                pickForHand.SetZone(ZoneType.Hand);

                // Move the OTHER peeked card to the bottom of the library.
                // Library order: index 0 is the top; AddCard appends, so
                // the existing library tail is unchanged and the bottomed
                // card sits at the very end (CR 701.18 — "on the bottom").
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
