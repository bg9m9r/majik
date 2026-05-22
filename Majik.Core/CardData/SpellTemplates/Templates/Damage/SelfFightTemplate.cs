using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "Self-fight" — target creature deals damage to itself equal to its power.
/// Inner Struggle, Justice Strike, Kiku's Shadow, Wrack with Madness shape:
///
///   "Target creature deals damage to itself equal to its power."
///
/// Distinct from <see cref="FightTemplate"/> (CR 701.13) which requires two
/// targets and bilateral damage exchange. Self-fight is one target dealing
/// damage to itself only — typically lethal for any creature with toughness
/// ≤ power.
///
/// Trailing rider clauses (Cut Propulsion's flying-doubles-damage) are
/// captured by the regex's trailing-text allowance and dropped at resolution.
/// </summary>
public sealed class SelfFightTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^target\s+creature\s+deals\s+damage\s+to\s+itself\s+equal\s+to\s+its\s+power\.",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "SelfFight";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("self-fight", () =>
                {
                    if (target is not Creature c) return;
                    var power = c.Power;
                    if (power > 0) c.TakeDamage(power);
                }) };
            });
    }
}
