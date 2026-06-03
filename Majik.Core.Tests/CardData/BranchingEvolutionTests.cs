using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BranchingEvolutionFactory"/>.
///
/// Card: Branching Evolution — Enchantment {2}{G} (Jumpstart).
///   "If one or more +1/+1 counters would be put on a creature you control,
///    twice that many +1/+1 counters are put on that creature instead."
///
/// Covers:
///   - Identity / dispatch.
///   - +1/+1 counter doubling on a controlled creature (1 → 2, 3 → 6).
///   - Stacks across two Branching Evolutions (×4) and alongside Hardened
///     Scales (registration-order dependent — verifies both fire once).
///   - Scoping: opponent's creature not doubled; non-creature (artifact) not
///     doubled; non-+1/+1 counter kind not doubled.
///   - Inert while off the battlefield.
///   - Single-arg create (no bus) is shape-only.
/// </summary>
public class BranchingEvolutionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BranchingEvolution_Identity()
    {
        var c = BranchingEvolutionFactory.Create(_alice);

        c.Name.Should().Be("Branching Evolution");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BranchingEvolution()
    {
        var card = NamedCardFactory.Create("Branching Evolution", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Branching Evolution");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{G}");
    }

    // -----------------------------------------------------------------------
    // Doubling on a controlled creature
    // -----------------------------------------------------------------------

    [Fact]
    public void PlusOneCounter_OnControlledCreature_IsDoubled()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(2, "1 requested × 2 from Branching Evolution");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void ThreeCounters_AreDoubledToSix()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 3, bus);

        placed.Should().Be(6, "3 × 2 = 6 ('twice that many')");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(6);
    }

    [Fact]
    public void TwoBranchingEvolutions_StackMultiplicatively()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(4, "1 × 2 × 2 — CR 616.1c, each doubler fires once");
    }

    [Fact]
    public void StacksWithHardenedScales()
    {
        var bus = new ReplacementBus();
        // Branching Evolution registered first, then Hardened Scales.
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);
        var scales = HardenedScalesFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(scales);
        scales.SetZone(ZoneType.Battlefield);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        // Applied in registration order: Branching Evolution doubles 1 → 2,
        // then Hardened Scales adds 1 → 3. (The affected-player ordering prompt
        // — CR 616.1 — is a known v1 gap; the bus applies in registration order.)
        placed.Should().Be(3, "1 ×2 (Branching Evolution) +1 (Hardened Scales) = 3 in registration order");
    }

    // -----------------------------------------------------------------------
    // Scoping
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCreature_NotDoubled()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);

        var bobBear = ControlledCreature(_bob);

        var placed = CountersService.Add(bobBear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(1, "Branching Evolution is one-sided ('a creature you control')");
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Artifact_NotDoubled()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);

        var artifact = new Artifact("Walking Ballista", "{X}{X}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(artifact, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(1, "Branching Evolution applies only to creatures, not artifacts");
        artifact.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void MinusOneMinusOne_NotDoubled()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(BranchingEvolutionFactory.Create(_alice, bus), _alice);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.MinusOneMinusOne, 1, bus);

        placed.Should().Be(1, "Branching Evolution scopes to +1/+1 counters only");
        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Lifecycle / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BranchingEvolution_OffBattlefield_DoesNotDouble()
    {
        var bus = new ReplacementBus();
        // Create with a bus but leave the enchantment off the battlefield.
        BranchingEvolutionFactory.Create(_alice, bus);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);
        placed.Should().Be(1, "Branching Evolution must be on the battlefield to fire");
    }

    [Fact]
    public void SingleArgFactory_DoesNotRegisterReplacement()
    {
        var c = BranchingEvolutionFactory.Create(_alice);
        c.Should().NotBeNull();
        c.Name.Should().Be("Branching Evolution");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature ControlledCreature(Player controller)
    {
        var bear = new Creature("Test Bear", "{1}{G}", 2, 2);
        bear.SetOwner(controller);
        bear.SetController(controller);
        controller.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    private static void PlaceOnBattlefield(Enchantment evolution, Player owner)
    {
        owner.Zones.Battlefield.AddCard(evolution);
        evolution.SetZone(ZoneType.Battlefield);
    }
}
