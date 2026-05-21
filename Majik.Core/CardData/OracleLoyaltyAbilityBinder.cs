using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Database;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Parses planeswalker oracle text and attaches <see cref="LoyaltyAbility"/>
/// instances to the card.
///
/// Effect bodies recognise a curated subset of common patterns; unknown
/// effect text attaches an empty no-op so the loyalty-change still applies.
/// </summary>
public static class OracleLoyaltyAbilityBinder
{
    // Matches a loyalty-ability cost line.
    // Accepts ASCII '+', ASCII '-', or unicode '−' (U+2212) for sign.
    // Groups: <sign>, <n>, <body>
    private static readonly Regex LoyaltyLine = new(
        @"(?m)^\s*(?<sign>[+\-−]?)(?<n>\d+):\s*(?<body>[^\n]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse <paramref name="entity"/>'s oracle text and attach a
    /// <see cref="LoyaltyAbility"/> to <paramref name="card"/> for every
    /// loyalty-cost line found. No-ops if <paramref name="card"/> is not a
    /// <see cref="Planeswalker"/> or oracle text is absent.
    /// </summary>
    /// <param name="allPlayers">
    /// Optional full player list. When supplied, "each opponent" effects
    /// apply to every player except <paramref name="controller"/>. When
    /// null those effects are silent no-ops (correct loyalty change still
    /// fires). Pass this in once <c>GameFacade</c> wires the binder.
    /// </param>
    public static void Bind(ICard card, CardEntity entity, Player controller,
        IReadOnlyList<Player>? allPlayers = null)
    {
        if (card is not Planeswalker pw) return;
        if (entity?.OracleText is not string text) return;

        foreach (Match m in LoyaltyLine.Matches(text))
        {
            var sign = m.Groups["sign"].Value;
            var n = int.Parse(m.Groups["n"].Value);
            var body = m.Groups["body"].Value.Trim();

            int loyaltyChange = sign switch
            {
                "+" => n,
                "-" => -n,
                "−" => -n,   // U+2212 MINUS SIGN
                _ => 0,           // bare "0:" abilities
            };

            Action effect = BuildEffect(body, controller, allPlayers);
            pw.AddAbility(new LoyaltyAbility(pw, loyaltyChange, effect));
        }
    }

    // --- effect pattern regexes ---

    private static readonly Regex DrawCards = new(
        @"draw\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EachOpponentLoses = new(
        @"each\s+opponent\s+loses\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreateTreasure = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+treasure\s+tokens?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YouGainLife = new(
        @"you\s+gain\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches "each opponent mills N cards" — Ashiok, Dream Render −1
    /// (2019 print simplified for v1).
    /// </summary>
    private static readonly Regex EachOpponentMillsN = new(
        @"each\s+opponent\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches "return target creature card from a graveyard to the
    /// battlefield" — Grist −2. V1: no targeting prompt; first creature
    /// in controller's graveyard is chosen automatically.
    /// <para>
    /// <b>Deferred:</b> full targeting prompt (requires a target-selection
    /// UI); "from any graveyard" (currently only controller's).
    /// </para>
    /// </summary>
    private static readonly Regex ReturnTargetCreatureFromGraveyard = new(
        @"return\s+target\s+creature\s+card\s+from\s+(?:a|your)\s+graveyard\s+to\s+(?:the|its\s+owner'?s?)\s+battlefield",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches "create a 1/1 [color and color|colorless] Insect creature token"
    /// — Grist +1.
    /// </summary>
    private static readonly Regex CreateInsectToken = new(
        @"create\s+a\s+1/1\s+(?:black\s+and\s+green|green\s+and\s+black|colorless)\s+insect\s+creature\s+token",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Action BuildEffect(string body, Player controller,
        IReadOnlyList<Player>? allPlayers)
    {
        // "Draw N cards." / "Draw a card."
        var mDraw = DrawCards.Match(body);
        if (mDraw.Success)
        {
            var n = WordToInt(mDraw.Groups["n"].Value);
            return () => DrawCards_(controller, n);
        }

        // "Each opponent loses N life." — opponents are not resolvable from
        // a single-controller scope; v1 registers a no-op so the loyalty
        // change still fires. A future slice will thread AllPlayers through.
        if (EachOpponentLoses.IsMatch(body))
            return () => { };

        // "Create N Treasure token(s)."
        var mTreasure = CreateTreasure.Match(body);
        if (mTreasure.Success)
        {
            var n = WordToInt(mTreasure.Groups["n"].Value);
            return () =>
            {
                for (var i = 0; i < n; i++)
                    TokenFactory.CreateTreasure(controller);
            };
        }

        // "You gain N life."
        var mLife = YouGainLife.Match(body);
        if (mLife.Success)
        {
            var n = WordToInt(mLife.Groups["n"].Value);
            return () => controller.GainLife(n);
        }

        // "Each opponent mills N cards." (Ashiok, Dream Render −1, 2019 print simplified)
        // When allPlayers is null the effect is a silent no-op; loyalty change still fires.
        var mMill = EachOpponentMillsN.Match(body);
        if (mMill.Success)
        {
            var n = WordToInt(mMill.Groups["n"].Value);
            return () =>
            {
                if (allPlayers == null) return;
                foreach (var p in allPlayers)
                {
                    if (!ReferenceEquals(p, controller))
                        MillAction.Apply(p, n);
                }
            };
        }

        // "Return target creature card from a graveyard to the battlefield."
        // (Grist −2). V1 auto-picks the first creature in controller's graveyard;
        // full targeting prompt deferred.
        var mReturn = ReturnTargetCreatureFromGraveyard.Match(body);
        if (mReturn.Success)
        {
            return () =>
            {
                var pick = controller.Zones.Graveyard.GetCards()
                    .OfType<Creature>().FirstOrDefault();
                if (pick == null) return;
                controller.Zones.Graveyard.RemoveCard(pick);
                controller.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
            };
        }

        // "Create a 1/1 black and green Insect creature token." (Grist +1).
        if (CreateInsectToken.IsMatch(body))
        {
            return () =>
            {
                var spec = new TokenFactory.TokenSpec(
                    Name: "Insect",
                    Power: 1,
                    Toughness: 1,
                    Subtypes: new[] { CardSubtype.Insect });
                TokenFactory.CreateOnBattlefield(spec, controller);
            };
        }

        // Fallback: unrecognised effect text — no-op body so loyalty change applies.
        return () => { };
    }

    private static void DrawCards_(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    private static int WordToInt(string s) => s.ToLowerInvariant() switch
    {
        "a" or "an" or "one" => 1,
        "two" => 2,
        "three" => 3,
        "four" => 4,
        "five" => 5,
        "six" => 6,
        "seven" => 7,
        "eight" => 8,
        "nine" => 9,
        "ten" => 10,
        _ => int.TryParse(s, out var v) ? v : 0,
    };
}
