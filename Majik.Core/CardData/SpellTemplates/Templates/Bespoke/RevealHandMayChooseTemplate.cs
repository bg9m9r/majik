using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// May-choose variant of the Duress family:
///
///   "Target opponent reveals their hand. You may choose [filter] card from
///    it. If you do, that player (discards|exiles) that card.
///    [If you don't, ...]"
///
/// Cards: Binding Negotiation, Nightsnare, Reckoner Shakedown, Specter's
/// Shriek, Traumatic Revelation.
///
/// v1 stub: always-pick (the caster always "may choose"), so the discard or
/// exile branch always fires. Trailing "If you don't" riders are dropped, as
/// are downstream rider clauses on the "If you do" path.
/// </summary>
public sealed class RevealHandMayChooseTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^target\s+opponent\s+reveals\s+their\s+hand\.\s*you\s+may\s+choose\s+(?:a|an)\s+(?<filter>[a-z][a-z0-9\s,\-]{0,80}?\s+)?card\s+from\s+it\.\s*if\s+you\s+do,\s*(?:that\s+player|they)\s+(?<verb>discards?|exiles?)\s+(?:it|that\s+card)\.",
        RegexOptions.IgnoreCase);

    public int Priority => 95;
    public string Name => "RevealHandMayChoose";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var verb = m.Groups["verb"].Value.ToLowerInvariant();
        // Normalise: "discards" → "discard", "exiles" → "exile".
        verb = verb.TrimEnd('s');
        return new Dictionary<string, string> { ["verb"] = verb };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var verb = @params.GetValueOrDefault("verb", "discard");
        var toExile = verb == "exile";
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target opponent", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect($"reveal-hand-may-choose-{verb}", () =>
                {
                    if (target is not Player tp) return;
                    var pick = tp.Zones.Hand.GetCards()
                        .FirstOrDefault(c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
                    if (pick is null) return;
                    tp.Zones.Hand.RemoveCard(pick);
                    if (toExile)
                    {
                        tp.Zones.Exile.AddCard(pick);
                        pick.SetZone(ZoneType.Exile);
                    }
                    else
                    {
                        tp.Zones.Graveyard.AddCard(pick);
                        pick.SetZone(ZoneType.Graveyard);
                    }
                }) };
            });
    }
}
