using System.Text.RegularExpressions;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterTargetSpellTemplate : ISpellTemplate
{
    // Accepts an optional type/color qualifier chain between "target" and
    // "spell" — "blue", "instant or sorcery", "creature or Aura",
    // "enchantment, instant, or sorcery", "nonartifact", "Spirit or Arcane".
    // The v1 stub counters the chosen target regardless of subtype, which
    // matches the resolved effect for any of these qualifier variants. Target
    // legality (which permits selecting only matching spells) is enforced
    // separately at target-selection time.
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+(?:[\w-]+(?:\s*,\s*[\w-]+)*(?:\s+or\s+[\w-]+)?\s+)?spell\b",
        RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "CounterTargetSpell";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CounterSpellFactory.CounterTargetSpell(ctx.Resolver, ctx.Stack);
}
