using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KazanduRefugeFactory"/> (Zendikar "Refuge"
/// gain-life tapland cycle).
///
/// R/G "gain land". Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {R} or {G}."
///
/// Same oracle shape as <see cref="RuggedHighlandsFactory"/> (Innistrad R/G
/// Refuge) — only the printing differs. Loaded from the embedded JSON
/// definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - Two single-colour mana abilities — {R} and {G} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the rest of the Refuge cycle.
/// </summary>
[Trait("Color", "C")]
public class KazanduRefugeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void KazanduRefuge_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Kazandu Refuge", _alice);

        land.Name.Should().Be("Kazandu Refuge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Kazandu Refuge is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void KazanduRefuge_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Kazandu Refuge", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void KazanduRefuge_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Kazandu Refuge", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void KazanduRefuge_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Kazandu Refuge", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void KazanduRefuge_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Kazandu Refuge", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Kazandu Refuge's ETB gains its controller 1 life");
    }
}
