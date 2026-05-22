using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "Wheel" — every player shuffles their hand and graveyard into their
/// library, then draws N cards:
///
///   "Each player shuffles their hand and graveyard into their library,
///    then draws seven cards."
///
/// Cards: Day's Undoing, Echo of Eons, Emergency Powers, Time Reversal.
/// Uses <see cref="ChosenSpellParams.AllPlayers"/> populated by
/// <see cref="SpellCastFlow"/> to iterate all players.
///
/// v1 stub: hand + graveyard → library (no shuffle; deterministic order),
/// then draw N. Trailing rider clauses (Day's Undoing's "If it's your turn,
/// end the turn", "Exile [name]" self-exile) are dropped.
/// </summary>
public sealed class WheelTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^each\s+player\s+shuffles\s+their\s+hand\s+and\s+graveyard\s+into\s+their\s+library,\s+then\s+draws\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?\.",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "Wheel";
    public BotIntent Intent => BotIntent.Draw | BotIntent.Discard;

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
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => new IEffect[] { new Effect($"wheel {n}", () =>
            {
                var players = p.AllPlayers;
                if (players is null || players.Count == 0) return;
                foreach (var pl in players)
                {
                    MoveZoneToLibrary(pl, pl.Zones.Hand);
                    MoveZoneToLibrary(pl, pl.Zones.Graveyard);
                    DrawN(pl, n);
                }
            }) });
    }

    private static void MoveZoneToLibrary(Player pl, Majik.Core.Zones.IZone zone)
    {
        // Snapshot the cards first so we don't mutate the collection mid-iter.
        var cards = zone.GetCards().ToList();
        foreach (var c in cards)
        {
            zone.RemoveCard(c);
            pl.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private static void DrawN(Player pl, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = pl.Zones.Library.GetCards().FirstOrDefault();
            if (top is null) return;
            pl.Zones.Library.RemoveCard(top);
            pl.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }
}
