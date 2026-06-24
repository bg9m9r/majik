using System.Collections.Generic;
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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
// ManaColor unused after switching to ManaGenerated.ToString() coverage check.
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TenderWildguideFactory"/>.
///
/// Tender Wildguide (Bloomburrow, {1}{G}). Creature — Possum Druid 2/2.
/// Oracle text (Scryfall, verified 2026-06-24):
///   "Offspring {2} (...)
///    {T}: Add one mana of any color.
///    {T}: Put a +1/+1 counter on this creature."
///
/// Covers the card's unique behaviour: the any-colour mana ability (five
/// ManaAbility instances, CR 605.1), the self-counter activated ability
/// (CR 602.2 + CR 122.1 — counter on THIS creature, no target), and the
/// Offspring {2} keyword marker (CR 702.169). Dispatch + well-formedness are
/// covered for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "G")]
public class TenderWildguideFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility SelfCounterAbility(Creature c)
        => c.Abilities.OfType<ActivatedAbility>().Single(a => a is not IManaAbility);

    [Fact]
    public void TenderWildguide_Identity_PossumDruid_2_2_At1G_WithOffspring()
    {
        var c = TenderWildguideFactory.Create(_alice);

        c.Name.Should().Be("Tender Wildguide");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Possum).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // CR 702.169 — Offspring {2} keyword marker (arg carries the {2} cost).
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Offspring")
            .Which.Arg.Should().Be(2, "Offspring's additional cost is {2}");
    }

    [Fact]
    public void TenderWildguide_HasFiveManaAbilities_OnePerColor()
    {
        // "{T}: Add one mana of any color." modeled as five ManaAbility
        // instances (one per WUBRG), mirroring Birds of Paradise.
        var c = TenderWildguideFactory.Create(_alice);
        var mas = c.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
    }

    [Fact]
    public void TenderWildguide_ManaAbilitiesCoverEveryColor()
    {
        var c = TenderWildguideFactory.Create(_alice);

        // ManaCost.ToString() returns bare colour letters — no braces.
        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Tender Wildguide can add one mana of ANY color");
    }

    [Fact]
    public void TenderWildguide_SelfCounterAbility_TapsAndTargetsNothing()
    {
        var c = TenderWildguideFactory.Create(_alice);
        var ability = SelfCounterAbility(c);

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the activation taps Tender Wildguide ({T})");
        ability.TargetRequests.Should().BeEmpty(
            "\"Put a +1/+1 counter on this creature\" targets nothing — the counter lands on the source");
    }

    [Fact]
    public async Task TenderWildguide_SelfCounterAbility_PutsPlusOnePlusOneOnItself()
    {
        var c = TenderWildguideFactory.Create(_alice);
        c.SetController(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();

        var ability = SelfCounterAbility(c);
        var before = c.Counters.Count(CounterType.PlusOnePlusOne);

        await ability.ResolveAsync(agent: null, game: null);

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(before + 1,
            "the +1/+1 counter is placed on Tender Wildguide itself (CR 122.1)");
    }

    [Fact]
    public void TenderWildguide_BuildOffspringCost_IsTwoGenericMana()
    {
        var c = TenderWildguideFactory.Create(_alice);
        var cost = TenderWildguideFactory.BuildOffspringCost(c);

        cost.Should().BeOfType<OffspringAdditionalCost>(
            "Offspring layers an OffspringAdditionalCost onto the cast");
        TenderWildguideFactory.OffspringCost.Should().Be(ManaCost.Parse("{2}"));
    }
}
