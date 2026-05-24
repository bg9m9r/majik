using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phoenix of Ash (Throne of Eldraine, {2}{R}{R}).
///
/// Creature — Phoenix 3/2. Oracle text:
///   "Phoenix of Ash can attack as though it didn't have summoning sickness
///    as long as it has haste.
///    Haste.
///    Escape—{3}{R}{R}, Exile four other cards from your graveyard.
///    (You may cast this card from your graveyard for its escape cost.)"
///
/// ## Implemented (v1)
/// - 3/2 Creature — Phoenix, mana cost {2}{R}{R}.
/// - <see cref="Majik.Core.Abilities.KeywordAbility"/> marker for Haste
///   (CR 702.10). The "can attack as though it didn't have summoning
///   sickness as long as it has haste" rider collapses observationally
///   to the Haste keyword in v1 — CR 702.10b already covers the typical
///   case. Same posture as <see cref="ArclightPhoenixFactory"/>.
///
/// Migrated to the fluent <see cref="CardDef"/> DSL.
///
/// - <b>Escape (CR 702.138) — wired via
///   <see cref="EscapeAlternativeCost"/></b>: cast-from-graveyard
///   alt cost with the "exile four other cards from your graveyard"
///   rider. <see cref="BuildAlternativeCost"/> returns the bound
///   alt-cost instance.
///
/// ## Deferred (v1 gaps)
/// - <b>"Can attack as though …"</b>: collapses to the Haste grant in v1.
/// </summary>
[CardName("Phoenix of Ash")]
public static class PhoenixOfAshFactory
{
    public const string CardName = "Phoenix of Ash";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>CR 702.138 — printed Escape mana cost: {3}{R}{R}.</summary>
    public const string EscapeManaCost = "{3}{R}{R}";

    /// <summary>CR 702.138a — Escape rider: exile four OTHER cards from
    /// your graveyard.</summary>
    public const int EscapeExileCount = 4;

    /// <summary>
    /// CR 702.138 — Phoenix of Ash's printed Escape alt-cost
    /// ({3}{R}{R}, exile four OTHER graveyard cards).
    /// </summary>
    public static EscapeAlternativeCost BuildAlternativeCost() =>
        new(ValueObjects.ManaCost.Parse(EscapeManaCost), EscapeExileCount);

    public static CardDef Define() => CardDef
        .Creature(CardName, PrintedManaCost, power: 3, toughness: 2)
        .WithSubtype(CardSubtype.Phoenix)
        // CR 702.10 — Haste.
        .WithKeyword("Haste");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
