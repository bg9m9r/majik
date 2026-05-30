using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gold Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {W}."
///
/// The mana-fixing twin of Palladium Myr on the original Mirrodin Myr
/// cycle: each cycle member is an Artifact Creature — Myr with a
/// "{T}: Add &lt;one colour&gt;" mana ability. Gold Myr taps for a single
/// white pip.
///
/// The card's entire printed shape AND its mana ability are materialised
/// from the embedded JSON definition (<c>gold-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>:
/// <list type="bullet">
///   <item>Base shape — <b>Artifact Creature — Myr 1/1 at {2}</b>. The JSON
///   <c>types</c> array carries both Creature and Artifact, so
///   <see cref="Card.HasType"/> surfaces the artifact type for affinity /
///   artifact-matters consumers (CR 301.1 / 302.1).</item>
///   <item><b>{T}: Add {W}. (CR 605.1)</b> — a single
///   <see cref="Majik.Core.Abilities.ManaAbility"/> built from the
///   <c>{ "kind": "mana", "produces": "W" }</c> definition.
///   <see cref="ValueObjects.ManaCost.Parse"/> maps the <c>W</c> pip to the
///   white bucket. The vanilla "{T}: Add" mana ability already taps the
///   source on activation, so no hand-rolled C# is required (same shape as
///   <see cref="PalladiumMyrFactory"/> / <see cref="OrnithopterOfParadiseFactory"/>).</item>
/// </list>
///
/// Summoning sickness (CR 302.6) is enforced by the engine at activation
/// time — not baked here.
/// </summary>
[CardName("Gold Myr")]
public static class GoldMyrFactory
{
    public const string CardName = "Gold Myr";
    public const string Slug = "gold-myr";

    /// <summary>
    /// Construct Gold Myr owned and controlled by <paramref name="owner"/>.
    /// The full card (Artifact Creature — Myr 1/1, {2}) and the
    /// {T}: Add {W} mana ability come from the embedded JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
