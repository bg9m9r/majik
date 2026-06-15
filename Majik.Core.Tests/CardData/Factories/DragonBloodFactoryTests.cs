using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DragonBloodFactory"/>.
///
/// Dragon Blood — Artifact, {3}. Oracle (Scryfall):
///   "{3}, {T}: Put a +1/+1 counter on target creature."
///
/// Covers card identity, the single {3}+{T}-cost targeted-counter activated
/// ability, its 1..1 target-creature request, and the +1/+1 counter landing on
/// the chosen creature (CR 122.1) — the on-card exercise of the targeted
/// PutCounterEffectDef the OracleActivatedAbilityBinder counter-other rebuild
/// shares.
/// </summary>
public class DragonBloodFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility CounterAbility(Artifact blood)
        => blood.Abilities.OfType<ActivatedAbility>().Single(a => a is not IManaAbility);

    [Fact]
    public void DragonBlood_IdentityIsCorrect()
    {
        var c = DragonBloodFactory.Create(_alice);

        c.Name.Should().Be("Dragon Blood");
        c.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void DragonBlood_HasSingleManaTapCounterAbility_WithTargetCreatureRequest()
    {
        var c = DragonBloodFactory.Create(_alice);
        var ability = CounterAbility(c);

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the activation taps Dragon Blood ({T})");
        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("3"),
                "the activation pays {3}");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task DragonBlood_PutsPlusOnePlusOneCounter_OnChosenCreature()
    {
        var alice = new Player("Alice", 20);

        var blood = DragonBloodFactory.Create(alice);
        blood.SetController(alice);
        blood.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(blood);
        blood.ClearSummoningSickness();

        var ally = new Creature("Ally Bear", "{1}{G}", 2, 2);
        ally.SetOwner(alice);
        ally.SetController(alice);
        ally.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(ally);

        var ability = CounterAbility(blood);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });

        var countersBefore = ally.Counters.Count(CounterType.PlusOnePlusOne);
        await ability.ResolveAsync(agent: null, game: null);

        ally.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(countersBefore + 1,
            "the +1/+1 counter is placed on the chosen creature (CR 122.1)");

        // Dragon Blood itself never receives the counter.
        blood.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the targeted counter lands on the chosen creature, not the source artifact");
    }
}
