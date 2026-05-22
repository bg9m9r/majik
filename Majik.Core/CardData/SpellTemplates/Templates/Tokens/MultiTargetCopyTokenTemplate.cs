using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

/// <summary>
/// "For each &lt;creature you control | creature target player controls |
/// token you control&gt;, create a token that's a copy of that
/// &lt;creature | permanent&gt;." — Clone Legion, Second Harvest variants.
///
/// v1 lossy:
/// - Pool is the caster's own creatures by default; "target player
///   controls" routes through the resolver if a target slot is present
///   (else falls back to caster's view).
/// - "Except it has X / they have haste / exile at end of step" riders
///   not handled — riders that DO bind via the composer's anaphoric
///   layer still fire as a follow-on clause.
/// - Each copy mirrors printed name + P/T + subtypes + keyword abilities.
/// </summary>
public sealed class MultiTargetCopyTokenTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"for\s+each\s+(?<pool>creature\s+you\s+control|creature\s+target\s+player\s+controls|token\s+you\s+control),\s+create\s+a\s+token\s+that'?s\s+a\s+copy\s+of\s+that\s+(?:creature|permanent|token)",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "MultiTargetCopyToken";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["pool"] = m.Groups["pool"].Value.ToLowerInvariant() }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var pool = @params.TryGetValue("pool", out var pk) ? pk : "creature you control";
        var caster = ctx.Caster;
        var resolver = ctx.Resolver;

        // "target player controls" needs a player target. Other shapes
        // walk the caster's own zones.
        var needsPlayerTarget = pool.Contains("target player");
        var targets = needsPlayerTarget
            ? new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) }
            : Array.Empty<TargetRequest>();

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: targets,
            EffectFactory: p => new IEffect[] { new Effect($"multi copy ({pool})", () =>
            {
                var sources = ResolveSources(pool, caster, resolver, p);
                foreach (var src in sources)
                {
                    var keywords = src.Abilities.OfType<KeywordAbility>()
                        .Select(k => k.Keyword)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var spec = new TokenFactory.TokenSpec(
                        Name: src.Name,
                        Power: src.BasePower,
                        Toughness: src.BaseToughness,
                        Subtypes: src.Subtypes.ToArray(),
                        Keywords: keywords);
                    TokenFactory.CreateOnBattlefield(spec, caster, zones: null);
                }
            }) });
    }

    private static IEnumerable<Creature> ResolveSources(
        string pool, Player caster, Func<object, object> resolver, ChosenSpellParams p)
    {
        if (pool.Contains("target player") && p.Targets.Count > 0 && p.Targets[0].Count > 0
            && resolver(p.Targets[0][0]) is Player target)
        {
            return target.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        }
        if (pool.Contains("token you control"))
        {
            return caster.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Where(c => c.IsToken)
                .ToList();
        }
        // Default: "creature you control".
        return caster.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
    }
}
