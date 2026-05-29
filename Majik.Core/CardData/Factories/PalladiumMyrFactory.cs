using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Palladium Myr (Scars of Mirrodin, {4}).
///
/// Artifact Creature — Myr 2/2. Oracle text (verified against Scryfall):
///   "{T}: Add {C}{C}."
///
/// The card's entire printed shape AND its mana ability are materialised
/// from the embedded JSON definition (<c>palladium-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>:
/// <list type="bullet">
///   <item>Base shape — <b>Artifact Creature — Myr 2/2 at {4}</b>. The
///   factory lists <c>Creature</c> first (concrete C# class) then
///   additively flags <c>Artifact</c> via the multi-type seam, matching
///   the Mirrodin Myr cycle (always Artifact + Myr).</item>
///   <item><b>{T}: Add {C}{C}. (CR 605.1)</b> — a single
///   <see cref="Majik.Core.Abilities.ManaAbility"/> built from the
///   <c>{ "kind": "mana", "produces": "CC" }</c> definition. Both
///   colourless pips are emitted together in one activation;
///   <see cref="ValueObjects.ManaCost.Parse"/> buckets each {C} as +1
///   generic (CR 107.4c — no dedicated colourless bucket today, same
///   convention as Plague Myr / Mind Stone / Inkmoth Nexus). The vanilla
///   "{T}: Add" mana ability already taps the source on activation, so no
///   hand-rolled C# is required.</item>
/// </list>
///
/// Summoning sickness (CR 302.6) is enforced by the engine at activation
/// time — not baked here.
/// </summary>
[CardName("Palladium Myr")]
public static class PalladiumMyrFactory
{
    public const string CardName = "Palladium Myr";
    public const string Slug = "palladium-myr";

    /// <summary>
    /// Construct Palladium Myr owned and controlled by
    /// <paramref name="owner"/>. The full card (Artifact Creature — Myr
    /// 2/2, {4}) and the {T}: Add {C}{C} mana ability come from the
    /// embedded JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
