using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// Echoing Courage / Bile Blight family — tribal pump/debuff hitting all
/// same-named creatures:
///
///   "Target creature and all other creatures with the same name as that
///    creature get +P/+T until end of turn."
///
/// Cards: Bile Blight, Echoing Courage, Echoing Decay.
///
/// v1 stub: pumps ONLY the target — the "and all other creatures with the
/// same name" clause is dropped. The single-target pump still resolves so
/// the load-bearing effect fires on the chosen creature.
/// </summary>
public sealed class SameNamePumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^target\s+creature\s+and\s+all\s+other\s+creatures\s+with\s+the\s+same\s+name\s+as\s+that\s+creature\s+get\s+(?<p>[+\-]\d+)\/(?<t>[+\-]\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "SameNamePump";
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string>
            {
                ["p"] = m.Groups["p"].Value,
                ["t"] = m.Groups["t"].Value,
            }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var p = int.Parse(@params["p"]);
        var t = int.Parse(@params["t"]);
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: param =>
            {
                var target = resolver(param.Targets[0][0]);
                return new IEffect[] { new Effect($"same-name pump {p:+#;-#;0}/{t:+#;-#;0}", () =>
                {
                    if (target is Creature c && c.ActiveEffects is not null)
                    {
                        c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, p, t));
                    }
                }) };
            });
    }
}
