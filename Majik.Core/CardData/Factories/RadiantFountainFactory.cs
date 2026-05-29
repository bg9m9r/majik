using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Radiant Fountain (Zendikar / reprints).
///
/// Colorless gain-life land. Oracle text (verified against Scryfall):
///   "When this land enters, you gain 2 life.
///    {T}: Add {C}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/radiant-fountain.json</c>. Same oracle shape
/// as <see cref="AkoumRefugeFactory"/> (a mana ability plus a self-ETB
/// "gain N life" trigger, CR 119) — only the produced mana and life amount
/// differ:
/// - <b>{T}: Add {C}</b> — a single colourless <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1a). {C} (CR 107.4c) has no dedicated bucket in
///   <see cref="Majik.Core.ValueObjects.ManaCost"/> today; it is treated as
///   +1 generic, matching Rogue's Passage / Urza's Saga "{T}: Add {C}".
/// - <b>"When this land enters, you gain 2 life"</b> — a self-ETB
///   <see cref="Majik.Core.Abilities.TriggeredAbility"/> resolving to a
///   gain-2-life effect on the controller (CR 119.3).
/// </summary>
[CardName("Radiant Fountain")]
public static class RadiantFountainFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("radiant-fountain");

    /// <summary>Construct Radiant Fountain owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
