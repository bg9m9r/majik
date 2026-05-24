using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Roiling Vortex (Zendikar Rising, {R}).
///
/// Oracle:
///   "At the beginning of your upkeep, Roiling Vortex deals 1 damage to
///    each player.
///    Whenever a player casts a spell, if no mana was spent to cast it,
///    Roiling Vortex deals 3 damage to that player.
///    {1}{R}, Sacrifice Roiling Vortex: Roiling Vortex deals 3 damage to
///    any target.
///    Players can't gain life."
///
/// Coverage:
/// - Identity / shape / NamedCardFactory dispatch.
/// - Upkeep trigger drains 1 from each player (allPlayersResolver
///   path) AND controller-only fallback.
/// - Free-cast trigger fires on a Spell with WasFreeCast=true.
/// - Free-cast trigger does NOT fire on a normal (mana-paid) Spell.
/// - {1}{R}, Sacrifice activated ability deals 3 to any target
///   (Player + Creature).
/// - Life-gain replacement zeroes Player.GainLife while attached.
/// </summary>
public class RoilingVortexFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasEnchantmentShape_RedCost()
    {
        var vortex = RoilingVortexFactory.Create(_alice);

        vortex.Should().BeOfType<Enchantment>();
        vortex.Name.Should().Be("Roiling Vortex");
        vortex.HasType(CardType.Enchantment).Should().BeTrue();
        vortex.ManaCost.Should().Be("{R}");
        vortex.ManaCostValue.TotalValue.Should().Be(1);
        vortex.Owner.Should().BeSameAs(_alice);
        vortex.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_AttachesAllAbilities_ForShapeObservability()
    {
        var vortex = RoilingVortexFactory.Create(_alice);

        // Two triggered abilities (upkeep ping + free-cast ping) + one
        // activated ability ({1}{R}, Sac: 3 damage). The static "players
        // can't gain life" lives on the ReplacementBus, not on the card.
        vortex.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
        vortex.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsRoilingVortexShape()
    {
        var dispatched = NamedCardFactory.Create("Roiling Vortex", _alice);

        dispatched.Should().BeOfType<Enchantment>();
        dispatched.Name.Should().Be("Roiling Vortex");
        dispatched.ManaCost.Should().Be("{R}");
        dispatched.HasType(CardType.Enchantment).Should().BeTrue();
        dispatched.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
        dispatched.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Upkeep trigger — 1 damage to each player
    // -----------------------------------------------------------------------

    [Fact]
    public void UpkeepTrigger_AllPlayersResolverSupplied_DrainsEveryPlayerOne()
    {
        var vortex = RoilingVortexFactory.Create(
            _alice,
            triggers: null,
            replacements: null,
            allPlayersResolver: () => new[] { _alice, _bob });

        vortex.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(vortex);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        // Resolve the upkeep trigger directly — execute its effect chain.
        var upkeep = vortex.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
        foreach (var e in upkeep.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1,
            "Roiling Vortex is symmetric — controller takes 1 too");
        _bob.LifeTotal.Should().Be(bobLifeBefore - 1,
            "Every player at the table takes 1 damage");
    }

    [Fact]
    public void UpkeepTrigger_NoResolver_ControllerOnlyFallback()
    {
        // Single-arg dispatcher posture: no allPlayersResolver wired.
        // Mirrors Pernicious Deed / Meathook Massacre's "scan controller-
        // only" fallback. Bob's life is untouched (defensive — keeps
        // shape-only tests from doing the wrong thing silently).
        var vortex = RoilingVortexFactory.Create(_alice);

        vortex.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(vortex);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        var upkeep = vortex.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
        foreach (var e in upkeep.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1);
        _bob.LifeTotal.Should().Be(bobLifeBefore, "no resolver → controller-only ping");
    }

    [Fact]
    public void UpkeepTrigger_LiveBus_RegistersPendingTrigger_OnControllerUpkeepOnly()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vortex = RoilingVortexFactory.Create(_alice, triggers, replacements: null, allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(vortex);
        vortex.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — Alice's Vortex does NOT trigger (oracle: "your
        // upkeep").
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0);

        // Alice's upkeep — upkeep trigger surfaces.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Free-cast trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void FreeCastTrigger_FiresOnFreeCastSpell_AndDealsThree()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vortex = RoilingVortexFactory.Create(_alice, triggers, replacements: null, allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(vortex);
        vortex.SetZone(ZoneType.Battlefield);

        // Bob casts a free spell (cascade / suspend / Memnite / pitch).
        // Construct a stand-in spell with WasFreeCast=true.
        var memnite = new Card("Memnite", "{0}");
        memnite.SetOwner(_bob);
        var freeSpell = new Majik.Core.Spells.Spell(memnite, _bob) { WasFreeCast = true };

        var bobLifeBefore = _bob.LifeTotal;
        var aliceLifeBefore = _alice.LifeTotal;

        bus.Publish(new SpellCastEvent(freeSpell));
        triggers.PendingCount.Should().Be(1, "free cast satisfies WasFreeCast — trigger queues");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 3,
            "the player who cast the free spell takes 3 damage");
        _alice.LifeTotal.Should().Be(aliceLifeBefore,
            "non-casting players are unaffected");
    }

    [Fact]
    public void FreeCastTrigger_DoesNotFireOnNormalCast()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vortex = RoilingVortexFactory.Create(_alice, triggers, replacements: null, allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(vortex);
        vortex.SetZone(ZoneType.Battlefield);

        // Bob casts Lightning Bolt — a normal, mana-paid spell.
        // Spell.WasFreeCast defaults to false; predicate rejects.
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        var paidSpell = new Majik.Core.Spells.Spell(bolt, _bob);

        var bobLifeBefore = _bob.LifeTotal;

        bus.Publish(new SpellCastEvent(paidSpell));

        triggers.PendingCount.Should().Be(0,
            "WasFreeCast=false — Roiling Vortex's free-cast trigger ignores normal casts");
        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }

    // -----------------------------------------------------------------------
    // Activated ability — {1}{R}, Sacrifice: 3 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void Activated_HasManaPlusSacrificeCost_AndAnyTargetRequest()
    {
        var vortex = RoilingVortexFactory.Create(_alice);
        var act = vortex.Abilities.OfType<ActivatedAbility>().Single();

        act.Costs.Should().HaveCount(2, "mana cost + sacrifice");
        act.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        act.Costs.OfType<AdditionalCost>().Should().HaveCount(1);
        act.Costs.OfType<AdditionalCost>().Single().CostType
            .Should().Be(AdditionalCostType.Sacrifice);

        act.TargetRequests.Should().HaveCount(1);
        act.TargetRequests[0].MinTargets.Should().Be(1);
        act.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Activated_DealsThreeToPlayer_WhenChosen()
    {
        var vortex = RoilingVortexFactory.Create(_alice);
        var act = vortex.Abilities.OfType<ActivatedAbility>().Single();

        act.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        var bobLifeBefore = _bob.LifeTotal;
        foreach (var e in act.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 3);
    }

    [Fact]
    public void Activated_DealsThreeToCreature_WhenChosen()
    {
        var vortex = RoilingVortexFactory.Create(_alice);
        var act = vortex.Abilities.OfType<ActivatedAbility>().Single();

        var hippo = new Creature("Test Hippo", "{3}{G}", 4, 4);
        hippo.SetOwner(_bob);
        hippo.SetController(_bob);

        act.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { hippo },
        });

        foreach (var e in act.Effects) e.Execute();

        hippo.Damage.Should().Be(3,
            "the 3-damage activated ability hits a creature target");
    }

    [Fact]
    public void Activated_NoTargetChosen_NoOps()
    {
        var vortex = RoilingVortexFactory.Create(_alice);
        var act = vortex.Abilities.OfType<ActivatedAbility>().Single();

        // Defensive — no SetChosenTargets call. CR 608.2b: do as much as
        // you can, which is nothing.
        var bobLifeBefore = _bob.LifeTotal;

        foreach (var e in act.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }

    // -----------------------------------------------------------------------
    // Static — "Players can't gain life"
    // -----------------------------------------------------------------------

    [Fact]
    public void LifeGainReplacement_BlocksGainLifeOnPlayer()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);
        _bob.AttachReplacementBus(bus);

        // Build Roiling Vortex with the replacement registered.
        RoilingVortexFactory.Create(_alice, triggers: null, replacements: bus, allPlayersResolver: null);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        _alice.GainLife(5);
        _bob.GainLife(7);

        _alice.LifeTotal.Should().Be(aliceLifeBefore,
            "Roiling Vortex zeros every life gain — 5 → 0");
        _bob.LifeTotal.Should().Be(bobLifeBefore,
            "the 'players can't gain life' static is symmetric");
    }

    [Fact]
    public void LifeGainReplacement_OmittedWhenNoBus_GainLifeNormally()
    {
        // Single-arg dispatcher posture: replacements not wired → the
        // static silently no-ops, matching Valakut's ETB-tapped
        // single-arg fallback.
        RoilingVortexFactory.Create(_alice);

        var aliceLifeBefore = _alice.LifeTotal;
        _alice.GainLife(5);

        _alice.LifeTotal.Should().Be(aliceLifeBefore + 5,
            "no bus attached → no replacement runs; gain proceeds");
    }
}
