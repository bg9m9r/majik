namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Engine-coverage tier for a single Scryfall card. Higher tier = higher
/// fidelity of the engine's behavior for that card.
///
/// Used by <see cref="CoverageClassifier"/> / <see cref="CoverageReportV2"/>
/// and the <c>coverage</c> console subcommand. Order of the enum members
/// is the classification priority — a card is assigned the FIRST matching
/// tier when checked top-to-bottom.
/// </summary>
public enum CoverageTier
{
    /// <summary>
    /// Name appears in the <c>NamedCardFactory</c> source-gen dispatch
    /// (i.e. has a <c>[CardName(...)]</c> attribute on some factory class).
    /// Highest fidelity — engine treats this card as fully-bespoke.
    /// </summary>
    NamedFactory = 0,

    /// <summary>
    /// Instant or sorcery for which
    /// <see cref="ScryfallCardFactory.LookupSpellDefinition"/> returns
    /// non-null — i.e. a compiled or live oracle-template covers the
    /// resolve-time effect.
    /// </summary>
    SpellBound = 1,

    /// <summary>
    /// Permanent (creature/artifact/enchantment/land/planeswalker) where
    /// <see cref="ScryfallCardFactory.Create"/> produces a card with at
    /// least one ability AND the oracle text contains only keyword
    /// markers + reminder text (i.e. the engine's keyword pipeline fully
    /// covers the printed rules text).
    /// </summary>
    KeywordOnly = 2,

    /// <summary>
    /// Vanilla creature — no oracle text at all. Engine plays it as a
    /// plain N/N beater with no abilities.
    /// </summary>
    Vanilla = 3,

    /// <summary>
    /// Card has printed oracle text but no <c>[CardName]</c> factory, no
    /// spell-template match, and no keyword markers covering the text.
    /// Engine produces a card shell but the rules text is not enforced.
    /// </summary>
    Unimplemented = 4,
}
