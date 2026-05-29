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
            // CR 701.15 — reveal top N, may put a creature card onto the
            // battlefield, rest to the bottom (lossy "any/random order"
            // collapses to reveal order — see class docs). Shared helper
            // surfaces the reveal pile to the agent (RemoteAgent →
            // portal modal with eligible cards highlighted, ineligibles
            // muted) instead of auto-picking the first creature.
            Majik.Core.Zones.RevealAndChoose.RevealTopAndChoose(
                caster: caster,
                count: n,
                eligiblePredicate: c => c.HasType(CardType.Creature),
                optional: true,
                label: "Creature to put onto the battlefield",
                pickedDestination: ZoneType.Battlefield,
                restDestination: ZoneType.Library,
                sourceTag: $"reveal-{n}-put-creature-bf");
        }) });
}
