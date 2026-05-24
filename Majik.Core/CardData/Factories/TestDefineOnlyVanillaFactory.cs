using Majik.Core.CardData.Definitions;
using Majik.Core.Cards.Types;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Synthetic fixture used by the DSL test suite to exercise the
/// <c>Define()</c>-only path through <c>NamedCardFactoryGenerator</c>. No
/// <c>Create(Player owner)</c> overload — the source generator must
/// synthesize the dispatch arm by calling
/// <see cref="CardDefRuntime.Build"/> directly.
///
/// Vanilla 1/1 Elf shape with no Modern card-pool collision.
/// </summary>
[CardName("DSL Test Vanilla Elf")]
public static class TestDefineOnlyVanillaFactory
{
    public static CardDef Define() => CardDef
        .Creature("DSL Test Vanilla Elf", "{G}", power: 1, toughness: 1)
        .WithSubtype(CardSubtype.Elf);
}
