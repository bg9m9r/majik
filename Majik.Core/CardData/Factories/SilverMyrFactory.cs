using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Silver Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {U}."
///
/// The full card — name, dual <b>Artifact + Creature</b> types
/// (CR 301.1 / 302.1), Myr subtype, {2}, 1/1, and the
/// <b>{T}: Add {U}</b> mana ability (CR 605.1) — is materialised entirely
/// from the embedded JSON definition (<c>silver-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <see cref="ManaAbilityDefinition"/> schema already expresses the
/// vanilla "{T}: Add &lt;produces&gt;" shape, so this card needs no
/// hand-rolled C# behaviour — same posture as the JSON-driven
/// mana-rock / mana-creature factories.
///
/// Unlike <see cref="PlagueMyrFactory"/> (the {T}: Add {C} analogue),
/// Silver Myr is a plain Myr — no Phyrexian subtype, no Infect — so the
/// only delta from that analogue is {C} -> {U} and dropping the Infect
/// keyword marker. Both of those collapse cleanly into the JSON
/// definition.
/// </summary>
[CardName("Silver Myr")]
public static class SilverMyrFactory
{
    public const string CardName = "Silver Myr";
    public const string Slug = "silver-myr";

    /// <summary>
    /// Materialise Silver Myr for <paramref name="owner"/> from the
    /// embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
