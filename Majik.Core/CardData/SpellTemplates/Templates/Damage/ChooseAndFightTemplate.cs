using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// Verbose-preamble fight variant — picks two targets explicitly via a
/// "Choose target X and target Y" phrasing rather than the inline
/// "Target X fights target Y" of <see cref="FightTemplate"/>:
///
///   "Choose target creature you control and target creature you don't
///    control. [optional conditional buff]. Then those creatures fight each
///    other."
///
/// Cards: Blizzard Brawl, Joust, Malamet Battle Glyph, Tail Swipe.
///
/// v1 stub: conditional buff is dropped (Blizzard Brawl's snow-permanent
/// check, Joust's Knight check, Tail Swipe's main-phase check) — the fight
/// resolves either way, which is the load-bearing effect.
/// </summary>
public sealed class ChooseAndFightTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^choose\s+target\s+creature\s+you\s+control\s+and\s+target\s+creature\s+you\s+don'?t\s+control\..*?then\s+those\s+creatures\s+fight\s+each\s+other",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 60;
    public string Name => "ChooseAndFight";
    public BotIntent Intent => BotIntent.Burn;

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
                new TargetRequest("target creature you control", 1, 1, Array.Empty<object>()),
                new TargetRequest("target creature you don't control", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var a = resolver(p.Targets[0][0]);
                var b = resolver(p.Targets[1][0]);
                return new IEffect[] { new Effect("choose-and-fight", () =>
                {
                    if (a is not Creature ca || b is not Creature cb) return;
                    // Read both powers before applying any damage (CR 701.13a).
                    var aPower = ca.Power;
                    var bPower = cb.Power;
                    if (aPower > 0) cb.TakeDamage(aPower);
                    if (bPower > 0) ca.TakeDamage(bPower);
                }) };
            });
    }
}
