using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// Variable pump where the +N/+N magnitude scales with the number of
/// creatures the caster controls:
///
///   "Until end of turn, target creature gets +1/+1 for each creature you
///    control [and gains [kw]]."
///
/// Cards: Chorus of Might, Get a Leg Up, King Harald's Revenge.
///
/// v1 stub: counts the caster's creatures at resolution time and pumps the
/// target by that amount. Trailing keyword grant (trample, reach) is
/// applied. Other trailing riders ("It must be blocked this turn if able")
/// are dropped.
/// </summary>
public sealed class VarPumpPerCreatureTemplate : ISpellTemplate
{
    private const string KeywordAlt =
        @"flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible|hexproof|shroud";

    private static readonly Regex Pattern = new(
        @"^until\s+end\s+of\s+turn,\s+target\s+creature\s+gets\s+\+1\/\+1\s+for\s+each\s+creature\s+you\s+control(?:\s+and\s+gains\s+(?<kw>" + KeywordAlt + @"))?",
        RegexOptions.IgnoreCase);

    public int Priority => 55;
    public string Name => "VarPumpPerCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var kw = m.Groups["kw"].Success ? m.Groups["kw"].Value : "";
        return new Dictionary<string, string> { ["kw"] = kw };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var kw = CountersSpellFactory.NormaliseKeyword(@params.GetValueOrDefault("kw", ""));
        var caster = ctx.Caster;
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: param =>
            {
                var target = resolver(param.Targets[0][0]);
                return new IEffect[] { new Effect("var pump per creature EOT", () =>
                {
                    if (target is not Creature c || c.ActiveEffects is null) return;
                    var n = caster.Zones.Battlefield.GetCards().OfType<Creature>().Count();
                    if (n > 0) c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, n, n));
                    if (!string.IsNullOrEmpty(kw))
                    {
                        c.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(c, kw));
                    }
                }) };
            });
    }
}
