using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunbaked Canyon (Modern Horizons — Horizon Canopy cycle).
///
/// R/W painless dual. Oracle text:
///   "{T}, Pay 1 life: Add {R} or {W}.
///    {1}, {T}, Sacrifice this land: Draw a card."
///
/// Same shape as <see cref="FieryIsletFactory"/>; only colour differs.
/// Wired via <see cref="Majik.Core.CardData.HorizonLandBinder"/> which
/// centralises the cycle's two ability shapes.
///
/// ## Implemented (v1)
/// - Two pay-1-life mana abilities ({R} and {W}). Life-cost gate enforces
///   CR 119.4 (life total &gt; 1 to activate).
/// - {1}, {T}, Sacrifice this land: Draw a card.
///
/// ## Deferred (v1 gaps)
/// - Sacrifice cost doesn't yet move the land to the graveyard
///   (see <see cref="Majik.Core.CardData.HorizonLandBinder.AttachSacDraw"/>).
/// </summary>
[CardName("Sunbaked Canyon")]
public static class SunbakedCanyonFactory
{
    /// <summary>
    /// Construct Sunbaked Canyon owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Sunbaked Canyon");
        land.SetOwner(owner);
        land.SetController(owner);

        HorizonLandBinder.AttachPayLifeMana(land, owner, "R");
        HorizonLandBinder.AttachPayLifeMana(land, owner, "W");
        HorizonLandBinder.AttachSacDraw(land, owner);

        return land;
    }
}
