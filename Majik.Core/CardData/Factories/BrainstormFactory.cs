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
///   from the controller's hand on top of the library. Agent-driven
///   picks via <see cref="IPlayerAgent.ChooseFromHandAsync"/>
///   (<see cref="BotIntent.Library"/>); no-agent fallback returns the
///   last-two-by-add-order from the hand (deterministic, mirrors
///   <see cref="SpellTemplates.Templates.Bespoke.BrainstormTemplate"/>).
/// - Graceful degradation:
///   - Empty library mid-draw flags the controller for the SBA loss
///     (CR 704.5b) and short-circuits remaining draws — matches
///     <see cref="FaithlessLootingFactory.BuildResolveEffect"/>.
///   - Hand with fewer than two cards after drawing returns however many
///     exist (no underflow).
/// - "In any order" semantics: the two picks are placed via
///   <see cref="IZone.InsertCardAt"/>(0) in sequence. The second pick lands
///   on top (library index 0); the first pick is one below it. For the
///   no-agent / deterministic path this preserves the
///   <see cref="SpellTemplates.Templates.Bespoke.BrainstormTemplate"/> order
///   (last-of-hand is the second insert → ends up on top).
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
/// - Full "choose two cards AND their order" prompt — currently two
///   independent ChooseFromHandAsync calls (each fed the remaining hand).
///   A "choose-and-order-N" agent shape would let the bot evaluate the
///   joint pick + order; v1 evaluates each pick in sequence which is
///   adequate for the rules-faithful outcome.
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
                // in any order." Two sequential picks; each consults the
                // agent over the current hand snapshot. Pre-agent fallback:
                // last-in-hand (deterministic, matches BrainstormTemplate).
                //
                // Library order: index 0 is the top. InsertCardAt(0) puts
                // a card at the top while preserving the rest. The second
                // pick is inserted last → lands on top; the first pick
                // sits one below it. With the deterministic fallback this
                // preserves BrainstormTemplate's ordering exactly.
                // -----------------------------------------------------------
                for (var i = 0; i < ReturnCount; i++)
                {
                    var hand = caster.Zones.Hand.GetCards().ToList();
                    if (hand.Count == 0) break;

                    ICard? pick;
                    if (agent != null)
                    {
                        pick = (await agent.ChooseFromHandAsync(caster, hand, BotIntent.LibraryReorder).ConfigureAwait(false));
                        // Null = decline, or agent returned a card that
                        // is no longer in hand (mis-wired agent). Fall
                        // back to the deterministic pick — Brainstorm's
                        // return clause is mandatory.
                        if (pick == null || pick.Zone != ZoneType.Hand)
                            pick = hand[^1];
                    }
                    else
                    {
                        pick = hand[^1];
                    }

                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Library.InsertCardAt(0, pick);
                    pick.SetZone(ZoneType.Library);
                }
            }),
        };
    }
}
