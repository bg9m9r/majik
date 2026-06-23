using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Obelisk of Naya ({3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add {R}, {G}, or {W}."
///
/// ## Implemented (v1)
/// - Artifact body / identity / owner / controller built from
///   <c>Majik.Core/CardData/Cards/obelisk-of-naya.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {R}, {G}, or {W}</b> — three
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per Naya
///   colour R/G/W), carried in the JSON definition as three
///   <c>{ "kind": "mana" }</c> entries. The activator picks a colour by
///   picking the matching mana-ability slot, so no separate colour prompt is
///   needed (CR 605.1 — mana abilities don't use the stack; CR 605.1a — a
///   "choose a colour" mana ability is modelled as one slot per producible
///   colour). The implicit {T} self-tap is baked into each mana ability's
///   cost. Same shape as <see cref="ManalithFactory"/> / the Ikoria "Crystal"
///   tri-colour rocks, just on the Naya colour triple.
/// </summary>
[CardName("Obelisk of Naya")]
public static class ObeliskOfNayaFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("obelisk-of-naya");

    /// <summary>
    /// Construct Obelisk of Naya owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
