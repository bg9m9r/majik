using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the 10-member Onslaught / Zendikar /
/// Modern Horizons fetchland cycle.
///
/// Each fetchland shares the same oracle shape — only the two basic-land
/// subtypes differ — so one factory class handles all ten:
/// <code>
/// [CardName("Bloodstained Mire", "Swamp",   "Mountain")]
/// [CardName("Arid Mesa",         "Plains",  "Mountain")]
/// [CardName("Wooded Foothills",  "Mountain","Forest")]
/// [CardName("Polluted Delta",    "Island",  "Swamp")]
/// [CardName("Windswept Heath",   "Forest",  "Plains")]
/// [CardName("Scalding Tarn",     "Island",  "Mountain")]
/// [CardName("Misty Rainforest",  "Forest",  "Island")]
/// [CardName("Flooded Strand",    "Plains",  "Island")]
/// [CardName("Verdant Catacombs", "Swamp",   "Forest")]
/// [CardName("Marsh Flats",       "Plains",  "Swamp")]
/// </code>
///
/// The two args are the canonical pair of basic-land subtypes the fetchland
/// searches for (CR 701.19a). The source generator forwards them at
/// dispatch time, prepending the printed card name as <c>args[0]</c>.
///
/// ## Implemented (v1)
/// - Land identity (no basic supertype, no subtypes — fetchlands produce no
///   mana on their own; CR 305.7).
/// - Activated ability: <c>{T}, Pay 1 life, Sacrifice this land:</c>
///   search the controller's library for a land card whose subtypes include
///   either of the two basics, put it onto the battlefield untapped.
/// - Library candidates filter on <c>HasType(Land)</c> + the subtype pair so
///   the search picks up both basic lands AND dual-type nonbasics
///   (e.g. Steam Vents, Bloodstained Mire on a Misty Rainforest activation).
/// - Agent prompt: <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> picks
///   the chosen land when an agent is registered; otherwise falls back to
///   the first deterministic match. Mirrors the pre-consolidation shape of
///   the per-card Misty Rainforest / Scalding Tarn factories.
/// - <see cref="AdditionalCost.Tap"/>, <see cref="AdditionalCost.PayLife"/>,
///   and <see cref="AdditionalCost.Sacrifice"/> are all declared as proper
///   ICosts on the ability (CR 117.5 — the real-card cost is
///   <c>{T}, Pay 1 life, Sacrifice this land:</c>). CostPayment runs them
///   atomically before the ability hits the stack so the activator can't
///   ship a half-paid stub. The resolve closure does only the tutor.
///
/// ## Deferred (v1 gaps)
/// - <b>Sorcery-speed gate</b>: fetchlands have no printed timing restriction;
///   none needed.
/// </summary>
[CardName("Bloodstained Mire", "Swamp",    "Mountain")]
[CardName("Arid Mesa",         "Plains",   "Mountain")]
[CardName("Wooded Foothills",  "Mountain", "Forest")]
[CardName("Polluted Delta",    "Island",   "Swamp")]
[CardName("Windswept Heath",   "Forest",   "Plains")]
[CardName("Scalding Tarn",     "Island",   "Mountain")]
[CardName("Misty Rainforest",  "Forest",   "Island")]
[CardName("Flooded Strand",    "Plains",   "Island")]
[CardName("Verdant Catacombs", "Swamp",    "Forest")]
[CardName("Marsh Flats",       "Plains",   "Swamp")]
public static class FetchLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when someone constructs the cycle
    /// factory by hand. Default-builds Flooded Strand (Plains/Island).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Flooded Strand", "Plains", "Island" });

    /// <summary>
    /// Construct the fetchland identified by <paramref name="args"/>, owned
    /// and controlled by <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the land.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Bloodstained Mire"),
    /// <c>[1] = first basic subtype</c> (e.g. "Swamp"),
    /// <c>[2] = second basic subtype</c> (e.g. "Mountain").
    /// Subtype names must match the <see cref="CardSubtype"/> enum.
    /// </param>
    public static Land Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"FetchLandCycleFactory needs args = [name, subtypeA, subtypeB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var subtypeA = ParseSubtype(args[1]);
        var subtypeB = ParseSubtype(args[2]);

        var land = new Land(cardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 117.5 — fetchland cost: {T}, Pay 1 life, Sacrifice this land.
        // CostPayment runs all three before the ability hits the stack, so
        // by the time the resolve closure fires the fetchland is already in
        // the graveyard and 1 life has been spent. The closure only needs
        // to perform the tutor (CR 701.19a) — no longer responsible for the
        // sacrifice or the life payment.
        var fetchEffect = new Effect(
            $"{cardName}: search library for {subtypeA} or {subtypeB}, put onto battlefield",
            async ctx =>
            {
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                await TutorLandToBattlefieldAsync(
                    controller,
                    c => c.HasType(CardType.Land)
                         && (c.HasSubtype(subtypeA) || c.HasSubtype(subtypeB)),
                    ctx).ConfigureAwait(false);
            });

        var fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(land),
                AdditionalCost.PayLife(1),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);
        return land;
    }

    private static CardSubtype ParseSubtype(string raw)
    {
        if (Enum.TryParse<CardSubtype>(raw, ignoreCase: false, out var v))
        {
            return v;
        }
        throw new ArgumentException(
            $"FetchLandCycleFactory: '{raw}' is not a valid CardSubtype.",
            nameof(raw));
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for the first land matching
    /// <paramref name="predicate"/>, consult the registered agent to choose
    /// among candidates (falls back to the first deterministic match), and
    /// move the chosen card to the battlefield untapped (CR 305).
    /// </summary>
    private static async ValueTask TutorLandToBattlefieldAsync(Player player, Func<ICard, bool> predicate, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(predicate)
            .ToList();
        if (candidates.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates, "land card")
                .ConfigureAwait(false)
            : candidates[0];
        if (pick == null) return;

        // CR 603.6a / CR 614 — route the Library → Battlefield move
        // through ZoneService when a live service is registered so the
        // tutored land's CardMovedEvent fires (drives bounce-land ETB
        // bounce + Amulet of Vigor untap) and ETB-tapped replacements
        // (shock lands paying 2 life, bounce lands always tapped) run.
        // Falls back to raw zone mutation for shape / dispatcher-test
        // paths with no registered service.
        var zones = ZoneServiceRegistry.Get(player);
        if (zones != null)
        {
            zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
        }
        else
        {
            player.Zones.Library.RemoveCard(pick);
            player.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(player);
        }
        // CR 701.20a — shuffle library after search.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "fetch-land");
    }
}
