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
/// Unit tests for <see cref="JungleHollowFactory"/> (Khans of Tarkir).
///
/// B/G "life gain land". Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {B} or {G}."
///
/// Same oracle shape as the Tarkir "refuge"/gainland cycle
/// (<see cref="BloodfellCavesFactory"/>) — ETB-tapped + an ETB self-trigger
/// + two single-colour mana abilities — except this member produces {B}/{G}.
/// The ETB keyword action is "you gain 1 life" (CR 119.3). Loaded from the
/// embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {B} and {G} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect raises the controller's life total by exactly 1.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the cycle.
/// </summary>
public class JungleHollowTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void JungleHollow_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Hollow", _alice);

        land.Name.Should().Be("Jungle Hollow");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Jungle Hollow is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_JungleHollow()
    {
        var card = NamedCardFactory.Create("Jungle Hollow", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Jungle Hollow");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void JungleHollow_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Hollow", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void JungleHollow_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Hollow", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void JungleHollow_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Hollow", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void JungleHollow_EtbEffect_GainsOneLife_ForController()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Jungle Hollow", alice);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the ETB trigger gains the controller 1 life (CR 119.3)");
    }
}
