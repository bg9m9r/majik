using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// "Prevent the next N damage that would be dealt to any target this
/// turn." family — Hold at Bay (7), Mending Hands (4), Shieldmate's
/// Blessing (3), and similar fixed-N spells.
///
/// Captures the integer N out of oracle text and registers a single
/// per-turn shield (<see cref="PreventNextNDamageToAnyTargetShield"/>)
/// with that pool size. The shield itself is "any target" — it doesn't
/// gate on the target field; the first qualifying damage intent eats
/// from the pool until exhausted.
///
/// Trailing rider clauses (Candles' Glow: "You gain life equal to the
/// damage prevented this way") are lossy at v1 — the regex accepts the
/// lead clause and ignores extra prose. The headline prevention still
/// fires.
///
/// Requires <see cref="SpellBindContext.Replacements"/>. CR 615.
/// </summary>
public sealed class PreventNextNDamageToAnyTargetTemplate : ISpellTemplate
{
    // Lead clause only — trailing riders / parenthetical reminder text
    // are accepted but dropped. "the next X" with a non-integer X (e.g.
    // Acolyte's Reward's "where X is your devotion") is rejected by
    // ParamN; those go to a different template.
    private static readonly Regex Pattern = new(
        @"^\s*prevent\s+the\s+next\s+(?<n>\d+)\s+damage\s+that\s+would\s+be\s+dealt\s+to\s+any\s+target\s+this\s+turn\.?",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "PreventNextNDamageToAnyTarget";
    public BotIntent Intent => BotIntent.Protection;

    public bool CanBind(SpellBindContext ctx) => ctx.Replacements is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        return new Dictionary<string, string> { ["n"] = m.Groups["n"].Value };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var bus = ctx.Replacements!;
        var n = @params.TryGetValue("n", out var raw) && int.TryParse(raw, out var parsed) ? parsed : 0;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("prevent-next-n-any-target", () =>
                {
                    if (n > 0)
                        bus.Register(new PreventNextNDamageToAnyTargetShield(n));
                }),
            });
    }
}
