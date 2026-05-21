using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KraulHarpoonerFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, subtypes, power/toughness, owner/controller)
/// - Ability set: one KeywordAbility (Reach) + one TriggeredAbility (ETB Undergrowth)
/// - ETB effect: no buff when graveyard has no creature cards
/// - ETB effect: +X/+0 buff registered when X creature cards are in graveyard
/// </summary>
public class KraulHarpoonerTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KraulHarpooner_NameIsCorrect()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.Name.Should().Be("Kraul Harpooner");
    }

    [Fact]
    public void KraulHarpooner_IsCreature()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void KraulHarpooner_HasCorrectSubtypes()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.HasSubtype(CardSubtype.Insect).Should().BeTrue("Kraul Harpooner is an Insect");
        k.HasSubtype(CardSubtype.Warrior).Should().BeTrue("Kraul Harpooner is a Warrior");
    }

    [Fact]
    public void KraulHarpooner_HasCorrectStats()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.BasePower.Should().Be(3);
        k.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void KraulHarpooner_OwnerAndControllerAreSet()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.Owner.Should().BeSameAs(_alice);
        k.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void KraulHarpooner_HasReachKeyword()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Reach",
                "Kraul Harpooner has Reach");
    }

    [Fact]
    public void KraulHarpooner_HasExactlyOneTriggeredAbility()
    {
        var k = KraulHarpoonerFactory.Create(_alice);

        k.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the Undergrowth ETB is the only triggered ability");
    }

    // -----------------------------------------------------------------------
    // ETB Undergrowth effect
    // -----------------------------------------------------------------------

    [Fact]
    public void KraulHarpooner_EtbEffect_NoBuff_WhenGraveyardHasNoCreatures()
    {
        var alice = new Player("Alice", 20);
        var k = KraulHarpoonerFactory.Create(alice);
        var service = new ContinuousEffectsService();
        k.ActiveEffects = service;

        // Graveyard has a non-creature card only.
        var instant = new Instant("Shock", "R");
        instant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(instant);
        instant.SetZone(ZoneType.Graveyard);

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // No pump registered; base stats unchanged.
        k.GetPower().Should().Be(3, "no creatures in graveyard → X = 0, no buff");
        k.GetToughness().Should().Be(2);
    }

    [Fact]
    public void KraulHarpooner_EtbEffect_BuffsByCreatureCardCount()
    {
        var alice = new Player("Alice", 20);
        var k = KraulHarpoonerFactory.Create(alice);
        var service = new ContinuousEffectsService();
        k.ActiveEffects = service;

        // Two creature cards in graveyard.
        for (var i = 0; i < 2; i++)
        {
            var c = new Creature($"Bear{i}", "1G", 2, 2);
            c.SetOwner(alice);
            alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // +2/+0 applied; toughness unchanged.
        k.GetPower().Should().Be(5, "X = 2 creatures in graveyard → +2/+0 → power 5");
        k.GetToughness().Should().Be(2, "toughness is unaffected by +X/+0");
    }

    [Fact]
    public void KraulHarpooner_EtbEffect_NoActiveEffects_DoesNotThrow()
    {
        // When ActiveEffects is null the buff is silently skipped (no service wired).
        var alice = new Player("Alice", 20);
        var k = KraulHarpoonerFactory.Create(alice);
        // k.ActiveEffects is null — pump code guards with != null.

        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("when ActiveEffects is null the buff is silently skipped");
    }
}
