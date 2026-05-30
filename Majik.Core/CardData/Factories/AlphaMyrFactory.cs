using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alpha Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 2/1. Oracle text (verified against Scryfall):
/// empty — Alpha Myr is a plain vanilla artifact creature. Unlike the
/// rest of the Mirrodin Myr cycle (Silver Myr, Gold Myr, …) it carries no
/// "{T}: Add &lt;mana&gt;" ability; it's simply a beater.
///
/// The full card — name, dual <b>Artifact + Creature</b> types
/// (CR 301.1 / 302.1), Myr subtype, {2}, 2/1 — is materialised entirely
/// from the embedded JSON definition (<c>alpha-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The empty
/// <c>abilities</c> array makes it vanilla, so no hand-rolled C# behaviour
/// is needed — same posture as the JSON-driven <see cref="SilverMyrFactory"/>
/// but with the mana ability dropped and 1/1 → 2/1.
/// </summary>
[CardName("Alpha Myr")]
public static class AlphaMyrFactory
{
    public const string CardName = "Alpha Myr";
    public const string Slug = "alpha-myr";

    /// <summary>
    /// Materialise Alpha Myr for <paramref name="owner"/> from the embedded
    /// JSON definition. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
