using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Badgermole Cub (Bloomburrow).
///
/// Creature — Bear {G} 1/1. Oracle text:
///   "When this creature enters, earthbend 1. (Target land you control becomes
///    a 0/0 creature with haste that's still a land. Put a +1/+1 counter on it.
///    When it dies or is exiled, return it to the battlefield tapped.)
///    Whenever you tap a creature for mana, add an additional {G}."
///
/// ## Implemented (v1)
/// - Correct name, type (Creature), subtype (Bear), mana cost ({G}),
///   power/toughness (1/1). Shell only — zero abilities attached.
///
/// Migrated to the fluent <see cref="CardDef"/> DSL. The
/// <see cref="Define"/> method is the canonical source; <see cref="Create"/>
/// is a thin typed shim so existing call sites keep their concrete return
/// type. The <c>NamedCardFactory</c> dispatcher reaches Define directly via
/// the source generator.
///
/// ## Deferred (v1 gaps)
/// - <b>Earthbend 1 ETB</b>: animate-land infra missing.
/// - <b>"Whenever you tap a creature for mana, add {G}"</b>: tap-for-mana
///   watcher missing.
/// </summary>
[CardName("Badgermole Cub")]
public static class BadgermoleCubFactory
{
    public static CardDef Define() => CardDef
        .Creature("Badgermole Cub", "{G}", power: 1, toughness: 1)
        .WithSubtype(CardSubtype.Bear);

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
