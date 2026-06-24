using System.Text.RegularExpressions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "Each opponent loses X life. You gain life equal to the life lost this
/// way." — the X-scaled drain-and-gain spell body (Exsanguinate).
///
/// Distinct from <see cref="EachOpponentLosesLifeTemplate"/>, which only
/// matches a FIXED loss amount ("each opponent loses N life") and carries no
/// life-gain rider. This template handles the variable-X form together with
/// the "you gain life equal to the life lost this way" rider — the rider can't
/// be composed independently of the loss clause because the gained amount is
/// the TOTAL actually lost (CR 119.3), which only the loss clause knows.
///
/// Binds the seed oracle text to the single source of truth,
/// <see cref="ExsanguinateFactory.BuildSpellDefinition"/> — a variable-X spell
/// (CR 601.2b) whose resolve drains X from each opponent and gains the total
/// (CR 109.5 / CR 119.3).
/// </summary>
public sealed class ExsanguinateDrainTemplate : ISpellTemplate
{
    // "each opponent loses X life. you gain life equal to the life lost this
    // way." — case-insensitive; the normalizer has already collapsed
    // whitespace. Anchors on the X-scaled loss + the life-lost-this-way rider
    // so the fixed-N EachOpponentLosesLife template is never shadowed.
    private static readonly Regex Pattern = new(
        @"each\s+opponent\s+loses\s+x\s+life\.?\s+you\s+gain\s+life\s+equal\s+to\s+the\s+life\s+lost\s+this\s+way",
        RegexOptions.IgnoreCase);

    // Beats EachOpponentLosesLifeTemplate (Priority 50) — this is the more
    // specific X-with-rider form; the fixed-N template would not match "X"
    // anyway, but the explicit ordering documents the intent.
    public int Priority => 60;
    public string Name => "ExsanguinateDrain";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Reach;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ExsanguinateFactory.BuildSpellDefinition(ctx.Caster);
}
