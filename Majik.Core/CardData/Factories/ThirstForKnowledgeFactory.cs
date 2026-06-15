using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thirst for Knowledge (Mirrodin / 5th Dawn,
/// {2}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-14):
///   "Draw three cards. Then discard two cards unless you discard an
///    artifact card."
///
/// ## Why it gets its own factory
/// Thirst for Knowledge is the artifact-deck sibling of
/// <see cref="ThirstForDiscoveryFactory"/> (which keys off a basic land
/// instead of an artifact) — both are draw-three looters with a printed
/// "unless you discard a &lt;type&gt; card" rider that lets the controller
/// satisfy the entire two-card discard cost by pitching a SINGLE card of
/// the named type. Per the card's printed ruling, "If you discard an
/// artifact card this way, you discard only that card." The net swing is
/// +2 hand size when an artifact is discarded vs. +1 without. That
/// conditional discard count is what makes it more than a re-skin of
/// Tormenting Voice; the shape is otherwise the same agent-or-fallback
/// discard policy as Faithless Looting / Thirst for Discovery.
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {2}{U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>):
///   1. Draw three cards (CR 121.1). Empty library mid-draw flags the
///      player for the SBA loss (CR 704.5b) and short-circuits the rest —
///      same handling as Faithless Looting / Thirst for Discovery.
///   2. Discard step (CR 701.16 + the printed "unless" rider): if an
///      artifact card is in hand, the controller may discard ONLY that
///      single artifact to satisfy the cost; otherwise they discard two
///      cards. An artifact card = <see cref="CardType.Artifact"/>.
/// - Discard pick uses the same agent-or-fallback policy as
///   <see cref="ThirstForDiscoveryFactory"/>: the agent's
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/> chooses each card; null agent / null
///   pick falls back to the deterministic last-card-in-hand policy.
/// - Artifact preference (v1 deterministic default): when no agent is
///   registered, the resolver discards an artifact if one is in hand —
///   this is strictly the controller's best line (lose one card instead of
///   two), so the default never makes a self-harming choice.
/// - "Discard up to N when fewer exist" (CR 701.16a): if the post-draw hand
///   has fewer cards than the cost, the resolver discards what is available.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven "do you want to discard your artifact?" yes/no prompt.
///   The current policy always pays with an artifact when one is available
///   (the strictly-better line) and otherwise pitches the deterministic
///   last-two-in-hand. A real choose-which-and-whether prompt waits on the
///   same discard-prompt system other v1 discard sites are queued behind.
/// </summary>
[CardName("Thirst for Knowledge")]
public static class ThirstForKnowledgeFactory
{
    public const string CardName = "Thirst for Knowledge";
    public const string PrintedManaCost = "{2}{U}";
    public const int DrawCount = 3;
    public const int FullDiscardCount = 2;

    /// <summary>CardDef DSL — card shape only. Draw-then-conditional-discard
    /// body lives in <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Thirst for Knowledge's resolve effect — draw three cards, then
    /// discard two cards unless an artifact card is discarded instead.
    /// </summary>
    /// <param name="caster">The player drawing + discarding.</param>
    /// <param name="agent">Optional agent for discard target selection.
    /// When null, the deterministic v1 picker is used (prefer an artifact,
    /// else last cards in hand).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Thirst for Knowledge: draw three cards, then discard two unless you discard an artifact.", async ctx =>
            {
                // ----------------------------------------------------------
                // CR 121.1 — "Draw three cards." Empty library mid-draw
                // flags the player for the SBA loss (CR 704.5b) and
                // short-circuits the remaining draws.
                // ----------------------------------------------------------
                for (var i = 0; i < DrawCount; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }

                // ----------------------------------------------------------
                // CR 701.16 + the printed "unless" rider — "discard two
                // cards unless you discard an artifact card." If an artifact
                // is in hand, discarding only that single card pays the whole
                // cost (printed ruling: "you discard only that card").
                // Otherwise discard two.
                //
                // Agent path: ChooseFromHandAsync(BotIntent.Discard). If the
                // agent's first pick is an artifact, that single discard
                // satisfies the cost. Otherwise we discard a second card.
                // Default (no agent): prefer an artifact if available (the
                // strictly-better line — lose one card, not two), else the
                // deterministic last-card-in-hand policy mirroring
                // FaithlessLooting / ThirstForDiscovery.
                // ----------------------------------------------------------
                var firstPick = await ChooseDiscardAsync(caster, agent).ConfigureAwait(false);
                if (firstPick == null)
                {
                    // CR 701.16a — hand empty after the draw; nothing to
                    // discard.
                    return;
                }

                var firstWasArtifact = IsArtifact(firstPick);
                Discard(caster, firstPick);

                // An artifact discard ends the cost (discard ONLY that
                // card). Otherwise we owe a second discard.
                if (!firstWasArtifact)
                {
                    var secondPick = await ChooseDiscardAsync(caster, agent).ConfigureAwait(false);
                    if (secondPick != null)
                    {
                        Discard(caster, secondPick);
                    }
                }
            }),
        };
    }

    /// <summary>
    /// Pick one card to discard from <paramref name="caster"/>'s hand.
    /// Agent path consults <see cref="IPlayerAgent.ChooseFromHandAsync"/>
    /// with <see cref="BotIntent.Discard"/>. Deterministic default prefers
    /// an artifact (the controller's strictly-better line for this card),
    /// then falls back to the last card in hand. Returns null when the hand
    /// is empty (CR 701.16a — discard up to N when fewer exist).
    /// </summary>
    private static async Task<ICard?> ChooseDiscardAsync(Player caster, IPlayerAgent? agent)
    {
        var hand = caster.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0)
        {
            return null;
        }

        if (agent != null)
        {
            var pick = await agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard).ConfigureAwait(false);
            if (pick != null && pick.Zone == ZoneType.Hand)
            {
                return pick;
            }
            // null = decline. The discard here is mandatory; fall through to
            // the deterministic pick so the rules-effect stays observable.
        }

        // Deterministic default: prefer an artifact (lose one card, not
        // two), else the last card in hand (mirrors FaithlessLooting).
        return hand.FirstOrDefault(IsArtifact) ?? hand[^1];
    }

    private static void Discard(Player caster, ICard card)
    {
        caster.Zones.Hand.RemoveCard(card);
        caster.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>A card is an artifact card iff it has the Artifact card type
    /// (CR 301 / 205.2b). Note this counts any artifact subtype — Equipment,
    /// Vehicle, etc. — since they are all Artifacts.</summary>
    private static bool IsArtifact(ICard card) =>
        card.HasType(CardType.Artifact);
}
