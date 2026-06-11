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
    // Matches: "{T}, Pay 1 life, Sacrifice <anything>: Search your library for a[n]
    //           <Plains|Island|Swamp|Mountain|Forest> or <Plains|Island|Swamp|Mountain|Forest> card"
    //
    // The article is "a" for consonant-leading basics (a Forest, a Swamp) and
    // "an" for vowel-leading basics (an Island) — Polluted Delta / Scalding Tarn
    // read "an Island or ...". `an?` accepts both; missing it left those two
    // fetchlands as do-nothing lands in real games.
    private static readonly Regex FetchLand = new(
        @"\{T\}\s*,\s*Pay\s+1\s+life\s*,\s*Sacrifice\s+[^:]+:\s*Search\s+your\s+library\s+for\s+an?\s+" +
        @"(?<a>Plains|Island|Swamp|Mountain|Forest)\s+or\s+(?<b>Plains|Island|Swamp|Mountain|Forest)\s+card",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches Prismatic Vista's single-target form:
    //   "{T}, Pay 1 life, Sacrifice <anything>: Search your library for a basic
    //    land card, put it onto the battlefield, then shuffle."
    // Fetches ANY basic land (CR 205.4a — Basic supertype + Land card type)
    // rather than two specific basic-land subtypes.
    private static readonly Regex BasicLandFetch = new(
        @"\{T\}\s*,\s*Pay\s+1\s+life\s*,\s*Sacrifice\s+[^:]+:\s*Search\s+your\s+library\s+for\s+a\s+" +
        @"basic\s+land\s+card",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches the sac-fetch-onto-battlefield-TAPPED cycle:
    //   "{T}, Sacrifice this land: Search your library for a basic land card,
    //    put it onto the battlefield tapped, then shuffle."
    // (Evolving Wilds, Terramorphic Expanse) and Fabled Passage, which adds the
    //   "Then if you control four or more lands, untap that land." rider.
    //
    // Distinct from BasicLandFetch (Prismatic Vista) in two ways: NO "Pay 1
    // life" in the cost, and the fetched basic enters TAPPED. The Fabled
    // Passage conditional-untap rider is detected separately via
    // FabledPassageUntapRider below — its presence flips the effect to the
    // four-or-more-lands variant.
    private static readonly Regex BasicLandFetchTapped = new(
        @"\{T\}\s*,\s*Sacrifice\s+[^:]+:\s*Search\s+your\s+library\s+for\s+a\s+" +
        @"basic\s+land\s+card\s*,\s*put\s+it\s+onto\s+the\s+battlefield\s+tapped",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fabled Passage's printed rider, appended after the tapped-fetch line:
    //   "Then if you control four or more lands, untap that land."
    private static readonly Regex FabledPassageUntapRider = new(
        @"if\s+you\s+control\s+four\s+or\s+more\s+lands\s*,\s*untap\s+that\s+land",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fabled Passage's printed untap rider: untap the fetched land when the
    // controller controls at least this many lands.
    private const int FabledPassageUntapLandThreshold = 4;

    // Matches the Modern Horizons "Horizon Canopy" cycle's sac-to-draw line:
    //   "{1}, {T}, Sacrifice this land: Draw a card."
    // (Fiery Islet, Sunbaked Canyon, Horizon Canopy, Silent Clearing,
    // Nurturing Peatland, Waterlogged Grove). The pain-mana line on these
    // lands is bound separately by OracleManaBinder. The ability binds via
    // HorizonLandBinder.AttachSacDraw — costs {1} + {T} + Sacrifice, effect
    // draws the top card of the controller's library.
    private static readonly Regex HorizonSacDraw = new(
        @"\{1\}\s*,\s*\{T\}\s*,\s*Sacrifice\s+[^:]+:\s*Draw\s+a\s+card",
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

        // Horizon Canopy cycle sac-to-draw: "{1}, {T}, Sacrifice this land:
        // Draw a card." Distinct wording from any fetch-land form, so order
        // among these branches is for clarity, not correctness.
        if (HorizonSacDraw.IsMatch(text))
        {
            HorizonLandBinder.AttachSacDraw(land, controller);
            return true;
        }

        // Sac-fetch-onto-battlefield-TAPPED cycle: "{T}, Sacrifice this land:
        // Search your library for a basic land card, put it onto the
        // battlefield tapped, then shuffle." (Evolving Wilds, Terramorphic
        // Expanse) + Fabled Passage's conditional-untap rider. NO "Pay 1 life"
        // in the cost and the basic enters TAPPED — both distinguish this from
        // the Prismatic Vista form below, so ordering is for clarity.
        if (BasicLandFetchTapped.IsMatch(text))
        {
            var untapsWhenFourLands = FabledPassageUntapRider.IsMatch(text);
            BindBasicLandFetchTapped(land, controller, untapsWhenFourLands);
            return true;
        }

        // Prismatic Vista form first: "a basic land card" (single any-basic
        // target). The two-basic FetchLand regex never matches this wording, so
        // ordering is for clarity, not correctness.
        if (BasicLandFetch.IsMatch(text))
        {
            BindBasicLandFetch(land, controller);
            return true;
        }

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
                    ctx => FetchEffectAsync(ctrl, subtypeA, subtypeB, ctx)),
            });

        fetchLand.AddAbility(ability);
        return true;
    }

    /// <summary>
    /// Attaches the Prismatic Vista–style fetch ability: same Tap + Pay 1 life +
    /// Sacrifice cost as the colour-pair cycle, but the effect searches for ANY
    /// basic land (CR 205.4a) rather than two named basic-land subtypes.
    /// </summary>
    private static void BindBasicLandFetch(Land land, Player controller)
    {
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
                    "search library for a basic land and put onto battlefield",
                    ctx => BasicLandFetchEffectAsync(ctrl, ctx)),
            });

        fetchLand.AddAbility(ability);
    }

    private static async ValueTask BasicLandFetchEffectAsync(Player controller, ResolutionContext ctx)
    {
        // CR 205.4a — basic lands are those with the Basic supertype. Mirrors
        // PrismaticVistaFactory.TutorBasicLandToBattlefieldAsync so the live
        // binder path matches the (test-only) factory path exactly.
        var candidates = controller.Zones.Library
            .GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
            ctx, controller, candidates, "basic land card").ConfigureAwait(false);

        if (pick != null)
        {
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
        }

        // CR 701.20a — shuffle whether or not a card was found.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "fetch-land");
    }

    /// <summary>
    /// Attaches the sac-fetch-onto-battlefield-TAPPED ability shared by
    /// Evolving Wilds / Terramorphic Expanse (and Fabled Passage with its
    /// conditional-untap rider). Cost is <c>{T}</c> + Sacrifice only — NO "Pay
    /// 1 life" (unlike the fetchland / Prismatic Vista cycle). The effect
    /// searches for ANY basic land (CR 205.4a), puts it onto the battlefield
    /// TAPPED (printed rider; CR 305 / 614), then shuffles. When
    /// <paramref name="untapsWhenFourLands"/> is set (Fabled Passage), the
    /// fetched land is untapped afterwards iff the controller now controls four
    /// or more lands (the just-fetched land counts; the sacrificed source does
    /// not). Mirrors <c>TerramorphicExpanseFactory</c> / <c>FabledPassageFactory</c>
    /// so the live binder path matches the (test-only) factory path exactly.
    /// </summary>
    private static void BindBasicLandFetchTapped(Land land, Player controller, bool untapsWhenFourLands)
    {
        var fetchLand = land;
        var ctrl = controller;

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            untapsWhenFourLands
                ? "sac self + tutor basic land -> battlefield tapped, shuffle, untap if 4+ lands"
                : "sac self + tutor basic land -> battlefield tapped, shuffle",
            async ctx =>
            {
                // Self-sacrifice inlined in the resolve closure because
                // AdditionalCost.Sacrifice.Pay() is a no-op stub (same posture
                // as the factories). Must happen before the search so the
                // source is no longer on the battlefield and so it does not
                // inflate the four-or-more-lands count for Fabled Passage.
                SacrificeToOwnersGraveyard(fetchLand);

                await BasicLandFetchTappedEffectAsync(ctrl, untapsWhenFourLands, ctx)
                    .ConfigureAwait(false);
            });

        fetchAbility = new ActivatedAbility(
            source: fetchLand,
            controller: ctrl,
            costs: new ICost[]
            {
                AdditionalCost.Tap(fetchLand),
                AdditionalCost.Sacrifice(fetchLand),
            },
            effects: new IEffect[] { fetchEffect });

        fetchLand.AddAbility(fetchAbility);
    }

    private static void SacrificeToOwnersGraveyard(Land self)
    {
        // CR 701.16 — sacrifice moves the permanent to its owner's graveyard.
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    private static async ValueTask BasicLandFetchTappedEffectAsync(
        Player controller, bool untapsWhenFourLands, ResolutionContext ctx)
    {
        // CR 205.4a — basic lands are those with the Basic supertype.
        var candidates = controller.Zones.Library
            .GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        // CR 701.19a — prompt the agent even on zero candidates so a human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
            ctx, controller, candidates, "basic land card").ConfigureAwait(false);

        if (pick != null)
        {
            // Route Library -> Battlefield through ZoneService so ETB-tapped
            // replacements (snow basics) + CardMovedEvent subscribers (Amulet
            // of Vigor untap, bounce-land ETB triggers) fire; then apply the
            // printed "tapped" rider (CR 305 / 614).
            var zones = ZoneServiceRegistry.Get(controller);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, controller);
                if (pick is Permanent permTapped && !permTapped.IsTapped)
                {
                    permTapped.Tap();
                }
            }
            else
            {
                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(controller);
                if (pick is Permanent perm)
                {
                    perm.Tap();
                }
            }

            // Fabled Passage rider: "Then if you control four or more lands,
            // untap that land." The just-fetched land is already on the
            // battlefield and counts; the sacrificed source no longer counts.
            if (untapsWhenFourLands)
            {
                var landCount = controller.Zones.Battlefield.GetCards()
                    .Count(c => c.HasType(CardType.Land));
                if (landCount >= FabledPassageUntapLandThreshold
                    && pick is Permanent permUntap && permUntap.IsTapped)
                {
                    permUntap.Untap();
                }
            }
        }

        // CR 701.20a — shuffle whether or not a card was found.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "fetch-land");
    }

    private static async ValueTask FetchEffectAsync(Player controller, CardSubtype subtypeA, CardSubtype subtypeB, ResolutionContext ctx)
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

        // CR 701.19a — LibrarySearch.PromptOnly always prompts the agent
        // even when candidates is empty so a human searcher sees the
        // failed search rather than a silent no-op.
        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
            ctx, controller, candidates, "land card").ConfigureAwait(false);

        if (pick != null)
        {
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
        }

        // CR 701.19c — "then shuffle." Route through the shared library-shuffle
        // helper for parity with FetchLandCycleFactory. Shuffles whether or
        // not a card was actually found.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "fetch-land");
    }
}
