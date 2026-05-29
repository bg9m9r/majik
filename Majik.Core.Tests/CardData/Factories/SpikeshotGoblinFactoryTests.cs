using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpikeshotGoblinFactory"/>.
///
/// Spikeshot Goblin (Mirrodin, {2}{R}):
///   Creature — Goblin Shaman 1/2.
///   "{R}, {T}: This creature deals damage equal to its power to any target."
///
/// Covers:
///   - Identity (Goblin Shaman 1/2, {2}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Activated ability shape: {R} mana cost + tap cost + one any-target
///     request (CR 602). No sacrifice.
///   - Resolution: damage equal to the creature's CURRENT power
///     (CR 608.2 — read at resolution) to player / creature target;
///     planeswalker target routes through loyalty removal (CR 306.7).
///   - Power-scaling: a pump effect that raises Spikeshot's power increases
///     the damage dealt (proves it reads live power, not the printed 1).
/// </summary>
public class SpikeshotGoblinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpikeshotGoblin_Identity()
    {
        var sg = SpikeshotGoblinFactory.Create(_alice);

        sg.Name.Should().Be("Spikeshot Goblin");
        sg.ManaCost.Should().Be("{2}{R}");
        sg.HasType(CardType.Creature).Should().BeTrue();
        sg.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        sg.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        sg.BasePower.Should().Be(1);
        sg.BaseToughness.Should().Be(2);
        sg.Owner.Should().BeSameAs(_alice);
        sg.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpikeshotGoblin_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Spikeshot Goblin", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spikeshot Goblin");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpikeshotGoblin_ActivatedAbility_HasManaAndTap_OneAnyTarget()
    {
        var sg = SpikeshotGoblinFactory.Create(_alice);

        var ability = sg.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the ping ability has a {R} mana cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the ability has a {T} cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Sacrifice,
                "Spikeshot Goblin does not sacrifice itself.");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Ping_DealsPowerToPlayerTarget()
    {
        var sg = SpikeshotGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sg);
        sg.SetZone(ZoneType.Battlefield);

        var ability = sg.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        // Base power is 1 → 1 damage.
        _bob.LifeTotal.Should().Be(19, "1 damage (Spikeshot's power) to Bob");
    }

    [Fact]
    public void Activate_Ping_DealsPowerToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var sg = SpikeshotGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sg);
        sg.SetZone(ZoneType.Battlefield);

        var ability = sg.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.Damage.Should().Be(1, "1 marked damage (Spikeshot's power) on the bears");
    }

    [Fact]
    public void Activate_Ping_PlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.7 — damage to a planeswalker removes loyalty counters.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var sg = SpikeshotGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sg);
        sg.SetZone(ZoneType.Battlefield);

        var ability = sg.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        ability.Resolve();

        pw.Loyalty.Should().Be(3, "1 loyalty counter removed (4 - 1, Spikeshot's power)");
    }

    [Fact]
    public void Activate_Ping_DamageScalesWithCurrentPower()
    {
        // CR 608.2 — the amount is determined as the ability resolves, so a
        // power pump on Spikeshot increases the damage dealt. This proves the
        // factory reads live power (Creature.Power), not the printed 1.
        var sg = SpikeshotGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sg);
        sg.SetZone(ZoneType.Battlefield);

        // Raise base power to 4 → "damage equal to its power" = 4.
        sg.BasePower = 4;
        sg.Power.Should().Be(4, "sanity: current power reflects the bump");

        var ability = sg.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(16, "4 damage equal to Spikeshot's pumped power");
    }
}
