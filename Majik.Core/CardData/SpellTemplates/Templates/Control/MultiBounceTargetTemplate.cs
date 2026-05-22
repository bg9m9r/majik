using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

/// <summary>
/// "Return up to N target creatures to their owners' hands." family —
/// e.g. Aether Tradewinds, Devastation Tide-style multi-bounce, Recoil-free
/// "Tidal Bore" types ("Return up to two target creatures to their owners'
/// hands."). v1 covers <c>up to two</c> / <c>up to three</c> /
/// <c>up to four</c> / <c>up to five</c>.
///
/// Single-target bounce stays with <see cref="BounceTargetTemplate"/> (its
/// regex doesn't accept the "up to N" prefix). This template only fires when
/// the prefix is present, so the two templates don't fight.
///
/// v1 stub: per-chosen-target return-to-hand. Doesn't model the per-target
/// legality predicate beyond the engine's default creature filter.
/// </summary>
public sealed class MultiBounceTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^return\s+up\s+to\s+(?<n>two|three|four|five)\s+target\s+creatures\s+to\s+(?:their|its)\s+owners?'?\s+hands?\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 65;
    public string Name => "MultiBounceTarget";
    public BotIntent Intent => BotIntent.Bounce;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var n = SpellTemplateHelpers.WordToInt(m.Groups["n"].Value);
        return new Dictionary<string, string> { ["n"] = n.ToString() };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = int.Parse(@params["n"]);
        var resolver = ctx.Resolver;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    $"up to {n} target creatures",
                    MinTargets: 0,
                    MaxTargets: n,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce),
            },
            EffectFactory: param =>
            {
                var chosen = param.Targets.Count > 0 ? param.Targets[0] : Array.Empty<object>();
                return new IEffect[] { new Effect($"bounce up to {n}", () =>
                {
                    foreach (var raw in chosen)
                    {
                        var resolved = resolver(raw);
                        if (resolved is ICard card) ReturnToOwnersHand(card);
                    }
                }) };
            });
    }

    // Mirrors ControlSpellFactory.ReturnToOwnersHand (which is private to
    // that factory). Kept inline so the template doesn't need to expose the
    // helper internally — also lets the comment travel with the bounce.
    private static void ReturnToOwnersHand(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            if (card.Zone == ZoneType.Battlefield)
                owner.Zones.Battlefield.RemoveCard(card);
            else if (card.Zone == ZoneType.Graveyard)
                owner.Zones.Graveyard.RemoveCard(card);
            else if (card.Zone == ZoneType.Exile)
                owner.Zones.Exile.RemoveCard(card);
            owner.Zones.Hand.AddCard(card);
        }
        card.SetZone(ZoneType.Hand);
    }
}
