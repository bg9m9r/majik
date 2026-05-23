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
/// Unit tests for <see cref="EarthshakerKhenraFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, subtypes, P/T, owner/controller).
/// - Ability set: a Haste KeywordAbility marker + a single ETB TriggeredAbility
///   with a 1..1 TargetRequest.
/// - ETB resolution: a CannotBlock CombatRestrictionEffect is registered on the
///   chosen target's ContinuousEffectsService when the target's power ≤ 2.
/// - ETB resolution gates: power-too-high (>2) leaves no restriction; null
///   ActiveEffects on the target is a no-op.
/// - NamedCardFactory dispatch returns an EarthshakerKhenra instance.
/// </summary>
public class EarthshakerKhenraTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EarthshakerKhenra_NameIsCorrect()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.Name.Should().Be("Earthshaker Khenra");
    }

    [Fact]
    public void EarthshakerKhenra_IsCreature()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void EarthshakerKhenra_HasCorrectSubtypes()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.HasSubtype(CardSubtype.Minotaur).Should().BeTrue("printed oracle is Minotaur Warrior");
        k.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void EarthshakerKhenra_HasCorrectStats()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.BasePower.Should().Be(2);
        k.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void EarthshakerKhenra_OwnerAndControllerAreSet()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.Owner.Should().BeSameAs(_alice);
        k.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void EarthshakerKhenra_HasHasteKeyword()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Haste",
                "Earthshaker Khenra has Haste (CR 702.10)");
    }

    [Fact]
    public void EarthshakerKhenra_HasExactlyOneTriggeredAbility()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        k.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB target-can't-block trigger is the only triggered ability");
    }

    [Fact]
    public void EarthshakerKhenra_EtbTriggerDeclaresOneTargetRequest()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        etb.TargetRequests.Should().HaveCount(1, "the ETB targets one creature");
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — CannotBlock restriction registration
    // -----------------------------------------------------------------------

    [Fact]
    public void EarthshakerKhenra_EtbEffect_RegistersCannotBlockOnLegalTarget()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        service.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeTrue(
            "the ETB rider locks the chosen creature out of blocking this turn");
    }

    [Fact]
    public void EarthshakerKhenra_EtbEffect_NoRestriction_WhenTargetPowerExceedsThreshold()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        // 3-power target — power exceeds the printed "2 or less" gate.
        var target = new Creature("Hill Giant", "3R", 3, 3);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        service.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeFalse(
            "power-3 creature fails the resolution-time 'power 2 or less' recheck (CR 608.2b)");
    }

    [Fact]
    public void EarthshakerKhenra_EtbEffect_NoActiveEffects_DoesNotThrow()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        // Target with no ContinuousEffectsService wired — shape-only.
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects is null.

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("the effect body guards on a null ActiveEffects");
    }

    [Fact]
    public void EarthshakerKhenra_EtbEffect_NoRestriction_WhenTargetLeftBattlefield()
    {
        var k = EarthshakerKhenraFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        // Target was put in graveyard between target-pick and resolution —
        // CR 608.2b illegal-target check fails.
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Graveyard);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = k.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        service.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeFalse(
            "target left the battlefield between cast and resolution — CR 608.2b");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchesEarthshakerKhenra()
    {
        var card = Majik.Core.CardData.NamedCardFactory.Create("Earthshaker Khenra", _alice);

        card.Should().BeOfType<Creature>("Earthshaker Khenra is a Creature");
        card.Name.Should().Be("Earthshaker Khenra");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(a => a.Keyword == "Haste",
                "the dispatcher returns a fully-wired card with Haste");
        card.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the dispatcher attaches the ETB trigger");
    }
}
