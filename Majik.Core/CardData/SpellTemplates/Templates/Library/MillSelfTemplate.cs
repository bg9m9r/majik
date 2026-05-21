using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class MillSelfTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*mill\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public int Priority => 50;
    public string Name => "MillSelf";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        LibrarySpellFactory.MillSelfSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(@params["n"]));
}
