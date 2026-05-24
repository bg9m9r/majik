using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sol Ring (Limited Edition Alpha, {1}).
///
/// Artifact. Oracle text:
///   "{T}: Add {C}{C}."
///
/// ## Implementation
///
/// Single <see cref="Majik.Core.Abilities.ManaAbility"/> taps Sol Ring
/// and adds two colourless. CR 605 covers the mana ability itself; CR
/// 107.4c routes {C} through the generic bucket via
/// <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (so
/// <c>Parse("CC")</c> yields a cost with <c>Generic == 2</c>).
///
/// Migrated to the fluent <see cref="CardDef"/> DSL.
///
/// ## Types
/// - Plain <see cref="Artifact"/>. No supertypes (not legendary on the
///   modern reprint — the Commander Legends / 30A printings re-add the
///   Legendary supertype, but the canonical Modern-legal Limited Edition
///   line is plain Artifact).
/// </summary>
[CardName("Sol Ring")]
public static class SolRingFactory
{
    public const string CardName = "Sol Ring";
    public const string PrintedManaCost = "{1}";

    public static CardDef Define() => CardDef
        .Artifact(CardName, PrintedManaCost)
        // {T}: Add {C}{C}. ManaCost.Parse("CC") buckets two {C} into
        // Generic = 2 (CR 107.4c — engine collapses colourless to generic).
        .ManaAbility("CC");

    public static Artifact Create(Player owner) =>
        (Artifact)CardDefRuntime.Build(Define(), owner);
}
