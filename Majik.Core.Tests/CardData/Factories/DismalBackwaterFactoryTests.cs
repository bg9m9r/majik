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
/// Unit tests for <see cref="DismalBackwaterFactory"/> — the Khans of
/// Tarkir "gain land" (a.k.a. life-gain dual / refuge cycle).
///
/// Oracle text (Scryfall, verified):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {B}."
///
/// Shape mirrors the surveil-land cycle (Commercial District et al.):
///   - Land (no subtypes / supertypes).
///   - Two single-colour mana abilities ({U} and {B}).
///   - One ETB-triggered ability (battlefield-active) that gains 1 life.
///   - Enters-tapped is applied on the production load path by
///     <see cref="Majik.Core.CardData.EntersTappedBinder"/> (CR 614.1c),
///     not by the named-card factory (test convenience), same as the
///     surveil-land cycle.
/// </summary>
[Trait("Color", "C")]
public class DismalBackwaterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static int ColorOf(ManaCost m, string c) => c switch
    {
        "W" => m.White,
        "U" => m.Blue,
        "B" => m.Black,
        "R" => m.Red,
        "G" => m.Green,
        _ => throw new ArgumentException($"Unknown colour {c}"),
    };

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DismalBackwater_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Dismal Backwater", _alice);

        land.Name.Should().Be("Dismal Backwater");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("gain lands are nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // {T}: Add {U} or {B} — two single-colour mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void DismalBackwater_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Dismal Backwater", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => ColorOf(m.ManaGenerated, "U") == 1
                                      && ColorOf(m.ManaGenerated, "B") == 0);
    }

    [Fact]
    public void DismalBackwater_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Dismal Backwater", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => ColorOf(m.ManaGenerated, "B") == 1
                                      && ColorOf(m.ManaGenerated, "U") == 0);
    }

    // -----------------------------------------------------------------------
    // ETB: you gain 1 life (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void DismalBackwater_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Dismal Backwater", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void DismalBackwater_EtbEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Dismal Backwater", alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the controller gains 1 life when this land enters");
    }
}
