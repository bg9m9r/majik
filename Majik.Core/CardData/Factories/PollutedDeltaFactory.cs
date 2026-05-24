using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Polluted Delta (Onslaught / reprints).
///
/// Land. Oracle text:
///   "{T}, Pay 1 life, Sacrifice Polluted Delta: Search your library for an
///    Island or Swamp card, put it onto the battlefield, then shuffle."
///
/// ## Implementation (v1)
/// - Land identity (no basic land type, no mana ability — fetchlands produce
///   no mana on their own; CR 305.7).
/// - Activated ability: cost is {T} + pay 1 life + sacrifice this land; the
///   effect searches the controller's library for the first card with subtype
///   <see cref="CardSubtype.Island"/> or <see cref="CardSubtype.Swamp"/> and
///   moves it directly to the battlefield untapped (CR 701.19a + CR 305.6).
/// - Sacrifice-self is performed inside the effect closure because
///   <see cref="AdditionalCost.Sacrifice"/>'s <c>Pay()</c> is a no-op stub —
///   same technique used by <see cref="WastelandFactory"/> and
///   <see cref="LotusPetalFactory"/>. The <see cref="AdditionalCost.Sacrifice"/>
///   is still declared on the ability so the engine sees the intent and can
///   gate activation via <c>CanPay</c>.
/// - <see cref="AdditionalCost.PayLife(1)"/> calls <c>Player.LoseLife(1)</c>
///   through its <c>Pay()</c> path (CR 119.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Library shuffle</b> (CR 701.19c). No <c>IZone.Shuffle</c> entry
///   point yet — same gap as every other tutor in the codebase.
/// - <b>Agent prompt</b>: the tutor picks the first qualifying land
///   deterministically. A real prompt via <c>IPlayerAgent.ChooseLibraryPickAsync</c>
///   is needed for player-driven choice (same gap as Primeval Titan / Path to Exile).
/// </summary>
[CardName("Polluted Delta")]
public static class PollutedDeltaFactory
{
    public const string CardName = "Polluted Delta";

    /// <summary>
    /// Construct Polluted Delta owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Pay 1 life, Sacrifice Polluted Delta:
        //   Search library for an Island or Swamp, put it onto the
        //   battlefield, then shuffle. (CR 602 / CR 701.19a)
        //
        // Sacrifice is performed inline in the effect closure because
        // AdditionalCost.Sacrifice.Pay() is a stub. The AdditionalCost is
        // still declared for CanPay() gate semantics.
        // ----------------------------------------------------------------
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
                    "Polluted Delta: search library for Island or Swamp, put onto battlefield",
                    () => FetchLandEffect(owner, land, CardSubtype.Island, CardSubtype.Swamp)),
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
        // Sacrifice this land first (stub cost catches up — CR 701.16).
        SacrificeToOwnersGraveyard(fetchLand);

        // Search for first matching land in library.
        var target = controller.Zones.Library
            .GetCards()
            .FirstOrDefault(c => c.HasSubtype(subtypeA) || c.HasSubtype(subtypeB));

        if (target == null) return; // No match — ability fizzles.

        controller.Zones.Library.RemoveCard(target);
        controller.Zones.Battlefield.AddCard(target);
        target.SetController(controller);
        // Land enters untapped — no Tap() call (CR 305.6 default).

        // CR 701.19c — shuffle. Deferred (no IZone.Shuffle yet).
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
