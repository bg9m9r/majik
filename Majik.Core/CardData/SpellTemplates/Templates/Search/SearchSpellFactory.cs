using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

internal static class SearchSpellFactory
{
    internal static SpellDefinition SearchLibrarySpell(Player caster, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"tutor {kindRaw}", () =>
        {
            // CR 701.19a — searches consult the agent. The kind predicate
            // pre-filters the candidate list; the agent picks zero or one.
            // Returning null = decline to find (legal under 701.19a).
            bool Pred(ICard c) => kindRaw.ToLowerInvariant() switch
            {
                "basic land" => c.HasType(CardType.Land),
                "land" => c.HasType(CardType.Land),
                "creature" => c.HasType(CardType.Creature),
                "artifact" => c.HasType(CardType.Artifact),
                "enchantment" => c.HasType(CardType.Enchantment),
                "instant" => c.HasType(CardType.Instant),
                "sorcery" => c.HasType(CardType.Sorcery),
                "planeswalker" => c.HasType(CardType.Planeswalker),
                // Empty / "card" = generic tutor — any library card qualifies.
                "" or "card" => true,
                _ => false,
            };
            var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();

            // CR 701.19a — LibrarySearch.PromptOnly always prompts the
            // agent (so a human searcher sees the full library with no
            // eligible cards highlighted and a single Acknowledge button
            // even when candidates is empty — the silent-no-op behaviour
            // that surprised the user on a Green Sun's Zenith into a deck
            // with zero green creatures is exactly what this helper is
            // designed to prevent).
            // TODO: remove sync-over-async once IEffect.Execute becomes async.
            var pickCtx = BuildPickContext(caster, p);
            var pick = LibrarySearch.PromptOnly(
                caster, candidates,
                string.IsNullOrEmpty(kindRaw) ? "card" : kindRaw + " card",
                pickCtx);
            if (pick != null)
            {
                caster.Zones.Library.RemoveCard(pick);
                caster.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
            // CR 701.20a — "If a player searches a library, that library
            // is shuffled afterward." LibraryShuffle pulls the active
            // GameRandom from GameRandomRegistry (deterministic when
            // tests seed it) and publishes a LibraryShuffledEvent.
            // Shuffles whether or not a card was actually found.
            LibraryShuffle.ShuffleLibrary(caster, $"search/{kindRaw}");
        }) });

    // Basic land names per CR 305.6.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase) { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    // The four "Farseek" basic land TYPES (CR 305.6) — note this is a TYPE
    // match (CardSubtype), not a name match, so it catches nonbasic duals /
    // shocks / triomes that carry one of these subtypes, and deliberately
    // EXCLUDES the fifth basic land type, Forest.
    private static readonly CardSubtype[] FarseekLandTypes =
        { CardSubtype.Plains, CardSubtype.Island, CardSubtype.Swamp, CardSubtype.Mountain };

    /// <summary>Sentinel <c>kindRaw</c> for the Farseek family — match any
    /// land card whose subtypes include Plains, Island, Swamp, or Mountain
    /// (CR 305.6 basic land types), basic or not.</summary>
    internal const string PlainsIslandSwampMountainKind = "plains/island/swamp/mountain land type";

    internal static SpellDefinition SearchLandToBattlefieldSpell(
        Player caster, string kindRaw, bool tapped) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"tutor land -> battlefield{(tapped ? " tapped" : "")}", () =>
        {
            bool MatchesFarseekType(ICard c) =>
                Array.Exists(FarseekLandTypes, c.HasSubtype);

            bool isFarseekKind = string.Equals(
                kindRaw, PlainsIslandSwampMountainKind, StringComparison.OrdinalIgnoreCase);

            bool Pred(ICard c)
            {
                if (!c.HasType(CardType.Land)) return false;
                // Farseek (CR 305.6): match by basic land TYPE — catches
                // nonbasic duals/shocks/triomes carrying one of the four
                // subtypes; excludes Forest. Distinct from the basic-NAME
                // match used by Rampant Growth / Cultivate.
                if (isFarseekKind) return MatchesFarseekType(c);
                if (kindRaw.Contains("basic", StringComparison.OrdinalIgnoreCase))
                    return BasicLandNames.Contains(c.Name);
                return true;
            }

            var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();

            // CR 701.19a — prompt agent even on zero candidates so the
            // human searcher sees the failed search rather than a silent
            // no-op (see LibrarySearch xmldoc).
            var pickCtx = BuildPickContext(caster, p);
            var pick = LibrarySearch.PromptOnly(
                caster, candidates,
                isFarseekKind
                    ? "Plains, Island, Swamp, or Mountain card"
                    : kindRaw.Contains("basic", StringComparison.OrdinalIgnoreCase)
                        ? "basic land card" : "land card",
                pickCtx);
            if (pick != null)
            {
                // CR 603.6a / CR 614 — route through ZoneService so ETB
                // triggers (bounce-land bounce, Amulet of Vigor untap) and
                // enters-tapped replacements fire on the tutored land. The
                // registry lookup falls back to raw mutation when no live
                // service is wired (shape / dispatcher-test path).
                var zones = ZoneServiceRegistry.Get(caster);
                if (zones != null)
                {
                    zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
                    if (tapped && pick is Permanent permTapped && !permTapped.IsTapped)
                    {
                        // Printed "tapped" rider — tap AFTER ZoneService.MoveCard
                        // so any ETB-tapped replacement (shock lands, bounce
                        // lands) has already applied. Double-tapping a tapped
                        // permanent is a no-op; an Amulet-of-Vigor untap trigger
                        // is already pending from the move, so the post-move
                        // tap doesn't suppress it.
                        permTapped.Tap();
                    }
                }
                else
                {
                    caster.Zones.Library.RemoveCard(pick);
                    caster.Zones.Battlefield.AddCard(pick);
                    pick.SetZone(ZoneType.Battlefield);
                    if (tapped && pick is Permanent perm)
                        perm.Tap();
                }
            }
            // CR 701.20a — shuffle after a search effect (see SearchLibrarySpell).
            // Shuffles whether or not a card was actually found.
            LibraryShuffle.ShuffleLibrary(caster, $"search-land/{kindRaw}");
        }) });

    /// <summary>
    /// Cultivate / Kodama's Reach template — "Search your library for up
    /// to two basic land cards, reveal those cards, put one onto the
    /// battlefield tapped and the other into your hand, then shuffle."
    /// (CR 701.19a search + CR 701.20a post-search shuffle.)
    ///
    /// Resolution shape:
    ///  - Prompt the caster's agent for the first basic land card. Agent
    ///    may decline (return null) — that's a legal "up to two" no-op.
    ///  - Prompt again for the second basic land card (excluding the
    ///    first pick). Agent may decline.
    ///  - Of the picks made, the FIRST pick goes to the battlefield
    ///    tapped (the prompt label calls this out explicitly so the
    ///    agent can score it as the BF slot) and the SECOND pick goes
    ///    to hand. When only one pick is made it goes to the battlefield
    ///    tapped (matches the bot's typical greedy ramp preference and
    ///    is one of the legal partitions of an "up to two" cast where
    ///    the player chose one card).
    ///  - The library is shuffled once at the end (CR 701.20a — a single
    ///    search effect performs one shuffle even when finding multiple
    ///    cards).
    ///
    /// The deterministic fallback (no agent registered) takes the first
    /// two basic land candidates from the library in iteration order —
    /// mirrors the <see cref="VeteranExplorerFactory"/> shape so the
    /// shape-only test path produces stable observations.
    /// </summary>
    internal static SpellDefinition SearchUpToTwoBasicsBattlefieldAndHandSpell(
        Player caster, string effectLabel) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"{effectLabel}: up to two basics -> battlefield-tapped + hand", async ctx =>
        {
            bool IsBasicLand(ICard c) => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name);

            var agent = ctx.Agent ?? AgentRegistry.Get(caster);
            var pickCtx = BuildPickContext(caster, p);
            var picks = new List<ICard>(capacity: 2);

            // First pick — destined for the battlefield (tapped).
            var firstCandidates = caster.Zones.Library.GetCards().Where(IsBasicLand).ToList();
            if (firstCandidates.Count > 0)
            {
                ICard? first = agent != null
                    ? (await agent.ChooseLibraryPickAsync(pickCtx, firstCandidates,
                        "basic land card to put onto the battlefield tapped").ConfigureAwait(false))
                    : firstCandidates[0];
                if (first != null) picks.Add(first);
            }

            // Second pick — destined for hand. Agent may decline ("up to two").
            var secondCandidates = caster.Zones.Library.GetCards()
                .Where(c => IsBasicLand(c) && !ReferenceEquals(c, picks.Count > 0 ? picks[0] : null))
                .ToList();
            if (secondCandidates.Count > 0)
            {
                ICard? second = agent != null
                    ? (await agent.ChooseLibraryPickAsync(pickCtx, secondCandidates,
                        "basic land card to put into your hand").ConfigureAwait(false))
                    : secondCandidates[0];
                if (second != null) picks.Add(second);
            }

            // Move the first pick to battlefield tapped (when present).
            // Second pick to hand (when present). When only one pick was
            // made it goes to the battlefield tapped — see class docs.
            if (picks.Count >= 1)
            {
                var bfPick = picks[0];
                var zones = ZoneServiceRegistry.Get(caster);
                if (zones != null)
                {
                    zones.MoveCard(bfPick, ZoneType.Library, ZoneType.Battlefield, caster);
                    if (bfPick is Permanent permTapped && !permTapped.IsTapped)
                        permTapped.Tap();
                }
                else
                {
                    caster.Zones.Library.RemoveCard(bfPick);
                    caster.Zones.Battlefield.AddCard(bfPick);
                    bfPick.SetZone(ZoneType.Battlefield);
                    if (bfPick is Permanent perm) perm.Tap();
                }
            }
            if (picks.Count >= 2)
            {
                var handPick = picks[1];
                caster.Zones.Library.RemoveCard(handPick);
                caster.Zones.Hand.AddCard(handPick);
                handPick.SetZone(ZoneType.Hand);
            }

            // CR 701.20a — shuffle once after the search effect, even
            // when zero cards were found (the search still happened).
            LibraryShuffle.ShuffleLibrary(caster, $"search-two-basics/{effectLabel}");
        }) });

    /// <summary>
    /// Green Sun's Zenith template — {X}{G} sorcery (Rule 107.4b X cost).
    /// Tutors the first library card whose color matches <paramref name="colorRaw"/> and
    /// whose mana value ≤ X, placing it directly onto the battlefield (CR 701.19a).
    ///
    /// Color is determined by <see cref="CardColors.GetColors"/>, which derives color
    /// from the card's mana cost pips (CR 105.2a).
    ///
    /// Post-resolution self-return-to-library (the "Shuffle Green Sun's Zenith into
    /// its owner's library" clause, CR 608.2c override) is DEFERRED — v1 lets the
    /// spell go to the graveyard like any other sorcery. Engine infrastructure for
    /// a generic "ShuffleSourceToLibraryOnResolve" hook in SpellCastFlow is needed
    /// to implement it correctly.
    /// </summary>
    internal static SpellDefinition GreenSunsZenithSpell(Player caster, string colorRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p =>
        {
            var x = p.X ?? 0;
            // Map the oracle-text color word to the ManaColor enum value.
            var targetColor = colorRaw.ToLowerInvariant() switch
            {
                "white"  => ManaColor.White,
                "blue"   => ManaColor.Blue,
                "black"  => ManaColor.Black,
                "red"    => ManaColor.Red,
                "green"  => ManaColor.Green,
                _        => ManaColor.Green,
            };
            return new IEffect[] { new Effect($"GSZ x={x}", () =>
            {
                var candidates = caster.Zones.Library.GetCards()
                    .Where(c =>
                        c.HasType(CardType.Creature) &&
                        CardColors.GetColors(c).Contains(targetColor) &&
                        ManaCost.Parse(c.ManaCost).TotalValue <= x)
                    .ToList();

                // CR 701.19a — prompt agent even on zero candidates (see
                // LibrarySearch xmldoc). This is the exact path that
                // silently no-op'd when a user cast GSZ into a deck with
                // no green creatures matching the chosen X.
                var pickCtx = BuildPickContext(caster, p);
                var pick = LibrarySearch.PromptOnly(
                    caster, candidates,
                    $"{colorRaw} creature card with mana value {x} or less",
                    pickCtx);
                if (pick != null)
                {
                    // CR 603.6a — route through ZoneService so ETB triggers
                    // on the tutored creature fire.
                    var zones = ZoneServiceRegistry.Get(caster);
                    if (zones != null)
                    {
                        zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
                    }
                    else
                    {
                        caster.Zones.Library.RemoveCard(pick);
                        caster.Zones.Battlefield.AddCard(pick);
                        pick.SetZone(ZoneType.Battlefield);
                    }
                }
                // CR 701.20a — shuffle after a search effect, regardless
                // of whether a card was found.
                LibraryShuffle.ShuffleLibrary(caster, "green-suns-zenith");
            }) };
        });

    /// <summary>
    /// Build a best-effort <see cref="GameContext"/> for a tutor closure so
    /// the agent's <c>ChooseLibraryPickAsync</c> can see opponent board
    /// state. The closure model is sync-over-async — at execution time we
    /// only have <see cref="ChosenSpellParams.AllPlayers"/> from the cast
    /// flow plus the caster; we don't have the live priority window, turn
    /// number, phase, or stack reference. Fill those with neutral
    /// placeholders (caster as active player, fresh empty stack, no phase)
    /// — <see cref="Majik.Bot.Heuristic.LibraryPickPolicy"/> only consumes
    /// <c>AllPlayers</c>, so the placeholders are inert in v2. Returns null
    /// when the roster is unavailable so the policy falls back to its
    /// pre-ctx neutral defaults instead of seeing a single-player game.
    /// </summary>
    private static GameContext? BuildPickContext(Player caster, ChosenSpellParams p)
    {
        if (p.AllPlayers is null || p.AllPlayers.Count == 0) return null;
        return new GameContext(
            self: caster,
            allPlayers: p.AllPlayers,
            activePlayer: caster,
            turnNumber: 0,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());
    }
}
