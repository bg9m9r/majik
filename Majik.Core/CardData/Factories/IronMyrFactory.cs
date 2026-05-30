using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Iron Myr (Mirrodin Besieged, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {R}."
///
/// The card's entire printed shape AND its mana ability are materialised
/// from the embedded JSON definition (<c>iron-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>:
/// <list type="bullet">
///   <item>Base shape — <b>Artifact Creature — Myr 1/1 at {2}</b>. The
///   factory lists <c>Creature</c> first (concrete C# class) then
///   additively flags <c>Artifact</c> via the multi-type seam, matching
///   the Mirrodin Myr cycle (always Artifact + Myr). Mirrors
///   <see cref="PalladiumMyrFactory"/>.</item>
///   <item><b>{T}: Add {R}. (CR 605.1)</b> — a single
///   <see cref="Majik.Core.Abilities.ManaAbility"/> built from the
///   <c>{ "kind": "mana", "produces": "R" }</c> definition. The vanilla
///   "{T}: Add" mana ability already taps the source on activation, so no
///   hand-rolled C# is required (same posture as Palladium Myr, whose
///   only difference is producing {C}{C} rather than one {R}).</item>
/// </list>
///
/// Summoning sickness (CR 302.6) is enforced by the engine at activation
/// time — not baked here.
/// </summary>
[CardName("Iron Myr")]
public static class IronMyrFactory
{
    public const string CardName = "Iron Myr";
    public const string Slug = "iron-myr";

    /// <summary>
    /// Construct Iron Myr owned and controlled by <paramref name="owner"/>.
    /// The full card (Artifact Creature — Myr 1/1, {2}) and the
    /// {T}: Add {R} mana ability come from the embedded JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
