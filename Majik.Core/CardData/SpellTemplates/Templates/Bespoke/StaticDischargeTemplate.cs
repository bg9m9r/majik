using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Intensity / Intensify (Mystery Booster 2 — Static Discharge and the
/// Intensity family). Production binder seam for the cast-time behaviour of an
/// Intensity damage spell:
///
///   "This sorcery deals damage equal to its intensity to any target. Then
///    cards you own named &lt;name&gt; intensify by 1."
///
/// <para>
/// Intensity is a card-scoped numeric value tracked on
/// <see cref="Card.Intensity"/> (NOT a permanent counter) — see
/// <see cref="IntensifyHelper"/>. The printed "Starting intensity N" is
/// stamped onto the live card by
/// <see cref="Majik.Core.CardData.ScryfallCardFactory"/> at build time; this
/// template supplies the resolve body:
/// </para>
/// <list type="bullet">
///   <item>Reads the caster's current intensity for the named card via
///   <see cref="IntensifyHelper.IntensityOf"/> (every owned copy stays in
///   lock-step, so reading any owned copy — including the one currently
///   resolving on the stack — yields the correct amount).</item>
///   <item>Deals that much damage to the chosen "any target" through
///   <see cref="Fx.DealDamageAny"/> (CR 115.3 — creature, player,
///   planeswalker, or battle).</item>
///   <item>CR 608.2c "Then …" — afterwards, intensifies every copy the caster
///   owns by 1 via <see cref="IntensifyHelper.IntensifyOwnedCopies"/>.</item>
/// </list>
///
/// <para>
/// The "~" sentinel in the regex is the caster's own card name after
/// <see cref="OracleTextNormalizer.NormalizeForCard"/> rewrites it — so the
/// template stays name-agnostic and matches any future Intensity damage card
/// that follows this printed shape.
/// </para>
/// </summary>
public sealed class StaticDischargeTemplate : ISpellTemplate
{
    // After NormalizeForCard, the card's own name (in "cards you own named X")
    // is rewritten to "~". We anchor on the intensity-damage clause and the
    // trailing "intensify by N" instruction.
    private static readonly Regex Pattern = new(
        @"deals\s+damage\s+equal\s+to\s+its\s+intensity\s+to\s+any\s+target\.\s*" +
        @"then\s+cards\s+you\s+own\s+named\s+~\s+intensify\s+by\s+(?<n>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 90;
    public string Name => "StaticDischargeIntensity";
    public BotIntent Intent => BotIntent.Burn;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        return new Dictionary<string, string>
        {
            ["intensifyBy"] = m.Groups["n"].Value,
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        var caster = ctx.Caster;
        var cardName = ctx.Entity.Name;
        var intensifyBy = @params.TryGetValue("intensifyBy", out var v) && int.TryParse(v, out var n)
            ? Math.Max(1, n)
            : 1;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: param =>
            {
                var target = resolver(param.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Static Discharge: deal intensity damage, then intensify by 1",
                        () =>
                        {
                            var intensity = IntensifyHelper.IntensityOf(caster, cardName);
                            if (intensity > 0) Fx.DealDamageAny(target, intensity);
                            IntensifyHelper.IntensifyOwnedCopies(caster, cardName, intensifyBy);
                        }),
                };
            });
    }
}
