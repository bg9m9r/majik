using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Concentrate (Mirrodin, {2}{U}{U}).
///
/// Sorcery. Oracle text:
///   "Draw three cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}{U}.
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draws
///   three cards from the top of the library (CR 121.1). Empty library
///   mid-draw flags the player for the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.  Mirrors the
///   draw-loop shape used by <see cref="ThoughtcastFactory.BuildResolveEffect"/>
///   with the count raised to three.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: draws go through direct top-of-library
///   + zone moves (same posture as Thoughtcast / Wrenn's Resolve), not through
///   a centralised "Player.DrawCard" pipeline. Draw-replacement effects
///   (e.g. Dredge, Maralen of the Mornsong) won't intercept Concentrate's
///   draws until a unified draw API lands — engine-wide gap, not
///   card-specific.
/// </summary>
[CardName("Concentrate")]
public static class ConcentrateFactory
{
    public const string CardName = "Concentrate";
    public const string PrintedManaCost = "{2}{U}{U}";

    /// <summary>
    /// Build a Concentrate sorcery owned by <paramref name="owner"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>
    /// Build Concentrate's resolve effect — draw three cards top-of-library.
    /// Mirrors <see cref="ThoughtcastFactory.BuildResolveEffect"/>'s draw
    /// loop with count raised to three (CR 121.1).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Concentrate: draw three cards.", () =>
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
