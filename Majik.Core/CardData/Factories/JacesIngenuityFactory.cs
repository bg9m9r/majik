using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jace's Ingenuity (Magic 2011, {3}{U}{U}).
///
/// Instant. Oracle text:
///   "Draw three cards."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{U}{U}.
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draws
///   three cards from the top of the library (CR 121.1). Empty library
///   mid-draw flags the player for the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.  Mirrors the
///   draw-loop shape used by <see cref="ConcentrateFactory.BuildResolveEffect"/>
///   with card type changed to Instant and cost raised to {3}{U}{U}.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: draws go through direct top-of-library
///   + zone moves (same posture as Concentrate / Thoughtcast), not through
///   a centralised "Player.DrawCard" pipeline. Draw-replacement effects
///   (e.g. Dredge, Maralen of the Mornsong) won't intercept these draws
///   until a unified draw API lands — engine-wide gap, not card-specific.
/// </summary>
[CardName("Jace's Ingenuity")]
public static class JacesIngenuityFactory
{
    public const string CardName = "Jace's Ingenuity";
    public const string PrintedManaCost = "{3}{U}{U}";

    /// <summary>
    /// Build a Jace's Ingenuity instant owned by <paramref name="owner"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>
    /// Build Jace's Ingenuity's resolve effect — draw three cards top-of-library.
    /// Mirrors <see cref="ConcentrateFactory.BuildResolveEffect"/>'s draw
    /// loop (CR 121.1).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Jace's Ingenuity: draw three cards.", () =>
            {
                // CR 121.1 — three simple top-of-library draws. Empty
                // library mid-draw flags the SBA loss (CR 704.5b) and
                // short-circuits the remaining draws.
                for (var i = 0; i < 3; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            }),
        };
    }
}
