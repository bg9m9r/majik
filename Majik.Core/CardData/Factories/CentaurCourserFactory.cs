using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Centaur Courser (Magic 2010, {2}{G}).
///
/// Creature — Centaur Warrior 3/3. Oracle text (verified against Scryfall):
/// empty — Centaur Courser is a plain vanilla creature with no printed
/// keywords, triggers, statics, or activated abilities.
///
/// The full card — name, <see cref="Creature"/> type, Centaur + Warrior
/// subtypes (CR 205.3m), {2}{G}, 3/3 — is materialised entirely from the
/// embedded JSON definition (<c>centaur-courser.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The empty <c>abilities</c>
/// array makes it vanilla, so no hand-rolled C# behaviour is needed — same
/// posture as the JSON-driven <see cref="AlphaMyrFactory"/>.
/// </summary>
[CardName("Centaur Courser")]
public static class CentaurCourserFactory
{
    public const string CardName = "Centaur Courser";
    public const string Slug = "centaur-courser";

    /// <summary>
    /// Materialise Centaur Courser for <paramref name="owner"/> from the
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
