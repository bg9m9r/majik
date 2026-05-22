using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// Sister to <see cref="GrantKeywordTilEotTemplate"/> for the multi-keyword
/// case:
///
///   "Target creature gains [kw1] and [kw2] until end of turn."
///
/// Cards: Battle-Rage Blessing, Horrid Vigor, Offer Immortality (deathtouch +
/// indestructible), plus the broader "gains [kw1] and [kw2]" pattern.
///
/// v1 stub: parse up to two keywords from the known set and register a
/// GrantKeywordUntilEndOfTurnEffect for each on the target creature.
/// </summary>
public sealed class MultiKeywordGrantTilEotTemplate : ISpellTemplate
{
    private const string KeywordAlt =
        @"flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible|hexproof|shroud|fear|intimidate|wither|infect";

    private static readonly Regex Pattern = new(
        @"^target\s+creature\s+gains?\s+(?<kw1>" + KeywordAlt + @")\s+and\s+(?<kw2>" + KeywordAlt + @")\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 55;
    public string Name => "MultiKeywordGrantTilEot";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string>
            {
                ["kw1"] = m.Groups["kw1"].Value,
                ["kw2"] = m.Groups["kw2"].Value,
            }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var kw1 = CountersSpellFactory.NormaliseKeyword(@params["kw1"]);
        var kw2 = CountersSpellFactory.NormaliseKeyword(@params["kw2"]);
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: param =>
            {
                var target = resolver(param.Targets[0][0]);
                return new IEffect[] { new Effect($"grants {kw1} + {kw2} EOT", () =>
                {
                    if (target is not Creature c || c.ActiveEffects is null) return;
                    c.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(c, kw1));
                    c.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(c, kw2));
                }) };
            });
    }
}
