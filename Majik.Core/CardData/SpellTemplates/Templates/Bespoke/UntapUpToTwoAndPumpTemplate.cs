using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Boros Charm / Glorious Charge family — "Untap up to two target creatures.
/// They each get +P/+T until end of turn." Two creature target slots (0..2)
/// and applies <see cref="PumpUntilEndOfTurnEffect"/> per chosen target on
/// top of an untap.
///
/// Cards: Battlewise Valor, Sun-Blessed Healer, etc. (any
/// "Untap up to two target creatures. They each get +P/+T until end of turn.").
///
/// v1 stub: applies untap + pump per chosen target. Doesn't model the
/// per-target legality predicate beyond the engine's default creature filter.
/// </summary>
public sealed class UntapUpToTwoAndPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^untap\s+up\s+to\s+two\s+target\s+creatures\.\s+they\s+each\s+get\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "UntapUpToTwoAndPump";
    public BotIntent Intent => BotIntent.Buff | BotIntent.CombatTrick;

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
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "up to two target creatures",
                    MinTargets: 0,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff | BotIntent.CombatTrick),
            },
            EffectFactory: param =>
            {
                // Snapshot chosen targets up-front so the closure body resolves
                // them at construction time (parity with other multi-target
                // pump templates).
                var chosen = param.Targets.Count > 0 ? param.Targets[0] : Array.Empty<object>();
                return new IEffect[] { new Effect($"untap-up-to-two-and-pump +{p}/+{t}", () =>
                {
                    foreach (var raw in chosen)
                    {
                        var resolved = resolver(raw);
                        if (resolved is not Creature c) continue;
                        if (c.IsTapped) c.Untap();
                        if ((p != 0 || t != 0) && c.ActiveEffects is not null)
                        {
                            c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, p, t));
                        }
                    }
                }) };
            });
    }
}
