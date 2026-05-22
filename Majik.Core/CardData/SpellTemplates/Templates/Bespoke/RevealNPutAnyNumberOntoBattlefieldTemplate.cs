using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Genesis Wave / Glacial Revelation-shape:
///
///   "Reveal the top N cards of your library. You may put any number of
///    [filter] cards from among them onto the battlefield. [Then] put the rest
///    [on the bottom of your library in a random order | into your hand | …]."
///
/// Distinct from <see cref="RevealNPutOneCreatureOntoBattlefieldTemplate"/>:
/// "any number of [filter] cards" rather than "a [filter] card" (one).
///
/// v1 simplifications:
/// - Greedy: put EVERY revealed card whose type matches a permanent type
///   (artifact/creature/enchantment/land/planeswalker — Battle/Saga inclusive)
///   onto the battlefield. We do not filter by sub-attributes — Genesis Wave's
///   "mana value X or less" and Glacial Revelation's "snow" restriction are
///   dropped (lossy — any permanent qualifies).
/// - The "rest" destination is detected from the trailing clause; we honour
///   "into your hand" (Glacial Revelation), defaulting to bottom-of-library
///   otherwise.
/// - "Random order" / "any order" for the bottomed pile is lossy — append in
///   reveal order.
/// - Direct zone movement — ETB triggers do not fire in the stub. Matches the
///   lossy semantic of existing reanimation-to-battlefield templates.
/// </summary>
public sealed class RevealNPutAnyNumberOntoBattlefieldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^reveal\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|x)\s+cards\s+of\s+your\s+library\.\s*you\s+may\s+put\s+any\s+number\s+of\s+(?<filter>[a-z][\w\s,'\-]{0,80}?)?\s*cards?(?:\s+[a-z][\w\s,'\-]{0,80}?)?\s+from\s+among\s+them\s+onto\s+the\s+battlefield",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex RestToHand = new(
        @"put\s+the\s+rest\s+into\s+your\s+hand",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 70;
    public string Name => "RevealNPutAnyNumberOntoBattlefield";
    public BotIntent Intent => BotIntent.Ramp | BotIntent.Reanimate;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        return new Dictionary<string, string>
        {
            ["n"] = m.Groups["n"].Value,
            ["rest"] = RestToHand.IsMatch(oracleText) ? "hand" : "bottom",
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        // Genesis Wave's "X" — without a real X-cost binding we treat as 0
        // here; in practice the caller-supplied X (when implemented) would be
        // substituted before Rehydrate. For now WordToInt("x") returns 0,
        // which yields a no-op effect (zero cards revealed). Concrete digits
        // and word-numbers behave correctly.
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        var restToHand = @params.GetValueOrDefault("rest", "bottom") == "hand";
        return RevealNPutAnyNumberSpell(ctx.Caster, n, restToHand);
    }

    private static SpellDefinition RevealNPutAnyNumberSpell(Player caster, int n, bool restToHand) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("RevealNPutAnyNumberOntoBattlefield", () =>
        {
            var revealed = caster.Zones.Library.GetCards().Take(n).ToList();

            foreach (var card in revealed)
            {
                caster.Zones.Library.RemoveCard(card);
                if (IsPermanent(card))
                {
                    caster.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                }
                else if (restToHand)
                {
                    caster.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
                else
                {
                    // Bottomed — append in reveal order (lossy "random order" v1).
                    caster.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }
            }
        }) });

    private static bool IsPermanent(ICard card) =>
        card.HasType(CardType.Artifact)
        || card.HasType(CardType.Creature)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Land)
        || card.HasType(CardType.Planeswalker);
}
