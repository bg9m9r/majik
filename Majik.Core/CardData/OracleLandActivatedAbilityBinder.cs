using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Binds activated abilities on Land cards synthesised from oracle text.
///
/// Currently covers the fetch-land cycle (Misty Rainforest, Verdant Catacombs,
/// Windswept Heath, etc.) whose oracle text follows the pattern:
///
///   "{T}, Pay 1 life, Sacrifice &lt;name&gt;: Search your library for a
///    &lt;BasicA&gt; or &lt;BasicB&gt; card, put it onto the battlefield, then shuffle."
///
/// The resulting <see cref="ActivatedAbility"/> carries three costs:
///   1. Tap the fetch land (<see cref="AdditionalCost.Tap"/>)
///   2. Pay 1 life (<see cref="AdditionalCost.PayLife"/>)
///   3. Sacrifice the fetch land (<see cref="AdditionalCost.Sacrifice"/>)
///
/// The effect searches the controller's library for the first card whose
/// <see cref="ICard.Subtypes"/> contains either target land subtype and
/// moves it directly to the battlefield. Shuffling is a no-op stub until a
/// shuffle-hook is plumbed through the engine (CR 701.19c).
/// </summary>
public static class OracleLandActivatedAbilityBinder
{
    // Matches: "{T}, Pay 1 life, Sacrifice <anything>: Search your library for a
    //           <Plains|Island|Swamp|Mountain|Forest> or <Plains|Island|Swamp|Mountain|Forest> card"
    private static readonly Regex FetchLand = new(
        @"\{T\}\s*,\s*Pay\s+1\s+life\s*,\s*Sacrifice\s+[^:]+:\s*Search\s+your\s+library\s+for\s+a\s+" +
        @"(?<a>Plains|Island|Swamp|Mountain|Forest)\s+or\s+(?<b>Plains|Island|Swamp|Mountain|Forest)\s+card",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Map from oracle name → CardSubtype enum value.
    private static readonly Dictionary<string, CardSubtype> SubtypeByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Plains"]   = CardSubtype.Plains,
            ["Island"]   = CardSubtype.Island,
            ["Swamp"]    = CardSubtype.Swamp,
            ["Mountain"] = CardSubtype.Mountain,
            ["Forest"]   = CardSubtype.Forest,
        };

    /// <summary>
    /// Inspect <paramref name="entity"/>'s oracle text and, if a fetch-land
    /// pattern is detected, attach the corresponding <see cref="ActivatedAbility"/>
    /// to <paramref name="card"/>. Does nothing if the card is not a
    /// <see cref="Land"/>.
    /// </summary>
    /// <returns><c>true</c> when an ability was attached; <c>false</c> otherwise.</returns>
    public static bool Bind(ICard card, CardEntity entity, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        // Only bind to Land permanents.
        if (card is not Land land) return false;

        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = FetchLand.Match(text);
        if (!m.Success) return false;

        var subtypeNameA = m.Groups["a"].Value;
        var subtypeNameB = m.Groups["b"].Value;

        if (!SubtypeByName.TryGetValue(subtypeNameA, out var subtypeA) ||
            !SubtypeByName.TryGetValue(subtypeNameB, out var subtypeB))
        {
            return false;
        }

        // Capture for closure — avoid capturing the match object.
        var fetchLand = land;
        var ctrl = controller;

        var ability = new ActivatedAbility(
            source: fetchLand,
            controller: ctrl,
            costs: new ICost[]
            {
                AdditionalCost.Tap(fetchLand),
                AdditionalCost.PayLife(1),
                AdditionalCost.Sacrifice(fetchLand),
            },
            effects: new IEffect[]
            {
                new Effect(
                    $"search library for {subtypeNameA} or {subtypeNameB} and put onto battlefield",
                    () => FetchEffect(ctrl, subtypeA, subtypeB)),
            });

        fetchLand.AddAbility(ability);
        return true;
    }

    private static void FetchEffect(Player controller, CardSubtype subtypeA, CardSubtype subtypeB)
    {
        // CR 701.19a / CR 701.19c — gather the legal candidates (lands whose
        // subtypes include either of the two basics the fetchland names), let
        // the controller's agent pick one, then route the chosen card to the
        // battlefield via ZoneService so ETB triggers + ETB-tapped
        // replacements fire on the tutored land (Underground Mortuary surveil,
        // shock-land "may pay 2 life or enters tapped", bounce-land bounce,
        // Amulet of Vigor untap).
        //
        // Pre-fix this method called FirstOrDefault + raw zone mutation:
        //   * AgentRegistry was never consulted, so the engine silently
        //     auto-picked the first match. The human user saw their fetchland
        //     resolve without ever being asked which land to fetch — even
        //     after PR #1003 wired AgentRegistry on the GameFacade. The
        //     fetchland production path went through THIS binder, not through
        //     FetchLandCycleFactory (which DOES consult the agent), so the
        //     prompt never fired at the live table.
        //   * Raw Library.RemoveCard / Battlefield.AddCard bypassed
        //     ZoneService.MoveCard, so CardMovedEvent never published and no
        //     ETB replacement / trigger ran on the tutored land.
        //
        // Both paths now match FetchLandCycleFactory.TutorLandToBattlefield.
        var candidates = controller.Zones.Library
            .GetCards()
            .Where(c => c.HasType(CardType.Land)
                     && (c.HasSubtype(subtypeA) || c.HasSubtype(subtypeB)))
            .ToList();
        if (candidates.Count == 0) return;

        var agent = AgentRegistry.Get(controller);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "land card")
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null) return; // CR 701.19a — declining a successful search is legal.

        // CR 603.6a / CR 614 — route the Library → Battlefield move through
        // ZoneService when a live service is registered so the tutored land's
        // CardMovedEvent fires (drives bounce-land bounce + Amulet of Vigor
        // untap) and ETB-tapped replacements (shock lands paying 2 life,
        // bounce/surveil lands always tapped) run. Falls back to raw zone
        // mutation for the no-service test paths.
        var zones = ZoneServiceRegistry.Get(controller);
        if (zones != null)
        {
            zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }

        // CR 701.19c — "then shuffle." Route through the shared library-shuffle
        // helper for parity with FetchLandCycleFactory.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "fetch-land");
    }
}
