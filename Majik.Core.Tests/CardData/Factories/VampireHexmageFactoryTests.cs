using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VampireHexmageFactory"/>.
///
/// Vampire Hexmage (Zendikar, {B}{B}). Creature — Vampire Shaman 2/1.
/// Oracle text (verified against Scryfall):
///   "First strike
///    Sacrifice this creature: Remove all counters from target permanent."
///
/// Covers:
/// - Identity ({B}{B} Creature — Vampire Shaman, 2/1, mono-black).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - First strike keyword marker (CR 702.7).
/// - Exactly one activated ability with a 1..1 "target permanent" slot.
/// - Sacrifice cost: activating the ability moves the Hexmage to the graveyard.
/// - Effect removes ALL counters of every type from the target permanent.
/// </summary>
[Trait("Color", "B")]
public class VampireHexmageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireHexmage_Identity()
    {
        var c = VampireHexmageFactory.Create(_alice);

        c.Name.Should().Be("Vampire Hexmage");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.ManaCost.Should().Be("{B}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VampireHexmage_IsMonoBlack()
    {
        var c = VampireHexmageFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().ContainSingle().Which.Should().Be(ManaColor.Black);
    }

    // -----------------------------------------------------------------------
    // Keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireHexmage_HasFirstStrike()
    {
        var c = VampireHexmageFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(c).Should().BeTrue("CR 702.7 — first strike");
    }

    // -----------------------------------------------------------------------
    // Activated-ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireHexmage_HasExactlyOneActivatedAbility_WithSingleTargetSlot()
    {
        var c = VampireHexmageFactory.Create(_alice);

        var abilities = c.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1, "exactly one sacrifice-to-remove-counters ability");

        var ability = abilities.Single();
        ability.TargetRequests.Should().HaveCount(1, "one 'target permanent' slot");
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Sacrifice cost
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireHexmage_Activation_SacrificesSelf_ToGraveyard()
    {
        var hexmage = VampireHexmageFactory.Create(_alice);
        hexmage.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hexmage);

        var target = new Creature("Walking Ballista", "{0}", 0, 0);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);
        target.Counters.Add(CounterType.PlusOnePlusOne, 4);

        var ability = hexmage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in ability.Effects) effect.Execute();

        hexmage.Zone.Should().Be(ZoneType.Graveyard,
            "activating the ability sacrifices the Hexmage (CR 701.16)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(hexmage);
        _alice.Zones.Graveyard.GetCards().Should().Contain(hexmage);
    }

    // -----------------------------------------------------------------------
    // Effect — remove ALL counters
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireHexmage_RemovesAllCountersFromTargetPermanent()
    {
        var hexmage = VampireHexmageFactory.Create(_alice);
        hexmage.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hexmage);

        var target = new Creature("Walking Ballista", "{0}", 0, 0);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);
        target.Counters.Add(CounterType.PlusOnePlusOne, 5);

        var ability = hexmage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in ability.Effects) effect.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Vampire Hexmage removes ALL counters from the target permanent (CR 122.5)");
    }

    [Fact]
    public void VampireHexmage_RemovesAllCounters_AcrossMultipleCounterTypes()
    {
        var hexmage = VampireHexmageFactory.Create(_alice);
        hexmage.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hexmage);

        // A planeswalker-style mix: e.g. loyalty + a +1/+1 marker. The classic
        // combo target is a planeswalker (Dark Depths' ice counters); a multi-
        // type mix proves "all" drains every type.
        var target = new Creature("Counter Soup", "{2}", 1, 1);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);
        target.Counters.Add(CounterType.PlusOnePlusOne, 3);
        target.Counters.Add(CounterType.MinusOneMinusOne, 2);

        var ability = hexmage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in ability.Effects) effect.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        target.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
        target.Counters.HasAny.Should().BeFalse("'remove all counters' drains every type");
    }

    [Fact]
    public void VampireHexmage_NoTarget_IsNoOp_StillSacrifices()
    {
        var hexmage = VampireHexmageFactory.Create(_alice);
        hexmage.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hexmage);

        var ability = hexmage.Abilities.OfType<ActivatedAbility>().Single();
        // No targets set.
        foreach (var effect in ability.Effects) effect.Execute();

        hexmage.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost is still paid even with no resolvable target");
    }
}
