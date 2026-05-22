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
/// Expressive Iteration (Strixhaven). Sorcery — {U}{R}. Oracle text:
///
///   "Look at the top three cards of your library. Put one of them into
///    your hand, put one of them on the bottom of your library, and exile
///    one of them. You may play the exiled card this turn."
///
/// ## Implemented (v1)
/// - Bespoke template — matches the exact oracle text fragment.
/// - Deterministic v1 distribution: of the top three library cards,
///   the first goes to the caster's hand, the second to the bottom of
///   the caster's library, the third to the exile zone.
/// - Library size less than three is handled gracefully: 0 → no-op; 1 → hand
///   only; 2 → hand + bottom only.
///
/// ## Deferred (v1 gaps)
/// - <b>Real player choice</b>: which of the three goes where is fixed by
///   index. The agent-prompt system needs a "choose-among" decision shape
///   before this can defer to the caster.
/// - <b>"You may play the exiled card this turn" rider</b>: requires a
///   temporary cast-from-exile permission tracker keyed on the exiled
///   card (CR 118.10). v1 exiles the card normally with no follow-up
///   permission; the caster simply can't play it. Acceptable because no
///   other v1 card relies on that permission shape.
/// </summary>
public sealed class ExpressiveIterationTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"look\s+at\s+the\s+top\s+three\s+cards\s+of\s+your\s+library\.\s*"
        + @"put\s+one\s+of\s+them\s+into\s+your\s+hand,\s*"
        + @"put\s+one\s+of\s+them\s+on\s+the\s+bottom\s+of\s+your\s+library,\s*"
        + @"and\s+exile\s+one\s+of\s+them",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 100;
    public string Name => "ExpressiveIteration";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        BuildSpell(ctx.Caster);

    private static SpellDefinition BuildSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Expressive Iteration", () =>
        {
            // Peek the top three. Library may have fewer.
            var top3 = caster.Zones.Library.GetCards().Take(3).ToList();
            if (top3.Count == 0) return;

            // 1) First card → hand.
            var toHand = top3[0];
            caster.Zones.Library.RemoveCard(toHand);
            caster.Zones.Hand.AddCard(toHand);
            toHand.SetZone(ZoneType.Hand);

            if (top3.Count < 2) return;

            // 2) Second card → bottom of library.
            //    Library.RemoveCard + AddCard pushes to the natural back
            //    of the underlying list, which is the bottom for this
            //    engine. Matches LookAtTopPutOneInHand's "bottom" path.
            var toBottom = top3[1];
            caster.Zones.Library.RemoveCard(toBottom);
            caster.Zones.Library.AddCard(toBottom);
            toBottom.SetZone(ZoneType.Library);

            if (top3.Count < 3) return;

            // 3) Third card → exile.
            //    "You may play the exiled card this turn" rider is deferred
            //    (see xmldoc) — exile is final for v1.
            var toExile = top3[2];
            caster.Zones.Library.RemoveCard(toExile);
            caster.Zones.Exile.AddCard(toExile);
            toExile.SetZone(ZoneType.Exile);
        }) });
}
