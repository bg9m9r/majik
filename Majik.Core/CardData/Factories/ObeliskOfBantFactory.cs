using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Obelisk of Bant (Conflux, {3}) — the Bant
/// ({G}{W}{U}) member of the Alara "Obelisk" tri-colour mana-rock cycle.
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add {G}, {W}, or {U}."
///
/// ## Implemented (v1)
/// - Artifact body / identity / owner / controller built from
///   <c>Majik.Core/CardData/Cards/obelisk-of-bant.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {G}, {W}, or {U}</b> — three
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per colour),
///   carried in the JSON definition as three <c>{ "kind": "mana" }</c> entries.
///   Per CR 605.1, mana abilities don't use the stack; the activator picks a
///   colour by picking the matching mana-ability slot, so no separate colour
///   prompt is needed. The implicit {T} self-tap is baked into each mana
///   ability's cost. Same shape as <see cref="ManalithFactory"/> /
///   <see cref="IndathaCrystalFactory"/>, minus cycling (Obelisk of Bant has
///   no cycling clause).
/// </summary>
[CardName("Obelisk of Bant")]
public static class ObeliskOfBantFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("obelisk-of-bant");

    /// <summary>
    /// Construct Obelisk of Bant owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
