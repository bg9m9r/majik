using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Llanowar Elves (Alpha,
/// Creature — Elf Druid {G} 1/1).
///
/// Oracle text:
///   "{T}: Add {G}."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid, mana cost {G}, owner/controller wired.
/// - <b>"{T}: Add {G}"</b> (CR 605.1) — modelled as a single
///   <see cref="Majik.Core.Abilities.ManaAbility"/> producing one green
///   mana. Mana abilities don't use the stack (CR 605.3), so the picker
///   resolves the production immediately when tapped.
///
/// ## Notes
/// Vanilla mana-dork — no other abilities. Summoning-sickness gates the
/// tap on the turn it enters (CR 302.6) unless it gains haste.
/// </summary>
[CardName("Llanowar Elves")]
public static class LlanowarElvesFactory
{
    public const string CardName = "Llanowar Elves";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// CardDef DSL — single green mana ability (CR 605.1).
    /// </summary>
    public static CardDef Define() => CardDef
        .Creature(CardName, PrintedManaCost, Power, Toughness)
        .WithSubtypes(CardSubtype.Elf, CardSubtype.Druid)
        .ManaAbility("G");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
