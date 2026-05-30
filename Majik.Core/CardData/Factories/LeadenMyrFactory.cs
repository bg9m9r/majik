using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leaden Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {B}."
///
/// The full card — name, dual <b>Artifact + Creature</b> types
/// (CR 301.1 / 302.1), Myr subtype, {2}, 1/1, and the
/// <b>{T}: Add {B}</b> mana ability (CR 605.1) — is materialised entirely
/// from the embedded JSON definition (<c>leaden-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <see cref="ManaAbilityDefinition"/> schema already expresses the
/// vanilla "{T}: Add &lt;produces&gt;" shape, so this card needs no
/// hand-rolled C# behaviour — same posture as the other JSON-driven
/// mana-myr factories.
///
/// This is the black member of the Mirrodin mono-colour Myr cycle: the
/// only delta from <see cref="SilverMyrFactory"/> (the {T}: Add {U}
/// sibling) is {U} -> {B}, which collapses cleanly into the JSON
/// definition.
/// </summary>
[CardName("Leaden Myr")]
public static class LeadenMyrFactory
{
    public const string CardName = "Leaden Myr";
    public const string Slug = "leaden-myr";

    /// <summary>
    /// Materialise Leaden Myr for <paramref name="owner"/> from the
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
