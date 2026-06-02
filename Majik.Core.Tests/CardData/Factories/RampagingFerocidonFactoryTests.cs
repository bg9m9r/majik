using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RampagingFerocidonFactory"/> (Ixalan, {2}{R}).
///
/// Creature — Dinosaur 3/3. Oracle text (verified against Scryfall):
///   "Menace
///    Players can't gain life.
///    Whenever another creature enters, this creature deals 1 damage to
///    that creature's controller."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch (Creature, Dinosaur,
///     {2}{R}, 3/3).
///   - Menace keyword marker (CR 702.111) — read by CombatAbilities.HasMenace.
///   - "Players can't gain life" replacement zeros every GainLife while the
///     bus is attached (CR 119.6 / 614).
///   - The another-creature-ETB ping deals 1 damage to the ENTERING
///     creature's controller (CR 603.6e) — both for the Ferocidon
///     controller's own creatures and for an opponent's, and self-entry
///     does NOT fire ("another creature").
/// </summary>
[Trait("Color", "R")]
public class RampagingFerocidonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity / dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_HasCreatureShape_TwoR_ThreeThreeDinosaur()
    {
        var ferocidon = RampagingFerocidonFactory.Create(_alice);

        ferocidon.Should().BeOfType<Creature>();
        ferocidon.Name.Should().Be("Rampaging Ferocidon");
        ferocidon.ManaCost.Should().Be("{2}{R}");
        ferocidon.HasType(CardType.Creature).Should().BeTrue();
        ferocidon.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        ferocidon.BasePower.Should().Be(3);
        ferocidon.BaseToughness.Should().Be(3);
        ferocidon.Owner.Should().BeSameAs(_alice);
        ferocidon.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasMenace()
    {
        var ferocidon = RampagingFerocidonFactory.Create(_alice);

        // CR 702.111 — Menace marker, read by the combat declaration rules.
        CombatAbilities.HasMenace(ferocidon).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // "Players can't gain life" replacement
    // -------------------------------------------------------------------------

    [Fact]
    public void LifeGainReplacement_BlocksGainLifeOnEveryPlayer()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);
        _bob.AttachReplacementBus(bus);

        RampagingFerocidonFactory.Create(_alice, triggers: null, replacements: bus);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        _alice.GainLife(5);
        _bob.GainLife(7);

        _alice.LifeTotal.Should().Be(aliceLifeBefore, "gain rewritten to zero");
        _bob.LifeTotal.Should().Be(bobLifeBefore, "symmetric — Bob's gain zeros too");
    }

    [Fact]
    public void LifeGainReplacement_OmittedWhenNoBus_GainsNormally()
    {
        // Single-arg dispatcher posture: no replacement bus wired — the
        // static silently no-ops (mirrors Sulfuric Vortex / Roiling Vortex).
        RampagingFerocidonFactory.Create(_alice);

        var aliceLifeBefore = _alice.LifeTotal;
        _alice.GainLife(5);

        _alice.LifeTotal.Should().Be(aliceLifeBefore + 5);
    }

    // -------------------------------------------------------------------------
    // Another-creature-ETB ping
    // -------------------------------------------------------------------------

    [Fact]
    public void EtbPing_OwnCreatureEnters_DealsOneToController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ferocidon = RampagingFerocidonFactory.Create(_alice, triggers, replacements: null);
        ferocidon.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ferocidon);

        var aliceLifeBefore = _alice.LifeTotal;

        // Another creature Alice controls enters.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(grizzly, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "another creature entered");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1,
            "the entering creature's controller (Alice) takes 1 damage");
    }

    [Fact]
    public void EtbPing_OpponentCreatureEnters_DealsOneToThatController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ferocidon = RampagingFerocidonFactory.Create(_alice, triggers, replacements: null);
        ferocidon.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ferocidon);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        // A creature Bob controls enters — Bob is "that creature's controller".
        var goblin = new Creature("Goblin", "{R}", 1, 1);
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        goblin.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(goblin, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "another creature entered (under Bob)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 1, "Bob is the entering creature's controller");
        _alice.LifeTotal.Should().Be(aliceLifeBefore, "Alice is untouched — not that controller");
    }

    [Fact]
    public void EtbPing_DoesNotFireOnSelfEntry()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ferocidon = RampagingFerocidonFactory.Create(_alice, triggers, replacements: null);
        ferocidon.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ferocidon);

        // The Ferocidon ITSELF entering — "another creature" excludes self.
        bus.Publish(new CardMovedEvent(ferocidon, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0, "the source itself entering is not 'another creature'");
    }

    [Fact]
    public void EtbPing_DoesNotFireForNoncreaturePermanents()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ferocidon = RampagingFerocidonFactory.Create(_alice, triggers, replacements: null);
        ferocidon.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ferocidon);

        var artifact = new Artifact("Mind Stone", "{2}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        artifact.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(artifact, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0, "only another CREATURE entering matters");
    }
}
