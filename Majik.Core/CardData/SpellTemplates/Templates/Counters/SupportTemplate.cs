using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// Support N keyword — "Put a +1/+1 counter on each of up to N target
/// creatures." Cards: Lead by Example, Nissa's Judgment, Unity of Purpose.
///
/// Pattern matches the keyword-only form ("Support N. (...reminder...)") and
/// the explicit prose ("Put a +1/+1 counter on each of up to N target
/// creatures"). Trailing riders after the Support sentence are dropped at v1.
///
/// Up-to-N targets via TargetRequest(min=0, max=N) — the cast flow lets the
/// caster choose zero through N creatures; resolution iterates the chosen
/// list.
/// </summary>
public sealed class SupportTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^support\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\.|^put\s+a\s+\+1\/\+1\s+counter\s+on\s+each\s+of\s+up\s+to\s+(?<n2>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+target\s+creatures",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "Support";
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var n = m.Groups["n"].Success && m.Groups["n"].Length > 0
            ? m.Groups["n"].Value
            : m.Groups["n2"].Value;
        return new Dictionary<string, string> { ["n"] = n };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest($"up to {n} target creatures", 0, n, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var picks = p.Targets[0];
                return new IEffect[] { new Effect($"support {n}", () =>
                {
                    foreach (var raw in picks)
                    {
                        var resolved = resolver(raw);
                        if (resolved is Permanent perm)
                        {
                            perm.Counters.Add(CounterType.PlusOnePlusOne, 1);
                        }
                    }
                }) };
            });
    }
}
