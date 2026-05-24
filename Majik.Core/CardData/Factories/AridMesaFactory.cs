using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arid Mesa (Zendikar / reprints).
///
/// Land. Oracle text:
///   "{T}, Pay 1 life, Sacrifice Arid Mesa: Search your library for a
///    Plains or Mountain card, put it onto the battlefield, then shuffle."
///
/// ## Implementation (v1)
/// Same shape as <see cref="PollutedDeltaFactory"/>. Fetches
/// <see cref="CardSubtype.Plains"/> or <see cref="CardSubtype.Mountain"/>.
///
/// ## Deferred (v1 gaps)
/// Library shuffle (CR 701.19c) and agent-driven pick deferred — same as
/// Polluted Delta and all other tutors in the codebase.
/// </summary>
public static class AridMesaFactory
{
    public const string CardName = "Arid Mesa";

    /// <summary>
    /// Construct Arid Mesa owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        var fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(land),
                AdditionalCost.PayLife(1),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[]
            {
                new Effect(
                    "Arid Mesa: search library for Plains or Mountain, put onto battlefield",
                    () => FetchLandEffect(owner, land, CardSubtype.Plains, CardSubtype.Mountain)),
            });

        land.AddAbility(fetchAbility);
        return land;
    }

    private static void FetchLandEffect(
        Player controller,
        Land fetchLand,
        CardSubtype subtypeA,
        CardSubtype subtypeB)
    {
        SacrificeToOwnersGraveyard(fetchLand);

        var target = controller.Zones.Library
            .GetCards()
            .FirstOrDefault(c => c.HasSubtype(subtypeA) || c.HasSubtype(subtypeB));

        if (target == null) return;

        controller.Zones.Library.RemoveCard(target);
        controller.Zones.Battlefield.AddCard(target);
        target.SetController(controller);
        // CR 701.19c — shuffle deferred.
    }

    private static void SacrificeToOwnersGraveyard(Land self)
    {
        if (self.Zone != ZoneType.Battlefield) return;

        var controller = self.Controller ?? self.Owner;
        var owner = self.Owner;
        if (controller == null || owner == null) return;

        controller.Zones.Battlefield.RemoveCard(self);
        owner.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }
}
