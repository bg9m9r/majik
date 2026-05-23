namespace Majik.Core.Cards.Types;

/// <summary>
/// CR 305.6 — fixed mapping from each basic-land subtype to the single
/// mana symbol its intrinsic mana ability produces. Used by both
/// <see cref="Majik.Core.CardData.OracleManaBinder"/> (printed binding)
/// and <see cref="Majik.Core.Effects.EffectiveManaAbilities"/> (layer-
/// derived override after Blood-Moon-style retyping).
/// </summary>
public static class BasicLandManaColors
{
    /// <summary>
    /// CR 305.6 — Mountain→R, Forest→G, Plains→W, Island→U, Swamp→B,
    /// Wastes→C. Symbols are <see cref="Majik.Core.ValueObjects.ManaCost"/>
    /// glyphs without braces; pass to <c>ManaCost.Parse</c> verbatim.
    /// </summary>
    public static IReadOnlyDictionary<CardSubtype, string> Map { get; } =
        new Dictionary<CardSubtype, string>
        {
            [CardSubtype.Mountain] = "R",
            [CardSubtype.Forest] = "G",
            [CardSubtype.Plains] = "W",
            [CardSubtype.Island] = "U",
            [CardSubtype.Swamp] = "B",
            [CardSubtype.Wastes] = "C",
        };

    /// <summary>True iff <paramref name="subtype"/> is one of the six
    /// basic-land subtypes recognized by CR 305.6.</summary>
    public static bool IsBasicLandSubtype(CardSubtype subtype) => Map.ContainsKey(subtype);
}
