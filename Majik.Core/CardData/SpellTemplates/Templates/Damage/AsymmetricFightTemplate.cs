using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "Ranged assault" — your creature deals damage equal to its power to a
/// target, but takes none back. Distinct from CR 701.13 Fight which is a
/// bilateral exchange.
///
///   "Target creature you control deals damage equal to its power to target
///    [creature you don't control | creature or planeswalker you don't control
///    | creature an opponent controls | creature with flying | player]."
///
/// Cards: Bite Down, Gravitic Punch, Hard-Hitting Question, Infectious Bite,
/// Master's Rebuke, Rabid Bite, Ram Through, Rocky Rebuke, Tail Slash,
/// Tenderize, Wing Puncture.
///
/// v1 stub: two targets, source (creature you control) deals damage equal to
/// its current power to the second target. Damage-direction modifiers (Ram
/// Through's trample-excess, Infectious Bite's poison rider) are dropped.
/// </summary>
public sealed class AsymmetricFightTemplate : ISpellTemplate
{
    // Accepts the modifiers "another target" (Fall of the Hammer, Cosmic
    // Hunger) and "any target" (Soul's Fire) in front of the second-target
    // anchor — same effect tree as plain "target".
    private static readonly Regex Pattern = new(
        @"^target\s+creature\s+you\s+control\s+deals\s+damage\s+equal\s+to\s+its\s+power\s+to\s+(?:another\s+|any\s+)?target(?:\s+(?<targetKind>[a-z][a-z0-9\s,'\-]{0,80}?))?\.",
        RegexOptions.IgnoreCase);

    public int Priority => 65;
    public string Name => "AsymmetricFight";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var kind = m.Groups["targetKind"].Value.Trim().ToLowerInvariant();
        // Crude classification: player vs anything-creature-like.
        var isPlayer = kind == "player" || kind.StartsWith("player ");
        return new Dictionary<string, string>
        {
            ["targetKind"] = kind,
            ["isPlayer"] = isPlayer ? "1" : "0",
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        var isPlayer = @params.TryGetValue("isPlayer", out var v) && v == "1";
        var secondLabel = isPlayer ? "target player" : "target creature";
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature you control", 1, 1, Array.Empty<object>()),
                new TargetRequest(secondLabel, 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var source = resolver(p.Targets[0][0]);
                var target = resolver(p.Targets[1][0]);
                return new IEffect[] { new Effect("asymmetric-fight", () =>
                {
                    if (source is not Creature sc) return;
                    var power = sc.Power;
                    if (power <= 0) return;
                    switch (target)
                    {
                        case Creature tc: tc.TakeDamage(power); break;
                        case Player tp: tp.LoseLife(power); break;
                    }
                }) };
            });
    }
}
