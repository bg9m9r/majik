using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Misty Rainforest (Zendikar / Modern Horizons / reprints).
///
/// Land. Oracle text:
///   "{T}, Pay 1 life, Sacrifice Misty Rainforest: Search your library for
///    a Forest or Island card and put it onto the battlefield. Then shuffle
///    your library."
///
/// ## Implemented (v1)
/// - Land identity (no basic supertype, no subtypes).
/// - <b>No mana ability</b>: fetchlands produce no mana by tapping.
/// - Activated ability: {T}, Pay 1 life, Sacrifice self → search for
///   Forest or Island, put onto the battlefield untapped.
/// - Self-sacrifice and 1-life payment are inline in the effect closure
///   (same pattern as <see cref="WastelandFactory"/> and
///   <see cref="ScaldingTarnFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - Library shuffle (CR 701.19c): no IZone.Shuffle entry point yet.
/// </summary>
public static class MistyRainforestFactory
{
    public const string CardName = "Misty Rainforest";

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: search library for Forest or Island, put onto battlefield",
            () =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;
                controller.LoseLife(1);

                SacrificeToOwnersGraveyard(land);

                TutorLandToBattlefield(
                    controller,
                    c => c.HasType(CardType.Land)
                         && (c.HasSubtype(CardSubtype.Forest)
                             || c.HasSubtype(CardSubtype.Island)));
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);

        return land;
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
    }
}
