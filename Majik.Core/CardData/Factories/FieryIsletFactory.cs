using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fiery Islet (Modern Horizons — Horizon Canopy cycle).
///
/// U/R painless dual. Oracle text:
///   "{T}, Pay 1 life: Add {U} or {R}.
///    {1}, {T}, Sacrifice this land: Draw a card."
///
/// ## Implemented (v1)
/// - <b>{T}, Pay 1 life: Add {U}</b> — wired as a <see cref="ManaAbility"/>
///   with a life-cost callback (see <see cref="ManaAbility(object, Player, ManaCost, Func{bool}, Action{Player})"/>).
///   The activation check requires <c>controller.LifeTotal &gt; 1</c>
///   (CR 119.4 — you can't pay life you don't have).
/// - <b>{T}, Pay 1 life: Add {R}</b> — second ManaAbility, same shape,
///   different colour. Player / bot picks whichever colour is needed
///   when paying mana costs (the source-picker already scans abilities
///   by produced colour).
/// - <b>{1}, {T}, Sacrifice this land: Draw a card</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>(1)
///   + <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>
///   + <see cref="Effect"/> closure that moves the top library card to
///   hand. Mirrors the Vexing Bauble shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Actual sacrifice on activation</b>: <see cref="AdditionalCost.Sacrifice"/>
///   currently records the intent but doesn't route the land to the
///   graveyard (see <c>AdditionalCost.Pay</c> — sacrifice case is a TODO
///   pending the zone-service wiring). When that lands, the sac-draw
///   ability will correctly remove the land before the draw resolves.
/// - <b>Pay-life as a "you may" prompt</b>: in MTG the player chooses
///   whether to activate; the engine doesn't auto-decide here. The
///   activation gate (life total &gt; 1) only enforces legality, not
///   willingness. Bot's source-picker treats this like any other mana
///   ability — when it picks this source to pay a cost, the life loss
///   happens silently.
/// </summary>
public static class FieryIsletFactory
{
    /// <summary>
    /// Construct Fiery Islet owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Fiery Islet");
        land.SetOwner(owner);
        land.SetController(owner);

        HorizonLandBinder.AttachPayLifeMana(land, owner, "U");
        HorizonLandBinder.AttachPayLifeMana(land, owner, "R");
        HorizonLandBinder.AttachSacDraw(land, owner);

        return land;
    }
}
