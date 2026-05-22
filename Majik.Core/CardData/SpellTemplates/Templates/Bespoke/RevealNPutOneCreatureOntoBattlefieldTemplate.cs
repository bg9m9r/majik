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
/// Aethermage's Touch / See the Unwritten-shape:
///
///   "Reveal the top N cards of your library. You may put a [filter] card from
///    among them onto the battlefield. Then put the rest [on the bottom of
///    your library in any/random order | into your graveyard]."
///
/// Distinct from <see cref="Library.ImpulseMayRevealFilterTemplate"/>:
/// that one puts the chosen card into HAND. This one puts it onto the
/// BATTLEFIELD (different effect tree entirely).
///
/// v1 simplifications:
/// - Pick the topmost matching (creature) card greedily.
/// - The "filter" capture is ignored at resolution — we look for the first
///   creature card. Most cards in this family use a creature filter (Aethermage's
///   Touch, See the Unwritten). Non-creature filters fall through cleanly.
/// - "Random order" / "any order" for the bottomed pile is lossy — append in
///   reveal order.
/// - Aethermage's Touch's "At the beginning of your end step, return this
///   creature to its owner's hand" rider is dropped at v1 (no delayed-trigger
///   wiring here).
/// - Direct zone movement — ETB triggers do not fire in the stub. Matches the
///   lossy semantic of existing reanimation-to-battlefield templates.
/// </summary>
public sealed class RevealNPutOneCreatureOntoBattlefieldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^reveal\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards\s+of\s+your\s+library\.\s*you\s+may\s+put\s+(?:a|an)\s+(?<filter>[a-z][\w\s,'\-]{0,60}?)?\s*card\s+from\s+among\s+them\s+onto\s+the\s+battlefield",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 70;
    public string Name => "RevealNPutOneCreatureOntoBattlefield";
    public BotIntent Intent => BotIntent.Reanimate | BotIntent.Ramp;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        return RevealNPutOneCreatureSpell(ctx.Caster, n);
    }

    private static SpellDefinition RevealNPutOneCreatureSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("RevealNPutOneCreatureOntoBattlefield", () =>
        {
            var revealed = caster.Zones.Library.GetCards().Take(n).ToList();
            ICard? chosen = revealed.FirstOrDefault(c => c.HasType(CardType.Creature));

            foreach (var card in revealed)
            {
                caster.Zones.Library.RemoveCard(card);
                if (ReferenceEquals(card, chosen))
                {
                    caster.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                }
                else
                {
                    // Bottomed — append in reveal order (lossy "random/any order" v1).
                    caster.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }
            }
        }) });
}
