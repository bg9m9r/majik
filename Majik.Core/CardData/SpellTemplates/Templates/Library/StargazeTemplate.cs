using System.Text.RegularExpressions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "Look at twice X cards from the top of your library. Put X cards from among
/// them into your hand and the rest into your graveyard. You lose X life." —
/// Stargaze's variable-X dig-and-drain body.
///
/// Distinct from the generic <see cref="LookAtTopPutKInHandTemplate"/>, which
/// matches a single fixed look-count / keep-count ("look at the top N cards …
/// put K of them …") and explicitly DROPS any trailing "You lose N life"
/// rider. Stargaze's look-count (2X) and keep-count (X) both scale off the
/// announced X (CR 601.2b), and the "You lose X life" clause (CR 119.3) is
/// load-bearing — it cannot be dropped — so it binds to a dedicated body.
///
/// Binds the seed oracle text to the single source of truth,
/// <see cref="StargazeFactory.BuildSpellDefinition"/> — a variable-X spell whose
/// resolve looks at 2X, keeps X to hand, bins the rest, then loses X life.
/// </summary>
public sealed class StargazeTemplate : ISpellTemplate
{
    // Case-insensitive; the normalizer has already collapsed whitespace.
    // Anchors on the "twice X" look-count + "from among them" + the
    // "you lose X life" rider so the generic LookAtTopPutKInHand template
    // (which would not match "twice X" anyway) is never mistakenly engaged.
    private static readonly Regex Pattern = new(
        @"look\s+at\s+twice\s+x\s+cards\s+from\s+the\s+top\s+of\s+your\s+library\.?\s+put\s+x\s+cards\s+from\s+among\s+them\s+into\s+your\s+hand\s+and\s+the\s+rest\s+into\s+your\s+graveyard\.?\s+you\s+lose\s+x\s+life",
        RegexOptions.IgnoreCase);

    // Above the generic look-K template (Priority 52) — this is the more
    // specific variable-X-with-life-loss form.
    public int Priority => 62;
    public string Name => "Stargaze";
    public BotIntent Intent => BotIntent.Cantrip;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StargazeFactory.BuildSpellDefinition(ctx.Caster);
}
