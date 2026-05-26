using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Karn's Bastion (War of the Spark).
///
/// Oracle:
///   "{T}: Add {C}.
///    {4}, {T}: Proliferate."
///
/// Coverage:
///   * Identity — plain Land, no printed supertypes/subtypes.
///   * NamedCardFactory dispatches Karn's Bastion to a Land.
///   * {T}: Add {C} — vanilla mana ability that taps the land for {C}.
///   * {4}, {T}: Proliferate — single ActivatedAbility distinct from the
///     mana ability; on Resolve, adds one more counter of an existing
///     kind to each controller-side permanent that already has one.
/// </summary>
public class KarnsBastionTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var bastion = KarnsBastionFactory.Create(_alice);

        bastion.Name.Should().Be("Karn's Bastion");
        bastion.HasType(CardType.Land).Should().BeTrue();
        bastion.Supertypes.Should().BeEmpty(
            "Karn's Bastion has no printed supertypes");
        bastion.Subtypes.Should().BeEmpty(
            "Karn's Bastion has no printed land subtypes");
        bastion.Owner.Should().BeSameAs(_alice);
        bastion.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Karn's Bastion", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Karn's Bastion");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void HasColorlessManaAbility_TappingProducesColorless()
    {
        var bastion = KarnsBastionFactory.Create(_alice);
        var manaAbility = bastion.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        produced.Generic.Should().Be(1,
            "{C} buckets as Generic +1 in ManaCost.Parse (same bucket as Mutavault / Mishra's Workshop)");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        bastion.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {4}, {T}: Proliferate
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleProliferateActivatedAbility_AlongsideManaAbility()
    {
        var bastion = KarnsBastionFactory.Create(_alice);

        var activated = bastion.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Effects.Should().HaveCount(1);
        activated.TargetRequests.Should().BeEmpty(
            "proliferate doesn't take targets — it self-selects the permanent/player set");
    }

    [Fact]
    public void Resolve_ProliferatesEveryControllerSidePermanentWithACounter()
    {
        var bastion = KarnsBastionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        // Two permanents on Alice's battlefield: one already has a +1/+1
        // counter (gets proliferated), one has no counters (skipped).
        var counted = new Creature("Walking Ballista", "{0}", 0, 0);
        counted.SetOwner(_alice);
        counted.SetController(_alice);
        counted.Counters.Add(CounterType.PlusOnePlusOne, 2);
        _alice.Zones.Battlefield.AddCard(counted);
        counted.SetZone(ZoneType.Battlefield);

        var uncountered = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        uncountered.SetOwner(_alice);
        uncountered.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(uncountered);
        uncountered.SetZone(ZoneType.Battlefield);

        var activated = bastion.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Resolve();

        counted.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "proliferate adds one more counter of an existing kind (CR 701.27) to every permanent with a counter");
        uncountered.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "permanents with no counters are NOT touched by proliferate");
    }
}
