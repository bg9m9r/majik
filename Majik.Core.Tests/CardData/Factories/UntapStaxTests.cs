using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end coverage for the untap-stax cycle riding the count-cap
/// extension to <see cref="UntapStepRestrictions"/> (CR 502.1):
///
/// <list type="bullet">
///   <item><b>Stasis</b> — MaxCount = 0 on a match-everything filter
///         (functionally "skip the untap step"); upkeep pay-{U}-or-
///         sacrifice trigger.</item>
///   <item><b>Static Orb</b> — MaxCount = 2, conditional gate on the
///         orb's own tap state, no filter restriction.</item>
///   <item><b>Winter Orb</b> — MaxCount = 1, conditional gate, Land
///         filter.</item>
///   <item><b>Smoke</b> — MaxCount = 1, unconditional, Creature filter.</item>
/// </list>
///
/// Each test calls <c>UntapStepRestrictions.Clear()</c> in the ctor +
/// dispose so process-level registry state never leaks across cases.
/// </summary>
public class UntapStaxTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public UntapStaxTests()
    {
        UntapStepRestrictions.Clear();
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    public void Dispose() => UntapStepRestrictions.Clear();

    // ------------------------------------------------------------------
    // Identity + NamedCardFactory dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void Stasis_Identity()
    {
        var s = StasisFactory.Create(_alice);
        s.Name.Should().Be("Stasis");
        s.ManaCost.Should().Be("{1}{U}");
        s.HasType(CardType.Enchantment).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        // Carries the upkeep trigger even when the bus is null.
        s.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void StaticOrb_Identity()
    {
        var o = StaticOrbFactory.Create(_alice);
        o.Name.Should().Be("Static Orb");
        o.ManaCost.Should().Be("{3}");
        o.HasType(CardType.Artifact).Should().BeTrue();
        o.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WinterOrb_Identity()
    {
        var o = WinterOrbFactory.Create(_alice);
        o.Name.Should().Be("Winter Orb");
        o.ManaCost.Should().Be("{2}");
        o.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Smoke_Identity()
    {
        var s = SmokeFactory.Create(_alice);
        s.Name.Should().Be("Smoke");
        s.ManaCost.Should().Be("{1}{R}");
        s.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_DispatchesAllFourStaxCards()
    {
        NamedCardFactory.Create("Stasis", _alice).Should().BeOfType<Enchantment>();
        NamedCardFactory.Create("Static Orb", _alice).Should().BeOfType<Artifact>();
        NamedCardFactory.Create("Winter Orb", _alice).Should().BeOfType<Artifact>();
        NamedCardFactory.Create("Smoke", _alice).Should().BeOfType<Enchantment>();
    }

    // ------------------------------------------------------------------
    // Stasis — "Players skip their untap steps" + upkeep maintenance
    // ------------------------------------------------------------------

    [Fact]
    public async Task Stasis_OnBattlefield_NoPermanentUntaps_ForEitherPlayer()
    {
        var stasis = StasisFactory.Create(_alice, triggers: null, eventBus: _bus);
        PutOnBattlefield(stasis, _alice);

        // Alice tapped permanents.
        var aliceBear = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(aliceBear);
        aliceBear.Tap();
        aliceBear.HasSummoningSickness = false;

        var aliceLand = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(aliceLand);
        aliceLand.Tap();

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        aliceBear.IsTapped.Should().BeTrue("Stasis skips the entire untap step");
        aliceLand.IsTapped.Should().BeTrue("Stasis skips the entire untap step");

        // Bob's turn — same gate applies to Bob.
        var bobBear = new Creature("Bear", "1G", 2, 2)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.Tap();
        bobBear.HasSummoningSickness = false;

        SeedLibrary(_bob, 3);
        await driver.RunTurnAsync(_bob, turnNumber: 3);
        bobBear.IsTapped.Should().BeTrue("Stasis is symmetric — Bob's untap step is also skipped");
    }

    [Fact]
    public void Stasis_Upkeep_PaysU_StaysOnBattlefield()
    {
        var stasis = StasisFactory.Create(_alice, triggers: null, eventBus: _bus);
        PutOnBattlefield(stasis, _alice);

        // Pre-stage {U}; the trigger auto-pays.
        _alice.AddManaToPool(ManaCost.Parse("U"));

        var trigger = stasis.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stasis.Zone.Should().Be(ZoneType.Battlefield, "Alice paid {U} — Stasis is not sacrificed");
        _alice.Zones.Battlefield.GetCards().Should().Contain(stasis);
        _alice.ManaPool.Total.Should().Be(0, "PayMana({U}) consumed the pre-staged mana");
    }

    [Fact]
    public void Stasis_Upkeep_CantPay_SacrificesStasis()
    {
        var stasis = StasisFactory.Create(_alice, triggers: null, eventBus: _bus);
        PutOnBattlefield(stasis, _alice);

        var trigger = stasis.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stasis.Zone.Should().Be(ZoneType.Graveyard, "Alice couldn't pay {U} — Stasis is sacrificed");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(stasis);
        _alice.Zones.Graveyard.GetCards().Should().Contain(stasis);
    }

    // ------------------------------------------------------------------
    // Static Orb — cap of 2, conditional on orb being untapped
    // ------------------------------------------------------------------

    [Fact]
    public async Task StaticOrb_Untapped_AllowsAtMostTwoPermanentsToUntap()
    {
        var orb = StaticOrbFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(orb, _alice);
        // Static Orb itself isn't tapped — cap is active.

        var p1 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var p2 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var p3 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        foreach (var p in new[] { p1, p2, p3 })
        {
            _alice.Zones.Battlefield.AddCard(p);
            p.Tap();
        }

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        // Exactly two untapped (cap of 2). Selection order: printed iteration.
        var untappedCount = new[] { p1, p2, p3 }.Count(p => !p.IsTapped);
        untappedCount.Should().Be(2, "Static Orb caps untap to 2 permanents");
    }

    [Fact]
    public async Task StaticOrb_Tapped_DoesNotApplyCap()
    {
        var orb = StaticOrbFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(orb, _alice);
        // Tap the orb itself — cap becomes dormant.
        orb.Tap();

        var p1 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var p2 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var p3 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        foreach (var p in new[] { p1, p2, p3 })
        {
            _alice.Zones.Battlefield.AddCard(p);
            p.Tap();
        }

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        // Orb itself was tapped — the cap is inactive, so all three untap.
        // Static Orb also untaps (it has no skip predicate of its own).
        new[] { p1, p2, p3 }.Count(p => p.IsTapped).Should().Be(0,
            "Static Orb is tapped so the cap is inactive — all permanents untap");
    }

    // ------------------------------------------------------------------
    // Winter Orb — cap of 1 on lands, conditional on orb being untapped
    // ------------------------------------------------------------------

    [Fact]
    public async Task WinterOrb_Untapped_CapsLandsToOne_NonLandsUnrestricted()
    {
        var orb = WinterOrbFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(orb, _alice);

        // Three tapped lands + a tapped creature on Alice's side.
        var l1 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var l2 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var l3 = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var bear = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        foreach (var p in new Permanent[] { l1, l2, l3, bear })
        {
            _alice.Zones.Battlefield.AddCard(p);
            p.Tap();
        }
        bear.HasSummoningSickness = false;

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        // Exactly one land untapped; the bear untaps freely (not a land).
        new[] { l1, l2, l3 }.Count(l => !l.IsTapped).Should().Be(1,
            "Winter Orb caps land untaps to 1");
        bear.IsTapped.Should().BeFalse("Winter Orb's cap is restricted to lands — creatures untap freely");
    }

    // ------------------------------------------------------------------
    // Smoke — cap of 1 on creatures, unconditional
    // ------------------------------------------------------------------

    [Fact]
    public async Task Smoke_CapsCreaturesToOne_NonCreaturesUnrestricted()
    {
        var smoke = SmokeFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(smoke, _alice);

        // Three tapped creatures + a tapped land.
        var c1 = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var c2 = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var c3 = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var land = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        foreach (var p in new Permanent[] { c1, c2, c3, land })
        {
            _alice.Zones.Battlefield.AddCard(p);
            p.Tap();
        }
        foreach (var c in new[] { c1, c2, c3 }) c.HasSummoningSickness = false;

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        new[] { c1, c2, c3 }.Count(c => !c.IsTapped).Should().Be(1,
            "Smoke caps creature untaps to 1");
        land.IsTapped.Should().BeFalse("Smoke's cap is restricted to creatures — lands untap freely");
    }

    // ------------------------------------------------------------------
    // Lifecycle — LTB lifts the cap
    // ------------------------------------------------------------------

    [Fact]
    public async Task Smoke_AfterLeavingBattlefield_CapLifts()
    {
        var smoke = SmokeFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(smoke, _alice);

        // Smoke leaves — registration must lift.
        smoke.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(smoke, ZoneType.Battlefield, ZoneType.Graveyard));

        var c1 = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var c2 = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        foreach (var c in new[] { c1, c2 })
        {
            _alice.Zones.Battlefield.AddCard(c);
            c.Tap();
            c.HasSummoningSickness = false;
        }

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        c1.IsTapped.Should().BeFalse("Smoke left the battlefield — cap lifted");
        c2.IsTapped.Should().BeFalse("Smoke left the battlefield — cap lifted");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void PutOnBattlefield(Permanent p, Player controller)
    {
        p.Zone = ZoneType.Battlefield;
        controller.Zones.Battlefield.AddCard(p);
        _bus.Publish(new CardMovedEvent(p, ZoneType.Hand, ZoneType.Battlefield));
    }

    private TurnDriver NewDriver()
    {
        IPlayerAgent agent = new DeterministicBotAgent();
        return new TurnDriver(
            players: new[] { _alice, _bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = agent,
                [_bob] = new DeterministicBotAgent(),
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: new CombatFlow(_bus, _sba),
            eventBus: _bus);
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
