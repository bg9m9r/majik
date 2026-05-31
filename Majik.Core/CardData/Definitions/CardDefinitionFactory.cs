using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Builds a runtime <see cref="ICard"/> from a <see cref="CardDefinition"/>.
///
/// <para>
/// PLAN 03 S2 — this type is now a thin entry <b>shim</b>: it deserializes
/// the JSON <see cref="CardDefinition"/> into the canonical fluent
/// <see cref="CardDef"/> (<see cref="CardDefinition.ToCardDef"/>) and hands it
/// to the single materializer (<see cref="CardDefRuntime.Build"/>). The 175
/// JSON wrapper factories' <c>CardDefinitionFactory.Build(Definition, owner)</c>
/// call sites are unchanged, and the runtime cards produced are byte-identical
/// to the pre-reroute direct build (the cost / effect / trigger / mana-ability
/// construction moved verbatim into <see cref="CardDefRuntime"/>, which both
/// declarative systems now share).
/// </para>
/// </summary>
public static class CardDefinitionFactory
{
    /// <summary>
    /// Materialize a card for the supplied owner. The first listed
    /// <see cref="CardDefinition.Types"/> dictates the runtime C# class
    /// (Land / Creature / Instant / …); additional types are added so
    /// multi-type cards (Artifact Creature, …) work correctly.
    /// </summary>
    public static ICard Build(CardDefinition definition, Player owner) =>
        Build(definition, owner, replacements: null);

    /// <summary>
    /// Materialize a card for the supplied owner, optionally routing
    /// JSON-driven +1/+1 counter placements through the supplied
    /// <see cref="ReplacementBus"/> (CR 614). When <paramref name="replacements"/>
    /// is null, counter placements fall through to a direct add — same
    /// behaviour as today's untouched callers.
    /// </summary>
    public static ICard Build(CardDefinition definition, Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(owner);
        return CardDefRuntime.Build(definition.ToCardDef(), owner, replacements);
    }
}
