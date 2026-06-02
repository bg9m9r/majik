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
/// Unit tests for <see cref="WindingConstrictorFactory"/>.
///
/// Card: Winding Constrictor — Creature — Snake {B}{G} 2/2 (Aether Revolt).
///   "If one or more counters would be put on an artifact or creature you
///    control, that many plus one of each of those kinds of counters are put
///    on that permanent instead. If you would get one or more counters, you
///    get that many plus one of each of those kinds of counters instead."
///
/// Covers (first clause — fully implemented):
///   - Identity / dispatch.
///   - +1/+1 counter bump on a controlled creature (1 → 2).
///   - Bump on a controlled ARTIFACT (generalizes past Hardened Scales).
///   - Bump on a NON +1/+1 counter kind (charge) — "of each of those kinds".
///   - Stacks across two Constrictors (+2) and alongside Hardened Scales.
///   - Scoping: opponent's permanent not bumped; non-artifact/creature
///     (enchantment) not bumped.
///   - Inert while off the battlefield.
///   - Single-arg create (no bus) is shape-only.
/// </summary>
public class WindingConstrictorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WindingConstrictor_Identity()
    {
        var c = WindingConstrictorFactory.Create(_alice);

        c.Name.Should().Be("Winding Constrictor");
        c.ManaCost.Should().Be("{B}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WindingConstrictor()
    {
        var card = NamedCardFactory.Create("Winding Constrictor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Winding Constrictor");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{B}{G}");
    }

    // -----------------------------------------------------------------------
    // First clause — counter bump on controlled artifact/creature
    // -----------------------------------------------------------------------

    [Fact]
    public void PlusOneCounter_OnControlledCreature_BumpsByOne()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(2, "1 requested + 1 from Winding Constrictor");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void Counter_OnControlledArtifact_BumpsByOne()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);

        var artifact = new Artifact("Walking Ballista", "{X}{X}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(artifact, CounterType.PlusOnePlusOne, 2, bus);

        placed.Should().Be(3, "Winding Constrictor applies to artifacts you control, not just creatures");
        artifact.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public void NonPlusOneCounterKind_OnControlledArtifact_BumpsThatKind()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);

        var artifact = new Artifact("Aether Hub", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(artifact, CounterType.Charge, 1, bus);

        placed.Should().Be(2, "'that many plus one of each of those kinds' — charge counters too");
        artifact.Counters.Count(CounterType.Charge).Should().Be(2);
    }

    [Fact]
    public void TwoConstrictors_StackForPlusTwo()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(3, "1 + 1 (first) + 1 (second) — CR 616.1c, each fires once");
    }

    [Fact]
    public void StacksWithHardenedScales()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);
        var scales = HardenedScalesFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(scales);
        scales.SetZone(ZoneType.Battlefield);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(3, "1 + 1 (Constrictor) + 1 (Hardened Scales) — both fire once on the +1/+1 intent");
    }

    // -----------------------------------------------------------------------
    // Scoping
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCreature_NotBumped()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);

        var bobBear = ControlledCreature(_bob);

        var placed = CountersService.Add(bobBear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(1, "Winding Constrictor is one-sided ('you control')");
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Enchantment_NotBumped()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(WindingConstrictorFactory.Create(_alice, bus), _alice);

        var enchant = new Enchantment("Some Enchantment", "{G}", supertypes: null, subtypes: null);
        enchant.SetOwner(_alice);
        enchant.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(enchant);
        enchant.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(enchant, CounterType.Charge, 1, bus);

        placed.Should().Be(1, "the replacement is scoped to artifacts or creatures only");
        enchant.Counters.Count(CounterType.Charge).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Lifecycle / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Constrictor_OffBattlefield_DoesNotBump()
    {
        var bus = new ReplacementBus();
        // Create with a bus but leave the Constrictor off the battlefield.
        WindingConstrictorFactory.Create(_alice, bus);

        var bear = ControlledCreature(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);
        placed.Should().Be(1, "Winding Constrictor must be on the battlefield to fire");
    }

    [Fact]
    public void SingleArgFactory_DoesNotRegisterReplacement()
    {
        var c = WindingConstrictorFactory.Create(_alice);
        c.Should().NotBeNull();
        c.Name.Should().Be("Winding Constrictor");
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

    private static void PlaceOnBattlefield(Creature constrictor, Player owner)
    {
        owner.Zones.Battlefield.AddCard(constrictor);
        constrictor.SetZone(ZoneType.Battlefield);
    }
}
