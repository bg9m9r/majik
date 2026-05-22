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
/// Castigate / Pick the Brain / Aggressive Negotiations / Check for Traps
/// family: "Target opponent reveals their hand. You choose [filter] card from
/// it[ and exile that card | . Exile that card]."
///
/// Sister template to <see cref="RevealHandThenDiscardTemplate"/> — same shape,
/// just exile instead of discard. Does NOT cover the graveyard-alt-source
/// variants (Agonizing Remorse, Psychic Intrusion, Covetous Urge, Memory Leak,
/// Never Happened) because those choose from "graveyard or hand", which is a
/// different effect tree.
///
/// v1 stub: card-type filter captured but ignored; trailing rider clauses
/// (Aggressive Negotiations' +1/+1 counter, Soul Search's spirit-token,
/// Check for Traps' flash-card life rider, Pick the Brain's delirium clause)
/// are silently dropped. Deterministic picker takes the first non-land card in
/// the opponent's hand and exiles it.
/// </summary>
public sealed class RevealHandThenExileTemplate : ISpellTemplate
{
    // Two shapes:
    //   "...You choose a nonland card from it and exile that card."
    //   "...You choose a nonland card from it. Exile that card."
    // Filter qualifier (nonland, noncreature/nonland, artifact, …) captured.
    // The clause between "card from it" and the exile resolution can carry a
    // filter qualifier ("with mana value 4 or greater", "of your choice", etc.)
    // — accept up to a sentence boundary so Appetite for Brains and friends
    // bind. The captured filter is informational; runtime ignores it.
    private static readonly Regex Pattern = new(
        @"^target\s+(?:opponent|player)\s+reveals\s+their\s+hand\.\s*you\s+choose\s+(?:a|an)\s+(?<filter>[a-z][a-z0-9\s,\-]{0,80}?\s+)?card\s+from\s+it(?:\s+[^.]{0,80}?)?(?:\s+and\s+exile\s+that\s+card|\.\s*exile\s+that\s+card)\.",
        RegexOptions.IgnoreCase);

    public int Priority => 95;
    public string Name => "RevealHandThenExile";
    public BotIntent Intent => BotIntent.Discard;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var filter = m.Groups["filter"].Success ? m.Groups["filter"].Value.Trim() : "";
        return new Dictionary<string, string> { ["filter"] = filter };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CastigateSpell(ctx.Resolver);

    /// <summary>
    /// v1 stub: deterministic pick — first non-land card in opponent's hand
    /// goes to exile. Card-type filter is ignored (lossy semantic).
    /// </summary>
    private static SpellDefinition CastigateSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target opponent", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("reveal-hand-then-exile", () =>
            {
                if (target is not Player tp) return;
                var pick = tp.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
                if (pick is null) return;
                tp.Zones.Hand.RemoveCard(pick);
                tp.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);
            }) };
        });
}
