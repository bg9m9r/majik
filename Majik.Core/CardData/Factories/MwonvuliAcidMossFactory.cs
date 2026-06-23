using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mwonvuli Acid-Moss (Time Spiral / reprints, {2}{G}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy target land. Search your library for a Forest card, put that
///    card onto the battlefield tapped, then shuffle."
///
/// ## Analogue
/// The "destroy target land" front half is the
/// <see cref="StoneRainFactory"/> / <see cref="CleansingWildfireFactory"/>
/// primitive — a single 1..1 "target land" <see cref="TargetRequest"/> whose
/// gatherer enumerates every land permanent across all players, resolved by
/// moving the chosen land to its owner's graveyard (CR 701.7b; an illegal
/// pick is a no-op per CR 608.2b).
///
/// The back half is the "search your library for a Forest card, put it onto
/// the battlefield tapped, then shuffle" tutor — the same put-onto-battlefield-
/// tapped + post-search-shuffle resolution as
/// <see cref="CleansingWildfireFactory"/>'s compensation search, but here:
///   - the searcher is the CASTER (not the destroyed land's controller),
///   - the search is MANDATORY (no "may"), and
///   - the predicate is "a Forest card" — i.e. any library card carrying the
///     <see cref="CardSubtype.Forest"/> land subtype (basic Forest or a
///     nonbasic land with the Forest type, e.g. Stomping Ground / Breeding
///     Pool), NOT a basic-land-NAME match.
///
/// Card shape comes from the embedded JSON (<c>mwonvuli-acid-moss.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/> needs
/// a target resolver supplied by the caller's <see cref="GameContext"/> (not
/// expressible in the data-only JSON schema).
///
/// ## Implemented (v1)
/// - Sorcery identity at {2}{G}{G}, mono-green, mana value 4.
/// - <b>Destroy target land</b> — single 1..1 <see cref="TargetRequest"/>
///   (Intent <see cref="BotIntent.Removal"/>) whose gatherer enumerates every
///   land permanent on the battlefield across all players. On resolution the
///   target land moves to its owner's graveyard (CR 701.7b) iff it is still a
///   land on the battlefield (CR 608.2b — illegal target → no-op).
/// - <b>Forest tutor onto the battlefield tapped</b> — the caster searches
///   their library for a card with the Forest land subtype, puts it onto the
///   battlefield tapped, then shuffles (CR 701.19a / CR 701.20a). The library
///   is shuffled whether or not a card was found. The search resolves
///   independently of the destroy clause (CR 608.2e — left-to-right; the
///   tutor is not gated on a legal destroy target).
///
/// ## Deferred (matches every tutor factory)
/// - <b>Reveal event</b>: the tutored Forest moves Library → Battlefield
///   without publishing a reveal event (the oracle text has no "reveal it"
///   step, so this is faithful).
/// </summary>
[CardName("Mwonvuli Acid-Moss")]
public static class MwonvuliAcidMossFactory
{
    public const string CardName = "Mwonvuli Acid-Moss";
    public const string Slug = "mwonvuli-acid-moss";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Mwonvuli
    /// Acid-Moss. Single 1..1 "target land" request, no X. On resolution
    /// (CR 608.2e — left to right):
    ///   1. Destroy the target land if still legal (CR 701.7b / CR 608.2b).
    ///   2. The caster searches their library for a Forest card, puts it onto
    ///      the battlefield tapped, then shuffles (CR 701.19a / CR 701.20a).
    /// </summary>
    /// <param name="caster">Mwonvuli Acid-Moss's controller; performs the search.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(Player caster, Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all land permanents across every player.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: destroy target land + search Forest -> battlefield tapped",
                        () => Resolve(caster, resolved)),
                };
            });
    }

    private static void Resolve(Player caster, object resolved)
    {
        // Step 1: Destroy target land (CR 701.7b / CR 608.2b). An illegal
        // target (non-land / off-battlefield) is a no-op for this clause.
        if (resolved is Permanent target
            && target.Zone == ZoneType.Battlefield
            && target.HasType(CardType.Land))
        {
            DestroyToOwnersGraveyard(target);
        }

        // Step 2: Mandatory Forest tutor onto the battlefield tapped
        // (CR 701.19a / CR 701.20a). Resolves regardless of the destroy
        // clause's legality (CR 608.2e — independent left-to-right clauses).
        SearchForestToBattlefieldTapped(caster);
    }

    /// <summary>
    /// CR 701.19a — search the caster's library for a Forest card, put it
    /// onto the battlefield tapped, then shuffle (CR 701.20a). "Forest card"
    /// = any card carrying the <see cref="CardSubtype.Forest"/> land subtype
    /// (basic Forest or a nonbasic Forest dual). Prompts the caster's agent
    /// for the pick; no agent → deterministic first-match fallback (shape /
    /// dispatcher-test path). The library is shuffled even when no Forest is
    /// found (the search still happened).
    /// </summary>
    private static void SearchForestToBattlefieldTapped(Player caster)
    {
        var candidates = caster.Zones.Library.GetCards()
            .Where(c => c.HasSubtype(CardSubtype.Forest))
            .ToList();

        ICard? pick = null;
        if (candidates.Count > 0)
        {
            var agent = AgentRegistry.Get(caster);
            if (agent != null)
            {
                try
                {
                    pick = agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates: candidates,
                        kindLabel: "Forest card")
                        .GetAwaiter().GetResult();
                }
                catch
                {
                    pick = null;
                }
            }
            else
            {
                // Deterministic fallback: take the first Forest in iteration
                // order so the shape-only test path produces a stable result.
                pick = candidates[0];
            }
        }

        if (pick != null)
        {
            // CR 603.6a / CR 614 — route through ZoneService so ETB triggers /
            // enters-tapped replacements on the tutored land fire. Falls back to
            // raw mutation when no live service is wired (shape / test path).
            var zones = ZoneServiceRegistry.Get(caster);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                caster.Zones.Library.RemoveCard(pick);
                caster.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                if (pick is Permanent permRaw) permRaw.Tap();
            }
        }

        // CR 701.20a — shuffle after the search, even when no Forest was found.
        LibraryShuffle.ShuffleLibrary(caster, "mwonvuli-acid-moss/forest-tutor");
    }

    /// <summary>CR 701.7b — move the destroyed land to its owner's graveyard.</summary>
    private static void DestroyToOwnersGraveyard(Permanent target)
    {
        var owner = target.Owner;
        if (owner == null) return;

        var holder = target.Controller ?? owner;
        holder.Zones.Battlefield.RemoveCard(target);
        owner.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard);
    }
}
