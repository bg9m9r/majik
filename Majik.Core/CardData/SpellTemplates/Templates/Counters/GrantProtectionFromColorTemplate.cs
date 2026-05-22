using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// "Target creature you control gains protection from the color of your choice
/// until end of turn." — Blessed Breath, Center Soul, Emerge Unscathed,
/// Redeem the Lost.
///
/// v1 stub: grants a generic "protection" keyword on the target creature.
/// The chosen color is dropped (no color-targeted-by check enforced) and any
/// trailing rider (Redeem the Lost's clash + return) is dropped.
/// </summary>
public sealed class GrantProtectionFromColorTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^target\s+creature\s+you\s+control\s+gains\s+protection\s+from\s+the\s+color\s+of\s+your\s+choice\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 55;
    public string Name => "GrantProtectionFromColor";
    public BotIntent Intent => BotIntent.Protection;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature you control", 1, 1, Array.Empty<object>()) },
            EffectFactory: param =>
            {
                var target = resolver(param.Targets[0][0]);
                return new IEffect[] { new Effect("grants protection EOT", () =>
                {
                    if (target is Creature c && c.ActiveEffects is not null)
                    {
                        c.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(c, "protection"));
                    }
                }) };
            });
    }
}
