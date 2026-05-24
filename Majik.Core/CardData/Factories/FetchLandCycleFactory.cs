using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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
/// - Self-sacrifice + 1-life payment are inline in the resolve closure
///   (same trick as <see cref="WastelandFactory"/>) because
///   <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub.
/// - <see cref="AdditionalCost.Tap"/> is the declared cost so the ability's
///   <c>CanPay</c> gate still reads correctly.
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

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{cardName}: search library for {subtypeA} or {subtypeB}, put onto battlefield",
            () =>
            {
                if (fetchAbility == null) return;

                // Pay 1 life (CR 119.4).
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;
                controller.LoseLife(1);

                // Self-sacrifice — move this land from battlefield to
                // owner's graveyard (CR 701.16). Must happen before the
                // library search so the land is no longer in the library.
                SacrificeToOwnersGraveyard(land);

                TutorLandToBattlefield(
                    controller,
                    c => c.HasType(CardType.Land)
                         && (c.HasSubtype(subtypeA) || c.HasSubtype(subtypeB)));
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
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

    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for the first land matching
    /// <paramref name="predicate"/>, consult the registered agent to choose
    /// among candidates (falls back to the first deterministic match), and
    /// move the chosen card to the battlefield untapped (CR 305).
    /// </summary>
    private static void TutorLandToBattlefield(Player player, Func<ICard, bool> predicate)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(predicate)
            .ToList();
        if (candidates.Count == 0) return;

        var agent = AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "land card")
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null) return;

        player.Zones.Library.RemoveCard(pick);
        player.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.SetController(player);
        // CR 701.20a — shuffle library after search.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "fetch-land");
    }
}
