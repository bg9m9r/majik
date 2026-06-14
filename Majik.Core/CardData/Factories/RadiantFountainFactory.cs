using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Radiant Fountain (a colorless gain-life land).
/// Oracle text (verified against Scryfall):
///   "When this land enters, you gain 2 life.
///    {T}: Add {C}."
///
/// <para>
/// Same oracle shape as the "Refuge" gain-life cycle
/// (<see cref="AkoumRefugeFactory"/>) minus two simplifications: Radiant
/// Fountain has no "enters tapped" clause (CR 614.1c), and it taps for a
/// single colourless mana {C} (CR 605.1, CR 107.4c) rather than two colours.
/// The ETB self-trigger gains the controller 2 life (CR 119.3) instead of 1.
/// Because there is no enters-tapped restriction, no
/// <see cref="ReplacementBus"/> wiring is needed — the single-arg
/// <see cref="Create(Player)"/> path is the whole card.
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the {C} mana ability, and the
/// "When this land enters, you gain 2 life" triggered ability — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/radiant-fountain.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>.
/// </para>
/// </summary>
[CardName("Radiant Fountain")]
public static class RadiantFountainFactory
{
    public const string Slug = "radiant-fountain";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Radiant Fountain owned and controlled by
    /// <paramref name="owner"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
