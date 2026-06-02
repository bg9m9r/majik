using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thirst for Discovery (Modern Horizons 3, {2}{U}).
///
/// Instant. Oracle text (Scryfall):
///   "Draw three cards. Then discard two cards unless you discard a basic
///    land card."
///
/// ## Why it gets its own factory
/// Thirst for Discovery is the draw-three sibling of the engine's
/// draw-then-discard looters (Faithless Looting, Cathartic Reunion,
/// Tormenting Voice). The twist is the printed "unless you discard a basic
/// land card" rider: the controller may satisfy the entire discard cost by
/// pitching a SINGLE basic land instead of two arbitrary cards. Per the
/// card's printed ruling, "If you discard a basic land card this way, you
/// discard only that card" — the net swing is +2 hand size with a basic
/// land vs. +1 without. That conditional discard count is what makes it
/// more than a re-skin of Tormenting Voice; the shape is otherwise the
/// same agent-or-fallback discard policy as Faithless Looting.
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {2}{U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>):
///   1. Draw three cards (CR 121.1). Empty library mid-draw flags the
///      player for the SBA loss (CR 704.5b) and short-circuits the rest —
///      same handling as Faithless Looting / Tormenting Voice.
///   2. Discard step (CR 701.16 + the printed "unless" rider): if a basic
///      land card is in hand, the controller may discard ONLY that single
///      basic land to satisfy the cost; otherwise they discard two cards.
///      A basic land = <see cref="CardType.Land"/> + the
///      <see cref="CardSupertype.Basic"/> supertype.
/// - Discard pick uses the same agent-or-fallback policy as
///   <see cref="FaithlessLootingFactory"/>: the agent's
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/> chooses each card; null agent / null
///   pick falls back to the deterministic last-card-in-hand policy.
/// - Basic-land preference (v1 deterministic default): when no agent is
///   registered, the resolver discards a basic land if one is in hand —
///   this is strictly the controller's best line (lose one card instead of
///   two), so the default never makes a self-harming choice.
/// - "Discard up to N when fewer exist" (CR 701.16a): if the post-draw hand
///   has fewer cards than the cost, the resolver discards what is available.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven "do you want to discard your basic land?" yes/no prompt.
///   The current policy always pays with a basic land when one is available
///   (the strictly-better line) and otherwise pitches the deterministic
///   last-two-in-hand. A real choose-which-and-whether prompt waits on the
///   same discard-prompt system other v1 discard sites are queued behind.
/// </summary>
[CardName("Thirst for Discovery")]
public static class ThirstForDiscoveryFactory
{
    public const string CardName = "Thirst for Discovery";
    public const string PrintedManaCost = "{2}{U}";
    public const int DrawCount = 3;
    public const int FullDiscardCount = 2;

    /// <summary>CardDef DSL — card shape only. Draw-then-conditional-discard
    /// body lives in <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Thirst for Discovery's resolve effect — draw three cards, then
    /// discard two cards unless a basic land is discarded instead.
    /// </summary>
    /// <param name="caster">The player drawing + discarding.</param>
    /// <param name="agent">Optional agent for discard target selection.
    /// When null, the deterministic v1 picker is used (prefer a basic land,
    /// else last cards in hand).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Thirst for Discovery: draw three cards, then discard two unless you discard a basic land.", async ctx =>
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
                // cards unless you discard a basic land card." If a basic
                // land is in hand, discarding only that single card pays
                // the whole cost (printed ruling: "you discard only that
                // card"). Otherwise discard two.
                //
                // Agent path: ChooseFromHandAsync(BotIntent.Discard). If the
                // agent's first pick is a basic land, that single discard
                // satisfies the cost. Otherwise we discard a second card.
                // Default (no agent): prefer a basic land if available (the
                // strictly-better line — lose one card, not two), else the
                // deterministic last-card-in-hand policy mirroring
                // FaithlessLooting.
                // ----------------------------------------------------------
                var firstPick = await ChooseDiscardAsync(caster, agent).ConfigureAwait(false);
                if (firstPick == null)
                {
                    // CR 701.16a — hand empty after the draw; nothing to
                    // discard.
                    return;
                }

                var firstWasBasicLand = IsBasicLand(firstPick);
                Discard(caster, firstPick);

                // A basic land discard ends the cost (discard ONLY that
                // card). Otherwise we owe a second discard.
                if (!firstWasBasicLand)
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
    /// a basic land (the controller's strictly-better line for this card),
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

        // Deterministic default: prefer a basic land (lose one card, not
        // two), else the last card in hand (mirrors FaithlessLooting).
        return hand.FirstOrDefault(IsBasicLand) ?? hand[^1];
    }

    private static void Discard(Player caster, ICard card)
    {
        caster.Zones.Hand.RemoveCard(card);
        caster.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>A card is a basic land iff it is a Land with the Basic
    /// supertype (CR 205.4a / 305.6).</summary>
    private static bool IsBasicLand(ICard card) =>
        card.HasType(CardType.Land) && card.HasSupertype(CardSupertype.Basic);
}
