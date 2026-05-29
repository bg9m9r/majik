using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="JwarIsleRefugeFactory"/> (Worldwake) — a member
/// of the Zendikar/Worldwake "Refuge" gain-life dual-land cycle.
///
/// U/B "Refuge" land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {B}."
///
/// Same oracle shape as the Theros scry-land cycle
/// (<see cref="TempleOfTriumphFactory"/>) and the Murders at Karlov Manor
/// surveil-land cycle (<see cref="CommercialDistrictFactory"/>): a tapped
/// dual land with an ETB triggered ability, only here the ETB effect is
/// "gain 1 life" (a simple controller life-gain, CR 119.3). Loaded from the
/// embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {U} and {B} (CR 605.1a).
/// - One battlefield-active ETB triggered ability.
/// - The ETB effect gains the controller exactly 1 life (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the scry-land / surveil-land
/// cycles.
/// </summary>
public class JwarIsleRefugeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void JwarIsleRefuge_IsLand_WithCorrectName()
    {
        var land = JwarIsleRefugeFactory.Create(_alice);

        land.Name.Should().Be("Jwar Isle Refuge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Refuge lands are nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_JwarIsleRefuge()
    {
        var card = NamedCardFactory.Create("Jwar Isle Refuge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Jwar Isle Refuge");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void JwarIsleRefuge_HasManaAbility_ForBlue()
    {
        var land = JwarIsleRefugeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void JwarIsleRefuge_HasManaAbility_ForBlack()
    {
        var land = JwarIsleRefugeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void JwarIsleRefuge_EtbTrigger_IsBattlefieldActive()
    {
        var land = JwarIsleRefugeFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void JwarIsleRefuge_EtbEffect_GainsControllerOneLife()
    {
        var alice = new Player("Alice", 20);

        var land = JwarIsleRefugeFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the ETB trigger gains the controller 1 life (CR 119.3)");
    }
}
