using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Brainstorm (Ice Age and many reprints). Instant — {U}. Oracle text:
///
///   "Draw three cards, then put two cards from your hand on top of your
///    library in any order."
///
/// ## Implemented (v1)
/// - Bespoke template — matches the canonical oracle fragment.
/// - Sequential resolution: draw three first, then return two from hand to
///   the top of the caster's library.
/// - Deterministic v1 picker: the two cards put back are the last two cards
///   added to the hand (i.e. the bottom-most two by add order). Order on the
///   library is then [secondReturned, firstReturned, ...] — the second
///   returned card is on top.
/// - Graceful degradation: empty library produces a partial / no draw and
///   then puts back whatever cards exist (up to 2 from hand). Hand with
///   fewer than 2 cards after drawing returns however many are available.
///
/// ## Deferred (v1 gaps)
/// - <b>Real player choice</b>: the "in any order" decision is fixed by
///   index. The agent-prompt system needs a "choose-and-order-N" decision
///   shape before this can defer to the caster. Same gap as
///   <see cref="ExpressiveIterationTemplate"/>.
/// - <b>SBA on empty library</b>: the per-card draw loop short-circuits on
///   empty library; loss-from-empty-draw state-based action is the engine's
///   responsibility (CR 704.5b). Acceptable because vanilla DrawCards
///   primitives across this codebase behave the same way.
/// </summary>
public sealed class BrainstormTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"draw\s+three\s+cards,\s*then\s+put\s+two\s+cards\s+from\s+your\s+hand\s+"
        + @"on\s+top\s+of\s+your\s+library\s+in\s+any\s+order",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 100;
    public string Name => "Brainstorm";
    public BotIntent Intent => BotIntent.Cantrip | BotIntent.Draw;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        BuildSpell(ctx.Caster);

    private static SpellDefinition BuildSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Brainstorm", () =>
        {
            // 1) Draw three cards. Library may have fewer; per-card guard
            //    mirrors LibrarySpellFactory.DrawCards_.
            for (var i = 0; i < 3; i++)
            {
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) break;
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }

            // 2) Put two cards from hand on top of library "in any order".
            //    v1 picks the last two cards in the hand by add order
            //    (deterministic; real player choice deferred per xmldoc).
            //
            //    Library "top" in this engine = first element of GetCards()
            //    (see LibrarySpellFactory.DrawCards_). Zone.InsertCardAt(0)
            //    puts a card at the top while preserving the rest of the
            //    library's order. Inserting toReturn[0] first, then
            //    toReturn[1], leaves toReturn[1] on top (index 0) and
            //    toReturn[0] just beneath it (index 1).
            var hand = caster.Zones.Hand.GetCards().ToList();
            var returnCount = Math.Min(2, hand.Count);
            if (returnCount == 0) return;

            // Pick the last `returnCount` hand cards as the ones to return.
            var toReturn = hand.Skip(hand.Count - returnCount).ToList();

            foreach (var c in toReturn)
            {
                caster.Zones.Hand.RemoveCard(c);
                caster.Zones.Library.InsertCardAt(0, c);
            }
        }) });
}
