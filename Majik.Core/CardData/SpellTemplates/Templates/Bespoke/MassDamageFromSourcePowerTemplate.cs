using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Chandra's Ignition / Waltz of Rage family — "Target creature you control
/// deals damage equal to its power to each other creature[ and each
/// opponent]." Asymmetric-fight extension where the damage hits every other
/// creature on the battlefield (and optionally each opponent), not a single
/// target.
///
/// Cards: Chandra's Ignition, Waltz of Rage (lossy: trailing
/// "Until end of turn, whenever a creature you control dies, exile the top
/// card of your library. You may play it until the end of your next turn."
/// rider dropped at v1 per the design brief).
///
/// v1 stub:
/// - One target (source creature you control).
/// - Snapshots every creature on the battlefield (across all known players
///   via <see cref="ChosenSpellParams.AllPlayers"/>; falls back to the
///   caster's view) and deals <c>source.Power</c> to each one that isn't
///   the source itself.
/// - If "and each opponent" is in the oracle text, also deals
///   <c>source.Power</c> to each non-caster player via
///   <see cref="Player.LoseLife"/>.
/// - Replacement-bus damage filtering is not threaded through here (matches
///   <see cref="Templates.Damage.DamageSpellFactory"/>'s simpler sweep
///   spells like <c>DealsXDamageEachCreatureSpell</c>).
/// </summary>
public sealed class MassDamageFromSourcePowerTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^target\s+creature\s+you\s+control\s+deals\s+damage\s+equal\s+to\s+its\s+power\s+to\s+each\s+other\s+creature(?<eachOpponent>\s+and\s+each\s+opponent)?\.",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "MassDamageFromSourcePower";
    public BotIntent Intent => BotIntent.Wrath | BotIntent.Burn;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        return new Dictionary<string, string>
        {
            ["eachOpponent"] = m.Groups["eachOpponent"].Success ? "1" : "0",
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        var caster = ctx.Caster;
        var hitsOpponents = @params.TryGetValue("eachOpponent", out var v) && v == "1";

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Wrath | BotIntent.Burn),
            },
            EffectFactory: param =>
            {
                var source = resolver(param.Targets[0][0]);
                var allPlayers = param.AllPlayers;
                return new IEffect[] { new Effect("mass damage from source power", () =>
                {
                    if (source is not Creature sc) return;
                    var power = sc.Power;
                    if (power <= 0) return;

                    // Collect every creature on the battlefield from every
                    // known player. ChosenSpellParams.AllPlayers is the
                    // canonical handle (plumbed through SpellCastFlow); for
                    // legacy callers that build params by hand, fall back to
                    // the caster's own battlefield view.
                    var players = allPlayers ?? new[] { caster };
                    var seen = new HashSet<Creature>();
                    foreach (var pl in players)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>())
                        {
                            if (ReferenceEquals(c, sc)) continue;
                            if (seen.Add(c)) c.TakeDamage(power);
                        }
                    }

                    if (hitsOpponents && allPlayers is not null)
                    {
                        foreach (var pl in allPlayers)
                        {
                            if (ReferenceEquals(pl, caster)) continue;
                            pl.LoseLife(power);
                        }
                    }
                }) };
            });
    }
}
