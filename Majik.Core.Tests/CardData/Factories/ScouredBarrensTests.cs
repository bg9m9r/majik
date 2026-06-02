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
/// Unit tests for <see cref="ScouredBarrensFactory"/> (Khans of Tarkir
/// "life-gain dual land", a.k.a. the Refuge cycle).
///
/// W/B "gain land". Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {B}."
///
/// Same oracle shape as <see cref="TranquilCoveFactory"/> — only the produced
/// colours differ ({W}/{B} instead of {W}/{U}). The ETB keyword action is a
/// flat "you gain 1 life" (CR 119.3). Loaded from the embedded JSON
/// definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {W} and {B} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the Refuge / Temple cycle.
/// </summary>
[Trait("Color", "C")]
public class ScouredBarrensTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ScouredBarrens_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Scoured Barrens", _alice);

        land.Name.Should().Be("Scoured Barrens");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Scoured Barrens is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void ScouredBarrens_HasManaAbility_ForWhite()
    {
        var land = (Land)NamedCardFactory.Create("Scoured Barrens", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void ScouredBarrens_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Scoured Barrens", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void ScouredBarrens_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Scoured Barrens", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void ScouredBarrens_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Scoured Barrens", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Scoured Barrens's ETB gains its controller 1 life");
    }
}
