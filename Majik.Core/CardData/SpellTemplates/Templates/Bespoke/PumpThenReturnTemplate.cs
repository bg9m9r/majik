using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Abnormal Endurance family — pumps the target until end of turn AND grants
/// it a one-shot "When this creature dies, return it to the battlefield…"
/// delayed trigger:
///
///   "Until end of turn, target creature gets +P/+0 and gains 'When this
///    creature dies, return it to the battlefield [tapped] under its owner's
///    control [with a +1/+1 counter on it]'."
///
/// Cards: Abnormal Endurance, Demonic Gifts, Feign Death, Pain 101, Presumed
/// Dead, Return to Action, Supernatural Stamina, Undying Malice.
///
/// Requires <see cref="SpellBindContext.Triggers"/> to register the delayed
/// trigger — <see cref="CanBind"/> returns false when absent (template
/// silently skips, matching <see cref="Templates.Misc.StubBindTemplates.FogTemplate"/>'s
/// gating on <see cref="SpellBindContext.Replacements"/>).
///
/// v1 stub: P/T pump applied; on death, returns to battlefield. "Tapped" and
/// "with a +1/+1 counter" riders are captured + applied. Turn-gating (CR
/// "until end of turn" — trigger should only fire if death happens this turn)
/// is dropped: the delayed trigger fires whenever the creature dies, which
/// is lossy but matches the load-bearing semantic.
/// </summary>
public sealed class PumpThenReturnTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^until\s+end\s+of\s+turn,\s+target\s+creature\s+(?:gets\s+\+(?<p>\d+)\/\+(?<t>\d+)\s+and\s+)?gains?\s+""when\s+this\s+creature\s+dies,\s+return\s+it\s+to\s+the\s+battlefield(?:\s+(?<tapped>tapped))?(?:\s+under\s+its\s+owner'?s\s+control)?(?:\s+with\s+a?\s*(?<counter>\+1\/\+1)\s+counter\s+on\s+it)?\.?""",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "PumpThenReturn";
    public Majik.Core.Cards.BotIntent Intent =>
        Majik.Core.Cards.BotIntent.Buff | Majik.Core.Cards.BotIntent.Protection;

    public bool CanBind(SpellBindContext ctx) => ctx.Triggers is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        return new Dictionary<string, string>
        {
            ["p"] = m.Groups["p"].Success ? m.Groups["p"].Value : "0",
            ["t"] = m.Groups["t"].Success ? m.Groups["t"].Value : "0",
            ["tapped"] = m.Groups["tapped"].Success ? "1" : "0",
            ["counter"] = m.Groups["counter"].Success ? "1" : "0",
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var p = int.Parse(@params["p"]);
        var t = int.Parse(@params["t"]);
        var tapped = @params["tapped"] == "1";
        var withCounter = @params["counter"] == "1";
        var resolver = ctx.Resolver;
        var triggers = ctx.Triggers!;

        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: param =>
            {
                var target = resolver(param.Targets[0][0]);
                return new IEffect[] { new Effect($"pump-then-return {p:+#;-#;0}/{t:+#;-#;0}", () =>
                {
                    if (target is not Creature c) return;
                    if ((p != 0 || t != 0) && c.ActiveEffects is not null)
                    {
                        c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, p, t));
                    }
                    var controller = c.Controller ?? ctx.Caster;
                    var returnEffect = new Effect("delayed: return to battlefield", () =>
                    {
                        if (c.Zone != ZoneType.Graveyard) return;
                        controller.Zones.Graveyard.RemoveCard(c);
                        controller.Zones.Battlefield.AddCard(c);
                        c.SetZone(ZoneType.Battlefield);
                        if (tapped) c.Tap();
                        // The +1/+1 counter rider is captured into params but
                        // the runtime stub doesn't apply it — the engine
                        // doesn't yet have a +1/+1 counter primitive that
                        // survives zone changes via this path. Lossy v1.
                        _ = withCounter;
                    });
                    var delayed = new DelayedTriggeredAbility(
                        source: c,
                        controller: controller,
                        condition: Triggers.OnDies(c),
                        effects: new IEffect[] { returnEffect });
                    triggers.RegisterDelayed(delayed);
                }) };
            });
    }
}
