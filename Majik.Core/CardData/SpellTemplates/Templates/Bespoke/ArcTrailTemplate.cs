using System.Text.RegularExpressions;
using Majik.Core.CardData.Factories;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Cards;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Arc Trail — "~ deals 2 damage to any target and 1 damage to any other
/// target." Binds the live cast to <see cref="ArcTrailFactory.BuildSpellDefinition"/>:
/// two requests, the second distinct from the first (CR 601.2c), 2 and 1
/// damage. Before this the generic any-target damage template matched the
/// first clause and bound one target.
/// </summary>
public sealed class ArcTrailTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*~\s+deals\s+2\s+damage\s+to\s+any\s+target\s+and\s+1\s+damage\s+to\s+any\s+other\s+target\.?\s*$",
        RegexOptions.IgnoreCase);

    // Above every generic damage template.
    public int Priority => 140;
    public string Name => "ArcTrail";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        oracleText is not null && Pattern.IsMatch(oracleText)
            ? EmptyParams.Instance
            : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ArcTrailFactory.BuildSpellDefinition(ctx.Resolver);
}