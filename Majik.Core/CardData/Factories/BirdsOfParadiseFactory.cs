using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Birds of Paradise (Alpha,
/// Creature — Bird {G} 0/1).
///
/// Oracle text:
///   "Flying
///    {T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - 0/1 Creature — Bird, mana cost {G}, owner/controller wired.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="Majik.Core.Abilities.KeywordAbility"/>
///   marker; read by CombatAbilities.HasFlying and the evasion enforcement
///   path.
/// - <b>"Add one mana of any color"</b> (CR 605.1, 106.1b) — modelled as
///   five <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per
///   WUBRG). Mirrors the Delighted Halfling shape: the mana picker can
///   satisfy any single-colour pip by tapping Birds of Paradise once and
///   choosing which colour to produce.
///
/// ## Notes
/// Vanilla mana-dork — no activated non-mana abilities, no triggered
/// abilities, no static effects. Multiple copies stack correctly (each
/// instance carries its own keyword + mana-ability set).
/// </summary>
[CardName("Birds of Paradise")]
public static class BirdsOfParadiseFactory
{
    public const string CardName = "Birds of Paradise";
    public const string PrintedManaCost = "{G}";
    public const int Power = 0;
    public const int Toughness = 1;

    /// <summary>
    /// CardDef DSL — Flying + five WUBRG mana abilities (CR 702.9,
    /// CR 605.1). The five-mana-ability shape matches Delighted Halfling
    /// and is how the engine models "Add one mana of any color" today —
    /// the picker selects whichever colour satisfies the pending pip.
    /// </summary>
    public static CardDef Define() => CardDef
        .Creature(CardName, PrintedManaCost, Power, Toughness)
        .WithSubtypes(CardSubtype.Bird)
        .WithKeyword("Flying")
        .ManaAbility("W")
        .ManaAbility("U")
        .ManaAbility("B")
        .ManaAbility("R")
        .ManaAbility("G");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
