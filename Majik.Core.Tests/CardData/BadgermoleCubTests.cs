using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BadgermoleCubFactory"/>.
///
/// Badgermole Cub — Creature — Bear {G} 1/1.
/// "When this creature enters, earthbend 1." (+ a deferred tap-for-mana clause).
///
/// Covers card identity, the ETB Earthbend-1 trigger shape (1..1 "target land
/// you control"), and resolution: the chosen land gets a +1/+1 counter and is
/// animated into a 1/1 Elemental creature with haste that's still a land.
/// </summary>
public class BadgermoleCubTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BadgermoleCub_Identity()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.Name.Should().Be("Badgermole Cub");
        b.HasType(CardType.Creature).Should().BeTrue();
        b.HasSubtype(CardSubtype.Bear).Should().BeTrue("Badgermole Cub is a Bear");
        b.BasePower.Should().Be(1);
        b.BaseToughness.Should().Be(1);
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BadgermoleCub_DispatchesViaNamedCardFactory()
    {
        var b = NamedCardFactory.Create("Badgermole Cub", _alice);

        b.Should().BeOfType<Creature>();
        b.Name.Should().Be("Badgermole Cub");
        b.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "the source-gen dispatcher routes through Create(owner), attaching the ETB Earthbend trigger and the tap-a-creature-for-mana trigger");
    }

    [Fact]
    public void BadgermoleCub_HasEarthbendEtbTrigger()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        var etb = b.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("land", "Earthbend targets a land you control");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void BadgermoleCub_EtbTargetGatherer_OnlyControllersLands()
    {
        var bob = new Player("Bob", 20);
        var myForest = MakeLandFor(_alice, "Forest");
        var oppForest = MakeLandFor(bob, "Forest");

        var b = BadgermoleCubFactory.Create(_alice);
        var etb = b.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);
        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            _alice, new[] { _alice, bob }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain, stack);

        var candidates = etb.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(myForest, "you can target a land you control");
        candidates.Should().NotContain(oppForest, "Earthbend can't target an opponent's land");
    }

    [Fact]
    public void BadgermoleCub_EtbResolution_AnimatesChosenLandToOneOne()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeLandFor(_alice, "Forest", svc);

        var b = BadgermoleCubFactory.Create(_alice);
        b.ActiveEffects = svc; // prod build wires the creature's CES
        var etb = b.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { forest } });
        foreach (var effect in etb.Effects) effect.Execute();

        forest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Earthbend 1 puts one +1/+1 counter on the land (CR 701.59b)");

        var chars = svc.Compute(forest);
        chars.Should().BeOfType<CreatureCharacteristics>();
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Land, "still a land (CR 701.59a)");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
        chars.Keywords.Should().Contain("Haste");

        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(1, "0/0 base + one +1/+1 counter = 1/1");
        cc.Toughness.Should().Be(1);

        forest.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the return-tapped delayed trigger is attached to the land (CR 701.59c)");
    }

    [Fact]
    public void BadgermoleCub_EtbResolution_NoTarget_IsNoOp()
    {
        var svc = new ContinuousEffectsService();
        var b = BadgermoleCubFactory.Create(_alice);
        b.ActiveEffects = svc;
        var etb = b.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no chosen target → no-op");
    }

    // -----------------------------------------------------------------------
    // "Whenever you tap a creature for mana, add an additional {G}."
    // -----------------------------------------------------------------------

    [Fact]
    public void BadgermoleCub_HasTapForManaTrigger()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        // The tap-for-mana trigger has no targets (unlike the Earthbend ETB).
        var tapTrigger = b.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        tapTrigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    /// <summary>
    /// You tap one of your creatures for mana: the mana ability adds its own
    /// pip, and Badgermole Cub's trigger adds an additional {G} to your pool.
    /// "Whenever you tap a creature for mana, add an additional {G}."
    /// </summary>
    [Fact]
    public void TappingYourCreatureForMana_AddsAdditionalGreen()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        // Badgermole Cub on the battlefield with its trigger registered.
        var cub = BadgermoleCubFactory.Create(_alice, triggers);
        cub.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cub);

        // A mana dork (Devoted Druid: {T}: add {G}) you control.
        var druid = (Creature)NamedCardFactory.Create("Devoted Druid", _alice);
        druid.SetController(_alice);
        druid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(druid);
        druid.ClearSummoningSickness(); // controlled since start of turn (CR 302.6)

        var manaAbility = druid.Abilities.OfType<IManaAbility>().First();

        activator.ActivateManaAbility(manaAbility, _alice);

        // The druid's own {G} is in the pool immediately; the trigger is
        // pending (CR 605.3 — the dork's ability doesn't use the stack).
        _alice.ManaPool.Green.Should().Be(1, "the dork's own {G}");
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(2,
            "Badgermole Cub adds an additional {G} (CR 605.1b)");
    }

    /// <summary>
    /// The clause is "Whenever YOU tap a creature for mana" — only the cub's
    /// controller tapping a creature counts. An opponent tapping their own
    /// creature does not trigger.
    /// </summary>
    [Fact]
    public void OpponentTappingTheirCreature_DoesNotTrigger()
    {
        var (bus, stack, triggers, activator) = BuildEngine();
        var bob = new Player("Bob", 20);

        var cub = BadgermoleCubFactory.Create(_alice, triggers);
        cub.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cub);

        var bobDruid = (Creature)NamedCardFactory.Create("Devoted Druid", bob);
        bobDruid.SetController(bob);
        bobDruid.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobDruid);
        bobDruid.ClearSummoningSickness();

        var manaAbility = bobDruid.Abilities.OfType<IManaAbility>().First();

        activator.ActivateManaAbility(manaAbility, bob);

        triggers.PendingCount.Should().Be(0, "only YOU tapping a creature triggers it");
        _alice.ManaPool.Green.Should().Be(0);
    }

    /// <summary>
    /// "Whenever you tap a CREATURE for mana" — tapping a land for mana does
    /// not trigger, even though it's your land.
    /// </summary>
    [Fact]
    public void TappingYourLandForMana_DoesNotTrigger()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var cub = BadgermoleCubFactory.Create(_alice, triggers);
        cub.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cub);

        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _alice);

        triggers.PendingCount.Should().Be(0, "a land isn't a creature");
        _alice.ManaPool.Green.Should().Be(1, "only the Forest's own {G}");
    }

    private static (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers, ManaAbilityActivator activator) BuildEngine()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var activator = new ManaAbilityActivator(bus);
        return (bus, stack, triggers, activator);
    }

    private Land MakeLandFor(Player p, string name, ContinuousEffectsService? svc = null)
    {
        var land = new Land(name) { Owner = p, Controller = p, Zone = ZoneType.Battlefield };
        if (svc != null) land.ActiveEffects = svc;
        p.Zones.Battlefield.AddCard(land);
        return land;
    }
}
