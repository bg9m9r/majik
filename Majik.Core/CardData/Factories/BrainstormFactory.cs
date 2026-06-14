using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brainstorm (Ice Age and many reprints, {U}).
///
/// Instant. Oracle text:
///   "Draw three cards, then put two cards from your hand on top of your
///    library in any order."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) draws three
///   cards from the top of the controller's library, then puts two cards
///   from the controller's hand on top of the library. The "choose two
///   AND their order" pick is a SINGLE joint decision via
///   <see cref="IPlayerAgent.ChooseAndOrderFromHandAsync"/>
///   (<see cref="ChoiceKind.OrderedPickN"/>, <see cref="BotIntent.LibraryReorder"/>) —
///   the agent evaluates the combined selection + ordering at once
///   (result[0] ends up on top of the library). No-agent fallback returns
///   the last-two-by-add-order from the hand (deterministic, preserving the
///   historical factory ordering).
/// - Graceful degradation:
///   - Empty library mid-draw flags the controller for the SBA loss
///     (CR 704.5b) and short-circuits remaining draws — matches
///     <see cref="FaithlessLootingFactory.BuildResolveEffect"/>.
///   - Hand with fewer than two cards after drawing returns however many
///     exist (no underflow).
/// - "In any order" semantics: the joint pick returns an ordered list where
///   result[0] is the card to put on TOP. The list is applied in reverse via
///   <see cref="IZone.InsertCardAt"/>(0) so result[0] ends at library index 0
///   (top) and result[1] sits one below it.
///
/// Coverage note: the data-driven
/// <see cref="SpellTemplates.Templates.Bespoke.BrainstormTemplate"/> already
/// binds this exact oracle text for the seed-driven cast path. This named
/// factory exists to:
///   1. Surface the printed shape to <see cref="NamedCardFactory"/> /
///      bot / shape-only call sites (Brainstorm is Legacy-legal, not Modern,
///      so it is not present in the Modern seed — the factory keeps the
///      printed-name surface for off-Modern fixtures).
///   2. Route agent-driven pick decisions through
///      <see cref="IPlayerAgent.ChooseFromHandAsync"/>, lifting the
///      deterministic "last-2 in hand" fallback the template ships with.
///
/// ## Deferred (v1 gaps)
/// - SBA loss-from-empty-library handling — relies on
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>; the SBA itself
///   is the engine's responsibility (CR 704.5b).
/// </summary>
[CardName("Brainstorm")]
public static class BrainstormFactory
{
    public const string CardName = "Brainstorm";
    public const string PrintedManaCost = "{U}";

    private const int DrawCount = 3;
    private const int ReturnCount = 2;

    /// <summary>CardDef DSL — card shape only. Draw-3 + put-2-on-top
    /// body lives in <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Brainstorm's resolve effect — draw 3, then put 2 from hand
    /// on top of the controller's library in chosen order. Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result.
    /// </summary>
    /// <param name="caster">Spell controller — draws + selects from their
    /// own hand and library.</param>
    /// <param name="agent">Optional agent for pick decisions. When null,
    /// the deterministic last-2-in-hand fallback is used (same as the
    /// shared template path).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect($"{CardName}: draw three cards, then put two cards from your hand on top of your library.", async ctx =>
            {
                // -----------------------------------------------------------
                // CR 121.1 — "Draw three cards." Per-card guard so the
                // first empty-library draw flags the loss SBA and stops
                // the remaining draws (CR 704.5b). Mirrors
                // FaithlessLootingFactory + BrainstormTemplate.
                // -----------------------------------------------------------
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

                // -----------------------------------------------------------
                // "Then put two cards from your hand on top of your library
                // in any order." A SINGLE joint "choose two AND their order"
                // decision (CR 701.x library-top reorder) via the
                // ChoiceKind.OrderedPickN sink — the agent evaluates the
                // combined pick + ordering at once rather than two greedy,
                // sequential picks fed the remaining hand.
                //
                // Result contract (ChooseAndOrderFromHandAsync): the returned
                // list is the chosen cards in chosen order, where result[0]
                // is the card the chooser wants ON TOP of the library. We
                // apply the result so result[0] ends up at library index 0.
                //
                // Pre-agent fallback (no agent supplied): the deterministic
                // last-two-of-hand picker, ordered so the BrainstormTemplate
                // library ordering is preserved EXACTLY (last-of-hand ends up
                // on top, second-to-last one below it).
                // -----------------------------------------------------------
                var hand = caster.Zones.Hand.GetCards().ToList();
                var returnCount = Math.Min(ReturnCount, hand.Count);
                if (returnCount > 0)
                {
                    IReadOnlyList<ICard> ordered;
                    if (agent != null)
                    {
                        // OrderedPickN: result[0] goes on top. The agent shim
                        // sanitizes (distinct, in-hand, exactly returnCount).
                        ordered = await agent
                            .ChooseAndOrderFromHandAsync(caster, hand, returnCount, BotIntent.LibraryReorder)
                            .ConfigureAwait(false);
                        // Defensive: if a mis-wired agent under-returns, backfill
                        // from the deterministic tail so the mandatory return
                        // clause always moves exactly returnCount cards.
                        if (ordered.Count < returnCount)
                            ordered = DeterministicTopOrder(hand, returnCount);
                    }
                    else
                    {
                        ordered = DeterministicTopOrder(hand, returnCount);
                    }

                    // Apply: result[0] must end on TOP (library index 0).
                    // InsertCardAt(0) puts a card on top, so inserting in
                    // REVERSE leaves result[0] on top, result[1] below it, …
                    for (var i = ordered.Count - 1; i >= 0; i--)
                    {
                        var pick = ordered[i];
                        caster.Zones.Hand.RemoveCard(pick);
                        caster.Zones.Library.InsertCardAt(0, pick);
                        pick.SetZone(ZoneType.Library);
                    }
                }
            }),
        };
    }

    /// <summary>
    /// Deterministic pre-agent ordering for the "put N on top in any order"
    /// clause: the last <paramref name="count"/> cards of the hand (by add
    /// order), returned so element 0 ends up ON TOP of the library. This
    /// reproduces the historical factory behaviour exactly — two sequential
    /// <c>hand[^1]</c> picks where the SECOND insert landed on top, i.e. the
    /// second-to-last hand card on top and the last hand card just below it
    /// (factory test <c>Brainstorm_Resolve_NoAgent_DrawsThree_ReturnsLastTwo</c>).
    /// </summary>
    private static IReadOnlyList<ICard> DeterministicTopOrder(IReadOnlyList<ICard> hand, int count)
        => hand.Skip(Math.Max(0, hand.Count - count)).ToList();
}
