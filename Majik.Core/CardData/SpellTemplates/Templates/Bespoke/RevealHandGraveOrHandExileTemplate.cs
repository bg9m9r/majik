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
/// Graveyard-or-hand exile variant of the Duress shape — chooses from a wider
/// source pool than <see cref="RevealHandThenExileTemplate"/>:
///
///   "Target opponent reveals their hand. You choose [filter] card from that
///    player's graveyard or hand and exile it."
///
/// Cards: Covetous Urge, Never Happened, Psychic Intrusion.
///
/// v1 stub: deterministic pick from hand only (graveyard alt source is
/// ignored). Trailing "You may cast that card…" rider (Covetous Urge, Psychic
/// Intrusion) is dropped at resolution — the load-bearing exile still happens.
/// </summary>
public sealed class RevealHandGraveOrHandExileTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^target\s+opponent\s+reveals\s+their\s+hand\.\s*you\s+choose\s+(?:a|an)\s+(?<filter>[a-z][a-z0-9\s,\-]{0,40}?\s+)?card\s+from\s+(?:that\s+player'?s|their)\s+graveyard\s+or\s+hand\s+and\s+exile\s+it\.",
        RegexOptions.IgnoreCase);

    public int Priority => 95;
    public string Name => "RevealHandGraveOrHandExile";
    public BotIntent Intent => BotIntent.Discard | BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var filter = m.Groups["filter"].Success ? m.Groups["filter"].Value.Trim() : "";
        return new Dictionary<string, string> { ["filter"] = filter };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        var eventBus = ctx.EventBus;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target opponent", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("reveal-hand-grave-or-hand-exile", () =>
                {
                    if (target is not Player tp) return;
                    // CR 701.16 — opponent reveals their hand even though the
                    // alt source (graveyard) is already public.
                    RevealHelper.RevealHand(eventBus, tp, "RevealHandGraveOrHandExile");
                    var pick = tp.Zones.Hand.GetCards()
                        .FirstOrDefault(c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
                    if (pick is null) return;
                    tp.Zones.Hand.RemoveCard(pick);
                    tp.Zones.Exile.AddCard(pick);
                    pick.SetZone(ZoneType.Exile);
                }) };
            });
    }
}
