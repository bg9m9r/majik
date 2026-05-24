using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scalding Tarn (Zendikar / Modern Horizons / reprints).
///
/// Land. Oracle text:
///   "{T}, Pay 1 life, Sacrifice Scalding Tarn: Search your library for
///    an Island or Mountain card and put it onto the battlefield. Then
///    shuffle your library."
///
/// ## Implemented (v1)
/// - Land identity (no basic supertype, no subtypes).
/// - <b>No mana ability</b>: fetchlands produce no mana by tapping.
/// - <b>{T}, Pay 1 life, Sacrifice this land: fetch Island or Mountain</b>
///   wired as an <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> as the declared cost. The self-
///   sacrifice and 1-life payment are performed inside the effect closure
///   (same trick as <see cref="WastelandFactory"/> and
///   <see cref="LotusPetalFactory"/>) because
///   <see cref="AdditionalCost.Sacrifice"/>'s Pay() is still a no-op stub.
/// - Searches the controller's library for the first card that is a Land
///   with the <see cref="CardSubtype.Island"/> or
///   <see cref="CardSubtype.Mountain"/> subtype (covers basic lands AND
///   dual-type nonbasics like Steam Vents). Consults the registered
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; falls back to the
///   first match deterministically when no agent is registered.
/// - Chosen land enters the battlefield <b>untapped</b> (CR 305 — original
///   oracle text does not say "tapped"; distinct from Path to Exile's tutor
///   rider).
///
/// ## Deferred (v1 gaps)
/// - <b>Library shuffle</b> (CR 701.19c): no <c>IZone.Shuffle</c> entry
///   point yet — same gap as every other tutor in this codebase.
/// - <b>"May search" semantics</b>: the printed ability does not say "may",
///   but if the library contains no matching land the search legally finds
///   nothing. The v1 path no-ops cleanly in that case.
/// - <b>Sorcery-speed gate</b>: fetchlands activate at instant speed per
///   oracle (no printed timing restriction) — no gate needed.
/// </summary>
public static class ScaldingTarnFactory
{
    public const string CardName = "Scalding Tarn";

    /// <summary>
    /// Construct Scalding Tarn owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Pay 1 life, Sacrifice this land:
        //   Search your library for an Island or Mountain card, put it
        //   onto the battlefield (untapped), then shuffle your library.
        //
        // CR 602 — activated ability. Cost declared as {T} (AdditionalCost.Tap).
        // Self-sacrifice + 1-life payment are handled inside the resolve
        // closure because AdditionalCost.Sacrifice.Pay() is a no-op stub.
        // ----------------------------------------------------------------
        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: search library for Island or Mountain, put onto battlefield",
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

                // Search library for Island or Mountain land card.
                TutorLandToBattlefield(
                    controller,
                    c => c.HasType(CardType.Land)
                         && (c.HasSubtype(CardSubtype.Island)
                             || c.HasSubtype(CardSubtype.Mountain)));
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);

        return land;
    }

    // ------------------------------------------------------------------
    // Shared helpers (inline — fetchlands are simple enough not to need
    // a shared base class; each factory is self-contained for clarity).
    // ------------------------------------------------------------------

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
    /// Search <paramref name="player"/>'s library for the first land
    /// matching <paramref name="predicate"/>, consult the registered agent
    /// to choose among candidates (falls back to first match), and move the
    /// chosen card to the battlefield untapped (CR 305).
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
        // CR 701.19c — shuffle library after search. Deferred (no IZone.Shuffle yet).
    }
}
