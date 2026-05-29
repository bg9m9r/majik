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
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cleansing Wildfire (Zendikar Rising, {1}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy target land. Its controller may search their library for a
///    basic land card, put it onto the battlefield tapped, then shuffle.
///    Draw a card."
///
/// ## Analogue
/// The destroy-target-land + optional basic-land compensation search shape is
/// identical to <see cref="SunderingEruptionFactory"/> (the front face of
/// Sundering Eruption // Volcanic Fissure shares the first two sentences word
/// for word). Cleansing Wildfire drops the "creatures without flying can't
/// block this turn" rider and adds an unconditional "Draw a card" for the
/// caster.
///
/// Card shape comes from the embedded JSON (<c>cleansing-wildfire.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/> needs
/// a target resolver supplied by the caller's <see cref="GameContext"/> (not
/// expressible in the data-only JSON schema).
///
/// ## Implemented (v1)
/// - Sorcery identity at {1}{R}, mono-red, mana value 2.
/// - <b>Destroy target land</b> — single 1..1 <see cref="TargetRequest"/>
///   (Intent <see cref="BotIntent.Removal"/>) whose gatherer enumerates every
///   land permanent on the battlefield across all players (no "opponent"
///   restriction). On resolution the target land moves to its owner's
///   graveyard (CR 701.7b) iff it is still a land on the battlefield
///   (CR 608.2b — illegal target → that clause is a no-op).
/// - <b>Optional basic-land compensation</b> — the controller of the destroyed
///   land MAY search their library for a basic land card, put it onto the
///   battlefield tapped, then shuffle (CR 701.19a / CR 701.20a). Offered only
///   when the destroy step succeeded. Modelled as an optional agent prompt
///   followed by a raw library tutor — same posture as
///   <see cref="SunderingEruptionFactory"/>. Declining / no agent → no search.
/// - <b>Draw a card</b> — CR 608.2e left-to-right clause ordering: the caster
///   draws one card after the destroy + compensation clauses. The draw is
///   unconditional (not gated on the target being legal), routed through
///   <see cref="Fx.DrawCards"/> so replacement effects fire.
///
/// ## Deferred (matches every tutor factory)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event.
/// </summary>
[CardName("Cleansing Wildfire")]
public static class CleansingWildfireFactory
{
    public const string CardName = "Cleansing Wildfire";
    public const string Slug = "cleansing-wildfire";

    // CR 305.6 — basic land names used by the compensation search.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Cleansing
    /// Wildfire. Single 1..1 "target land" request, no X. On resolution
    /// (CR 608.2e — left to right):
    ///   1. Destroy the target land if still legal (CR 701.7b / CR 608.2b).
    ///   2. The destroyed land's controller MAY tutor a basic land onto the
    ///      battlefield tapped, then shuffle (CR 701.19a / CR 701.20a).
    ///   3. The caster draws a card (unconditional).
    /// </summary>
    /// <param name="caster">Cleansing Wildfire's controller; draws the card.</param>
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
                        $"{CardName}: destroy target land + compensation search + draw a card",
                        () => Resolve(caster, resolved)),
                };
            });
    }

    private static void Resolve(Player caster, object resolved)
    {
        // Step 1: Destroy target land (CR 701.7b / CR 608.2b).
        Player? destroyedLandController = null;
        if (resolved is Permanent target
            && target.Zone == ZoneType.Battlefield
            && target.HasType(CardType.Land))
        {
            destroyedLandController = target.Controller ?? target.Owner;
            DestroyToOwnersGraveyard(target);
        }

        // Step 2: Optional compensation search for the destroyed land's
        // controller (only when the destroy step actually succeeded).
        if (destroyedLandController != null)
        {
            OfferCompensationSearch(destroyedLandController);
        }

        // Step 3: Draw a card (CR 608.2e — unconditional, fires regardless of
        // target legality). Routed through Fx.DrawCards so replacement effects
        // (e.g. Dredge, CR 614) get a chance.
        Fx.DrawCards(caster, 1);
    }

    /// <summary>
    /// CR 701.19a — the destroyed land's controller may search their library
    /// for a basic land card, put it onto the battlefield tapped, then shuffle.
    /// Prompt the controller's registered agent; declining or having no agent
    /// is legal (no-op). Mirrors <see cref="SunderingEruptionFactory"/>.
    /// </summary>
    private static void OfferCompensationSearch(Player landController)
    {
        var agent = AgentRegistry.Get(landController);
        if (agent == null)
        {
            // No agent registered → decline (deterministic fallback).
            return;
        }

        bool wantsToSearch;
        try
        {
            wantsToSearch = agent.ChooseYesNoAsync(
                question: "Search your library for a basic land card and put it onto the battlefield tapped?",
                intent: BotIntent.Ramp,
                ct: default)
                .GetAwaiter().GetResult();
        }
        catch
        {
            return; // Defensive: any agent failure → decline.
        }

        if (!wantsToSearch) return;

        var candidates = landController.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
            .ToList();

        if (candidates.Count == 0)
        {
            // Nothing to find — shuffle still happens (CR 701.20a).
            LibraryShuffle.ShuffleLibrary(landController, "cleansing-wildfire/compensation/empty");
            return;
        }

        ICard? pick;
        try
        {
            pick = agent.ChooseLibraryPickAsync(
                ctx: null,
                candidates: candidates,
                kindLabel: "basic land card")
                .GetAwaiter().GetResult();
        }
        catch
        {
            pick = null;
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(landController);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, landController);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                landController.Zones.Library.RemoveCard(pick);
                landController.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                if (pick is Permanent permRaw) permRaw.Tap();
            }
        }

        // CR 701.20a — shuffle after the search, even when no card was picked.
        LibraryShuffle.ShuffleLibrary(landController, "cleansing-wildfire/compensation");
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
