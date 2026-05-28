using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FlickerwispFactory"/>.
///
/// Covers:
/// - Identity ({1}{W}{W} Creature — Elemental, 3/1, white, mana value 3).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying keyword marker (CR 702.9).
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB target gatherer: creatures, artifacts, enchantments, and lands are
///   all legal targets ("another target permanent", no type filter).
/// - ETB "another" filter: Flickerwisp itself is NOT a legal target (CR 115.5b).
/// - ETB effect: the chosen permanent is exiled immediately.
/// - Delayed return: at the beginning of the next end step, the exiled
///   permanent returns to the battlefield under its OWNER's control (CR 603.7).
/// - Owner-vs-controller distinction: a permanent returned goes to its owner's
///   battlefield, not the current controller's (important for stolen permanents).
/// </summary>
public class FlickerwispFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_Identity()
    {
        var c = FlickerwispFactory.Create(_alice);

        c.Name.Should().Be("Flickerwisp");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue("Flickerwisp is an Elemental");
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Flickerwisp_IsWhite()
    {
        var c = FlickerwispFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White,
            "Flickerwisp has {W}{W} pips in its mana cost");
        colors.Should().HaveCount(1, "only one color identity");
    }

    [Fact]
    public void Flickerwisp_ManaValue_IsThree()
    {
        var c = FlickerwispFactory.Create(_alice);

        // {1}{W}{W} = mana value 3 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(3, "CR 202.3 — {1}{W}{W} has mana value 3");
    }

    [Fact]
    public void Flickerwisp_HasFlyingKeyword()
    {
        var c = FlickerwispFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Flickerwisp has Flying");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Flickerwisp", _alice);

        c.Should().BeOfType<Creature>("Flickerwisp is a Creature");
        c.Name.Should().Be("Flickerwisp");
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{W}{W}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = FlickerwispFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // Target gatherer — all permanent types are legal (no type filter)
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_TargetGatherer_AllPermanentTypesAreLegal()
    {
        // Populate Alice's and Bob's battlefields with one of each permanent type.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var artifact = new Artifact("Sol Ring", "{1}");
        var enchantment = new Enchantment("Pacifism", "{1}{W}");
        var land = new Land("Forest");
        var planeswalker = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3);

        foreach (var perm in new Permanent[] { creature, artifact, enchantment, land, planeswalker })
        {
            perm.SetOwner(_alice);
            perm.SetController(_alice);
            perm.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(perm);
        }

        // Create Flickerwisp itself and place it on the battlefield.
        var wisp = FlickerwispFactory.Create(_alice);
        wisp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wisp);

        var etb = wisp.Abilities.OfType<TriggeredAbility>().Single();
        var request = etb.TargetRequests.Single();

        // Build a minimal GameContext with both players so AllPlayers spans both sides.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);
        var candidates = request.CandidateGatherer!(ctx).OfType<Permanent>().ToList();

        // All five permanents should be gatherable.
        candidates.Should().Contain(creature, "creatures are legal targets");
        candidates.Should().Contain(artifact, "artifacts are legal targets");
        candidates.Should().Contain(enchantment, "enchantments are legal targets");
        candidates.Should().Contain(land, "lands are legal targets");
        candidates.Should().Contain(planeswalker, "planeswalkers are legal targets");

        // Flickerwisp itself must NOT appear ("another" — CR 115.5b).
        candidates.Should().NotContain(wisp,
            "CR 115.5b — 'another' excludes Flickerwisp itself");
    }

    // -----------------------------------------------------------------------
    // ETB effect: chosen permanent is exiled
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_EtbEffect_ExilesChosenPermanent()
    {
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var wisp = FlickerwispFactory.Create(_alice);
        var etb = wisp.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        grizzly.Zone.Should().Be(ZoneType.Exile,
            "Flickerwisp ETB exiles the target permanent");
        _alice.Zones.Exile.GetCards().Should().Contain(grizzly,
            "the exiled card is in the owner's exile zone");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(grizzly);
    }

    // -----------------------------------------------------------------------
    // ETB self-target guard: Flickerwisp cannot target itself ("another")
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_CannotTargetItself()
    {
        var wisp = FlickerwispFactory.Create(_alice);
        wisp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wisp);

        var etb = wisp.Abilities.OfType<TriggeredAbility>().Single();
        // Force self-target — should no-op at resolution.
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { wisp },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow();

        // Flickerwisp must remain on the battlefield — the effect no-ops.
        wisp.Zone.Should().Be(ZoneType.Battlefield,
            "CR 115.5b — 'another' prevents Flickerwisp from targeting itself");
    }

    // -----------------------------------------------------------------------
    // Delayed return: at next end step, permanent returns to owner's battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_DelayedReturn_ReturnsToOwnersBattlefield_AtNextEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice's creature to blink.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var wisp = FlickerwispFactory.Create(_alice, eventBus: bus, triggers: triggers);
        var etb = wisp.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        // Immediately after ETB: grizzly must be exiled.
        grizzly.Zone.Should().Be(ZoneType.Exile,
            "Flickerwisp ETB exiles the target immediately");

        // Fire the next end step — delayed trigger should enqueue.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "delayed return rider fires on the first end step after the ETB");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        grizzly.Zone.Should().Be(ZoneType.Battlefield,
            "the exiled permanent returns at the beginning of the next end step (CR 603.7)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(grizzly,
            "card returns to its owner's battlefield");
        _alice.Zones.Exile.GetCards().Should().NotContain(grizzly);
        grizzly.Controller.Should().BeSameAs(_alice,
            "returns under owner's control (CR 108.3 / CR 614)");
    }

    // -----------------------------------------------------------------------
    // Owner-vs-controller: stolen permanent returns to OWNER, not thief
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_DelayedReturn_StolenPermanent_ReturnsToOwner_NotThief()
    {
        // Simulate a "stolen" permanent: Bob owns it, but Alice controls it.
        // In this engine, zone containers are keyed by owner: the card lives in
        // Bob's battlefield zone even though Alice is the controller.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var stolen = new Creature("Act of Treason Target", "{R}", 2, 2);
        stolen.SetOwner(_bob);          // Bob is the owner.
        stolen.SetController(_alice);   // Alice stole control (e.g. Act of Treason).
        stolen.SetZone(ZoneType.Battlefield);
        // Owner-keyed zone: the card lives in Bob's battlefield zone.
        _bob.Zones.Battlefield.AddCard(stolen);

        var wisp = FlickerwispFactory.Create(_alice, eventBus: bus, triggers: triggers);
        var etb = wisp.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { stolen },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        // The permanent should have moved to Bob's exile (owner's exile).
        stolen.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(stolen,
            "the exiled card is in the owner's (Bob's) exile zone");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(stolen);

        // Fire the next end step.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "delayed return fires on the first end step");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        // Must return to Bob's battlefield (owner), not Alice's.
        stolen.Zone.Should().Be(ZoneType.Battlefield,
            "the stolen permanent returns to the battlefield (CR 603.7)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(stolen,
            "CR 108.3 — 'under its owner's control' means Bob gets it back");
        stolen.Controller.Should().BeSameAs(_bob,
            "controller set to owner on return (CR 614)");
    }

    // -----------------------------------------------------------------------
    // Wired path: bus event triggers ETB
    // -----------------------------------------------------------------------

    [Fact]
    public void Flickerwisp_WiredCreate_EnteringBattlefield_ExilesTriggerTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        // Target permanent to exile.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var wisp = FlickerwispFactory.Create(_alice, eventBus: bus, triggers: triggerManager);

        // Pre-set target on the ETB trigger before it fires via the bus.
        var etb = wisp.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        wisp.SetZone(ZoneType.Battlefield);
        var moveEvent = new CardMovedEvent(wisp, ZoneType.Hand, ZoneType.Battlefield);
        bus.Publish(moveEvent);

        triggerManager.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            item?.Resolve();
        }

        grizzly.Zone.Should().Be(ZoneType.Exile,
            "entering the battlefield via the bus exiles the target permanent end-to-end");
    }
}
