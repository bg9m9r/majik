using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Druid of the Cowl (Amonkhet, {1}{G}).
///
/// Creature — Elf Druid 1/3. Oracle text (Scryfall):
///   "{T}: Add {G}."
///
/// ## Implemented (v1)
/// - <b>Creature — Elf Druid {1}{G} 1/3</b>, owner/controller wired. Types,
///   subtypes, P/T and mana cost come from
///   <c>Majik.Core/CardData/Cards/druid-of-the-cowl.json</c> built by
///   <see cref="CardDefinitionFactory"/> — same thin JSON-loaded wrapper shape
///   as <see cref="ParadiseDruidFactory"/>, just a single green mana ability
///   and no rider abilities.
/// - <b>Single mana ability (CR 605.1)</b>: {T}: Add {G}. Declared as one
///   <c>{ "kind": "mana", "produces": "G" }</c> ability in the JSON; the
///   builder attaches a <see cref="Abilities.ManaAbility"/> that taps the
///   druid and is gated on !IsTapped.
///
/// ## Notes
/// - Functionally a vanilla mana dork (cf. Llanowar Elves / Elvish Mystic /
///   Boreal Druid), only with a {1}{G} 1/3 body instead of a 1/1.
/// - Summoning sickness (CR 302.1 / 605.3a) is the engine's responsibility,
///   not this factory's — the mana ability is structurally available when
///   untapped; the engine gates activation at run-time.
/// </summary>
[CardName("Druid of the Cowl")]
public static class DruidOfTheCowlFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("druid-of-the-cowl");

    /// <summary>
    /// Build Druid of the Cowl's card shape (types, subtypes, P/T, single
    /// {T}: Add {G} mana ability) owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
