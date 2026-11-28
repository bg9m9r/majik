using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// "Put a lore counter on target Saga you control." / "Remove a lore counter
/// from target Saga you control." (Clash of the Eikons' modes 2 and 3.)
/// CR 714.2b — putting a lore counter on a Saga triggers the chapter for the
/// new count, so the put form goes through <see cref="Majik.Core.CardData.Sagas.SagaState.AdvanceAndChapter"/>;
/// removing one triggers nothing. Lore counters live in the Loyalty slot
/// (<see cref="Majik.Core.CardData.Sagas.SagaState.LoreCounters"/>). The
/// target is re-checked on resolution: still a Saga its controller controls.
/// </summary>
public sealed class LoreCounterOnSagaTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*(?<verb>put|remove)\s+a\s+lore\s+counter\s+(?:on|from)\s+target\s+saga\s+you\s+control\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "LoreCounterOnSaga";
    // Benefits a permanent of the caster's (it advances or rewinds its own Saga).
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        if (oracleText is null) return null;
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["verb"] = m.Groups["verb"].Value.ToLowerInvariant() }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var put = @params.TryGetValue("verb", out var v) && v == "put";
        var caster = ctx.Caster;
        var request = TargetRequest.ControllerScoped(
            "target Saga you control", 1, 1, caster,
            (controller, _) => controller.Zones.Battlefield.GetCards()
                .OfType<Permanent>().Where(p => p.SagaState != null).Cast<object>().ToList());
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { request },
            EffectFactory: p =>
            {
                var target = ctx.Resolver(p.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(put ? "put a lore counter on target Saga" : "remove a lore counter from target Saga", () =>
                    {
                        if (target is not Permanent { SagaState: { } saga } perm) return;
                        if (!ReferenceEquals(perm.Controller, caster)) return;
                        if (put) saga.AdvanceAndChapter();
                        else if (saga.LoreCounters > 0) perm.Counters.Remove(CounterType.Loyalty, 1);
                    }),
                };
            });
    }
}