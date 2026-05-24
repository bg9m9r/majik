using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Modern Horizons "Horizon Canopy"
/// painless-dual cycle.
///
/// Each member shares the same oracle shape — only the two mana colours
/// differ — so one factory handles the cycle:
/// <code>
/// [CardName("Fiery Islet",     "U", "R")]
/// [CardName("Sunbaked Canyon", "R", "W")]
/// </code>
///
/// The two args are the canonical pair of single-letter colour codes the
/// land produces. The source generator forwards them at dispatch time,
/// prepending the printed card name as <c>args[0]</c>.
///
/// ## Implemented (v1)
/// - Land identity.
/// - Two <c>{T}, Pay 1 life: Add {C}</c> mana abilities — one per colour.
///   See <see cref="HorizonLandBinder.AttachPayLifeMana"/>; activation gate
///   enforces CR 119.4 (life total &gt; 1).
/// - <c>{1}, {T}, Sacrifice this land: Draw a card.</c> via
///   <see cref="HorizonLandBinder.AttachSacDraw"/>.
///
/// ## Deferred (v1 gaps)
/// - Sacrifice cost still records intent only — see
///   <see cref="HorizonLandBinder.AttachSacDraw"/>.
///
/// ## Cycle members not yet shipped
/// Horizon Canopy, Nurturing Peatland, Silent Clearing, Waterlogged Grove.
/// Adding any of them is a one-line edit — append a new <c>[CardName]</c>
/// attribute with the colour pair.
/// </summary>
[CardName("Fiery Islet",     "U", "R")]
[CardName("Sunbaked Canyon", "R", "W")]
public static class HorizonLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Fiery Islet.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Fiery Islet", "U", "R" });

    /// <summary>
    /// Construct the horizon land identified by <paramref name="args"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the land.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Sunbaked Canyon"),
    /// <c>[1] = first colour</c> (single-letter Scryfall code, e.g. "R"),
    /// <c>[2] = second colour</c> (e.g. "W").
    /// </param>
    public static Land Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"HorizonLandCycleFactory needs args = [name, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var colorA = args[1];
        var colorB = args[2];

        var land = new Land(cardName);
        land.SetOwner(owner);
        land.SetController(owner);

        HorizonLandBinder.AttachPayLifeMana(land, owner, colorA);
        HorizonLandBinder.AttachPayLifeMana(land, owner, colorB);
        HorizonLandBinder.AttachSacDraw(land, owner);

        return land;
    }
}
