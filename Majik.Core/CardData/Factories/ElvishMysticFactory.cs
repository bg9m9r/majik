using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Mystic (Magic 2014, {G}).
///
/// Creature — Elf Druid 1/1. Oracle text:
///   "{T}: Add {G}."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid, mana cost {G}, owner / controller wired.
/// - <b>Single mana ability (CR 605.1)</b>: {T}: Add {G}. Implemented as a
///   <see cref="ManaAbility"/> with a <c>canActivateCheck = !IsTapped</c>
///   gate — mirrors the per-colour mana abilities on
///   <see cref="LlanowarElvesFactory"/> (functional reprint).
///
/// ## Notes
/// - Elvish Mystic is a functional reprint of Llanowar Elves; the shape
///   intentionally mirrors <see cref="LlanowarElvesFactory"/> verbatim.
/// - Summoning sickness (CR 302.1 / 605.3a) is the engine's responsibility,
///   not this factory's — the mana ability is structurally always available
///   when untapped; the engine gates activation at run-time.
/// </summary>
[CardName("Elvish Mystic")]
public static class ElvishMysticFactory
{
    public const string CardName = "Elvish Mystic";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Elvish Mystic owned and controlled by
    /// <paramref name="owner"/>. The single {T}: Add {G} mana ability is
    /// attached structurally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 605.1 — {T}: Add {G}. Mana ability (no stack). Tap the elf
        // when activated; gate on !IsTapped so duplicate activations are
        // prevented. Mirrors Llanowar Elves' single-colour shape.
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
