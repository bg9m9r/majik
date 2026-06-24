using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Loot, Exuberant Explorer (Bloomburrow, {2}{G}).
///
/// Legendary Creature — Beast Noble 1/4. Oracle text (verified against
/// Scryfall 2026-06-24):
///   "You may play an additional land on each of your turns.
///    {4}{G}{G}, {T}: Look at the top six cards of your library. You may
///    reveal a creature card with mana value less than or equal to the
///    number of lands you control from among them and put it onto the
///    battlefield. Put the rest on the bottom in a random order."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 1/4, Legendary Creature — Beast Noble) is
/// loaded from <c>Majik.Core/CardData/Cards/loot-exuberant-explorer.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/>. The static land-play
/// permission and the activated dig ability are attached in code below —
/// neither shape is expressible by the declarative JSON ability schema, so
/// they are hand-rolled here, same posture as
/// <see cref="FaunaShamanFactory"/>.
///
/// ## Implemented (v1)
/// - 1/4 Legendary Beast Noble (CR 205.3m) at {2}{G}.
/// - <b>Static land-play permission (CR 305.2 / 720)</b>:
///   "You may play an additional land on each of your turns." Modeled as
///   <see cref="Permanent.AdditionalLandPlaysGranted"/> = 1, summed live by
///   <see cref="Majik.Core.Game.LandDropTracker"/> — same battlefield-gated,
///   controller-scoped grant as <see cref="AzusaLostButSeekingFactory"/>
///   (Azusa grants +2; Loot grants +1). The bonus appears the instant Loot
///   enters and vanishes the instant she leaves; the per-turn cap resets
///   independently (CR 505.5b).
/// - <b>Activated dig ability (CR 602.1)</b>:
///   <c>{4}{G}{G}, {T}: Look at the top six … put it onto the battlefield …</c>
///   The two activation costs are existing primitives:
///   <see cref="ManaCostCost"/> ("{4}{G}{G}") and
///   <see cref="AdditionalCost.Tap"/> on the source (CR 107.4 / 602.1 — the
///   {T} symbol). Resolution (<see cref="ResolveAsync"/>) peeks the top six
///   (CR 701.21 — short library never throws), offers the controller a SINGLE
///   optional pick — a creature card whose mana value (CR 202.3) is ≤ the
///   number of lands the controller controls at resolution — puts the pick
///   onto the battlefield via <see cref="ZoneService.MoveCard"/> (so ETB
///   triggers / replacement effects fire — CR 603.6a), and bottoms the rest
///   in random order (CR 701.20a). Same single-pick / may-decline / bottom-
///   random shape as <see cref="CollectedCompanyFactory"/>, with the mana-
///   value cap computed live instead of being a printed constant.
///
/// ## "Number of lands you control" cap
/// CR 202.3b — a card's mana value in any zone other than the stack reads X
/// in its cost as 0, so the cap compares printed mana values against the live
/// land count of the ABILITY'S controller at resolution (CR 608.2 — resolve
/// against current game state). The count is taken at resolution time, not
/// activation, so a land that enters between activation and resolution
/// (rare, but legal) raises the cap.
///
/// ## Deferred (v1 gaps)
/// - <b>"Reveal" signal</b>: the picked creature moves Library → Battlefield
///   without publishing a per-card reveal event — same gap as the rest of the
///   look-at-top-N family (<see cref="CollectedCompanyFactory"/>,
///   <see cref="MuxusGoblinGrandeeFactory"/>). The card still reaches the
///   battlefield, so observable game state is correct; only the public
///   "reveal" UI signal is absent.
/// </summary>
[CardName("Loot, Exuberant Explorer")]
public static class LootExuberantExplorerFactory
{
    public const string CardName = "Loot, Exuberant Explorer";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("loot-exuberant-explorer");

    /// <summary>CR 720 — Loot grants one additional land play each turn.</summary>
    public const int AdditionalLandPlays = 1;

    /// <summary>Activation mana cost — the {4}{G}{G} in "{4}{G}{G}, {T}: …".</summary>
    public const string ActivationManaCost = "{4}{G}{G}";

    /// <summary>Number of cards looked at off the top of the library.</summary>
    public const int PeekCount = 6;

    /// <summary>
    /// Construct Loot, Exuberant Explorer. The static land-play grant is
    /// stamped on the card and the activated dig ability is fully attached
    /// and exercisable. Battlefield placement routes through
    /// <see cref="ZoneServiceRegistry"/> (when a service is registered for
    /// the controller) or raw zone moves.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 305.2 / 720 — "You may play an additional land on each of your
        // turns." Battlefield-gated, controller-scoped, summed live by
        // LandDropTracker.AdditionalLandPlaysFromBattlefield.
        card.AdditionalLandPlaysGranted = AdditionalLandPlays;

        // ----------------------------------------------------------------
        // "{4}{G}{G}, {T}: Look at the top six cards of your library. You may
        //  reveal a creature card with mana value less than or equal to the
        //  number of lands you control from among them and put it onto the
        //  battlefield. Put the rest on the bottom in a random order."
        // CR 602.1 — activated ability. Two costs:
        //   {4}{G}{G} -> ManaCostCost
        //   {T}       -> AdditionalCost.Tap (CR 107.4 / 602.1)
        // ----------------------------------------------------------------
        var digEffect = new Effect(
            $"{CardName}: look at top {PeekCount}, you may put a creature card with " +
            "mv ≤ lands you control onto the battlefield, rest to bottom in random order.",
            ctx =>
            {
                var controller = (ctx.Source as Permanent)?.Controller
                    ?? card.Controller ?? owner;
                return ResolveAsync(controller, ctx);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { digEffect });

        card.AddAbility(activated);

        return card;
    }

    /// <summary>
    /// Execute Loot's dig resolution against <paramref name="caster"/>'s
    /// library. Public so tests and bots can drive the resolution without
    /// going through the activation pipeline. Peeks the top
    /// <see cref="PeekCount"/> cards (CR 701.21 — short library is fine),
    /// offers ONE optional pick (a creature card with mana value ≤ the
    /// number of lands <paramref name="caster"/> controls), moves the pick
    /// Library → Battlefield, then bottoms the remainder in random order
    /// (CR 701.20a).
    /// </summary>
    /// <param name="caster">Ability controller — the player whose library is
    /// looked at, whose lands set the mana-value cap, and onto whose
    /// battlefield the picked creature lands.</param>
    /// <param name="ctx">Resolution context (supplies the agent + game).</param>
    /// <param name="zoneService">Optional. When supplied, the picked
    /// creature's Library → Battlefield move routes through this service so
    /// ETB triggers (CR 603.6a) fire. When null,
    /// <see cref="ZoneServiceRegistry.Get"/> is consulted, falling back to
    /// raw zone manipulation.</param>
    /// <param name="agent">Optional explicit agent that owns the "you may"
    /// pick decision. When null, falls back to <see cref="ResolutionContext.Agent"/>
    /// then <see cref="AgentRegistry.Get"/>; when no agent is registered
    /// either, picks the first eligible candidate (deterministic pre-agent
    /// posture).</param>
    public static async ValueTask ResolveAsync(
        Player caster,
        ResolutionContext ctx,
        ZoneService? zoneService = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var library = caster.Zones.Library;

        // 1. Look at up to PeekCount cards (CR 701.21 — short library is fine).
        var peeked = library.GetCards().Take(PeekCount).ToList();
        if (peeked.Count == 0) return;

        // 2. Cap = number of lands the controller controls at resolution
        //    (CR 608.2 — resolve against current game state).
        int landCount = caster.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Land));

        // Eligible: creature cards with mana value ≤ landCount (CR 202.3 —
        // mana value reads off the printed cost; X is 0 outside the stack).
        bool IsEligible(ICard c) =>
            c.HasType(CardType.Creature) &&
            ManaCost.Parse(c.ManaCost ?? string.Empty).TotalValue <= landCount;

        var candidates = peeked.Where(IsEligible).ToList();

        // 3. "You may reveal a creature card …" — a single optional pick
        //    (CR 117.x — the agent may decline by returning null).
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            agent ??= ctx.Agent ?? AgentRegistry.Get(caster);
            if (agent != null)
            {
                pick = await agent.ChooseLibraryPickAsync(
                        ctx.Game,
                        candidates,
                        $"creature card with mana value {landCount} or less to put onto the battlefield")
                    .ConfigureAwait(false);
                // Defensive: agent must pick from the offered candidates.
                if (pick != null && !candidates.Contains(pick)) pick = null;
            }
            else
            {
                pick = candidates[0];
            }
        }

        // 4. Put the pick onto the battlefield (Library → Battlefield).
        if (pick != null)
        {
            var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(caster);
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(
                    pick, ZoneType.Library, ZoneType.Battlefield, caster);
            }
            else
            {
                library.RemoveCard(pick);
                caster.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(caster);
                if (pick is Permanent perm) perm.MarkEnteredBattlefield();
            }
        }

        // 5. Bottom the rest in random order (CR 701.20a). Per-game RNG;
        //    tests seed it. Remove-then-add each remainder card so the new
        //    bottom order is the shuffled order.
        var remainder = peeked.Where(c => c != pick).ToList();
        if (remainder.Count > 0)
        {
            var rng = GameRandomRegistry.Get(caster);
            rng.Shuffle(remainder);

            foreach (var c in remainder)
            {
                library.RemoveCard(c);
            }
            foreach (var c in remainder)
            {
                library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }
    }
}
