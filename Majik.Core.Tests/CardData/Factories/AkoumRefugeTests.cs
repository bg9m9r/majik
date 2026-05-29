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
/// Unit tests for <see cref="AkoumRefugeFactory"/> (Zendikar "Refuge"
/// gain-life tapland cycle).
///
/// B/R "gain land". Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {B} or {R}."
///
/// Same oracle shape as <see cref="KazanduRefugeFactory"/> (Zendikar R/G
/// Refuge) — only the colours / printing differ. Loaded from the embedded
/// JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - Two single-colour mana abilities — {B} and {R} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the rest of the Refuge cycle.
/// </summary>
public class AkoumRefugeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AkoumRefuge_IsLand_WithCorrectName()
    {
        var land = AkoumRefugeFactory.Create(_alice);

        land.Name.Should().Be("Akoum Refuge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Akoum Refuge is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AkoumRefuge()
    {
        var card = NamedCardFactory.Create("Akoum Refuge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Akoum Refuge");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AkoumRefuge_HasManaAbility_ForBlack()
    {
        var land = AkoumRefugeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void AkoumRefuge_HasManaAbility_ForRed()
    {
        var land = AkoumRefugeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void AkoumRefuge_EtbTrigger_IsBattlefieldActive()
    {
        var land = AkoumRefugeFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void AkoumRefuge_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = AkoumRefugeFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Akoum Refuge's ETB gains its controller 1 life");
    }
}
