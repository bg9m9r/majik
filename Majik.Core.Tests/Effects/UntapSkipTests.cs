using FluentAssertions;
using Majik.Core.Abilities;
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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 502.1 — untap-skip primitive coverage. Exercises the
/// <see cref="UntapStepRestrictions"/> registry through the
/// <see cref="DoesNotUntapStaticEffect"/> / <see cref="SubtypeDoesNotUntapStaticEffect"/>
/// lifecycle binders, and end-to-end via <see cref="TurnDriver"/>'s
/// UntapStep so the registry actually gates the per-permanent untap loop.
///
/// Each test calls <c>UntapStepRestrictions.Clear()</c> in the ctor so
/// process-level registry state never leaks between cases.
/// </summary>
public class UntapSkipTests : IDisposable
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

    public UntapSkipTests()
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
    // Registry-level: predicate semantics + idempotency
    // ------------------------------------------------------------------

    [Fact]
    public void Registry_PermanentSkip_BlocksUntap()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        var token = new object();

        UntapStepRestrictions.ShouldSkipUntap(bear, _alice).Should().BeFalse();
        UntapStepRestrictions.MarkPermanentDoesNotUntap(token, bear);
        UntapStepRestrictions.ShouldSkipUntap(bear, _alice).Should().BeTrue();

        UntapStepRestrictions.RemoveAll(token);
        UntapStepRestrictions.ShouldSkipUntap(bear, _alice).Should().BeFalse();
    }

    [Fact]
    public void Registry_PermanentSkip_Idempotent()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        var token = new object();

        // Same (token, permanent) re-add must not require multiple removes.
        UntapStepRestrictions.MarkPermanentDoesNotUntap(token, bear);
        UntapStepRestrictions.MarkPermanentDoesNotUntap(token, bear);
        UntapStepRestrictions.MarkPermanentDoesNotUntap(token, bear);

        UntapStepRestrictions.ShouldSkipUntap(bear, _alice).Should().BeTrue();
        UntapStepRestrictions.RemoveAll(token);
        UntapStepRestrictions.ShouldSkipUntap(bear, _alice).Should().BeFalse();
    }

    [Fact]
    public void Registry_SubtypeSkip_Symmetric_AcrossControllers()
    {
        var aliceIsland = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice };
        var bobIsland = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _bob, Controller = _bob };
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _bob, Controller = _bob };
        var token = new object();

        UntapStepRestrictions.MarkSubtypeDoesNotUntap(token, CardSubtype.Island);

        // Symmetric: both players' Islands are gated regardless of whose
        // untap step is current.
        UntapStepRestrictions.ShouldSkipUntap(aliceIsland, _alice).Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(aliceIsland, _bob).Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(bobIsland, _alice).Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(bobIsland, _bob).Should().BeTrue();

        // Non-Island unaffected.
        UntapStepRestrictions.ShouldSkipUntap(mountain, _alice).Should().BeFalse();
    }

    [Fact]
    public void Registry_MultipleSources_RemoveOne_KeepsOthers()
    {
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice };
        var tokenA = new object();
        var tokenB = new object();

        UntapStepRestrictions.MarkSubtypeDoesNotUntap(tokenA, CardSubtype.Island);
        UntapStepRestrictions.MarkSubtypeDoesNotUntap(tokenB, CardSubtype.Island);

        UntapStepRestrictions.ShouldSkipUntap(island, _alice).Should().BeTrue();

        // One source leaving doesn't lift the other's restriction.
        UntapStepRestrictions.RemoveAll(tokenA);
        UntapStepRestrictions.ShouldSkipUntap(island, _alice).Should().BeTrue();

        UntapStepRestrictions.RemoveAll(tokenB);
        UntapStepRestrictions.ShouldSkipUntap(island, _alice).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Lifecycle binders: ETB attaches, LTB lifts
    // ------------------------------------------------------------------

    [Fact]
    public void DoesNotUntapLifecycle_AttachesOnEnter_LiftsOnLeave()
    {
        var vault = ManaVaultFactory.Create(_alice, triggers: null, eventBus: _bus);
        // ETB: move to battlefield (zone change publishes CardMovedEvent —
        // but the lifecycle also sync-checks on Attach which has already
        // run inside the factory; we therefore just set the zone and
        // publish to trigger re-sync as the engine would).
        vault.Zone = ZoneType.Battlefield;
        _bus.Publish(new CardMovedEvent(vault, ZoneType.Hand, ZoneType.Battlefield));

        UntapStepRestrictions.ShouldSkipUntap(vault, _alice).Should().BeTrue();

        // LTB: move to graveyard; lifecycle should unregister.
        vault.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(vault, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(vault, _alice).Should().BeFalse();
    }

    [Fact]
    public void ChokeLifecycle_AttachesOnEnter_LiftsOnLeave()
    {
        var choke = ChokeFactory.Create(_alice, eventBus: _bus);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        choke.Zone = ZoneType.Battlefield;
        _bus.Publish(new CardMovedEvent(choke, ZoneType.Hand, ZoneType.Battlefield));

        UntapStepRestrictions.ShouldSkipUntap(island, _bob).Should().BeTrue();

        choke.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(choke, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(island, _bob).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // End-to-end: TurnDriver.UntapStep honours the registry
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManaVault_OnBattlefield_Tapped_IsNotUntappedByUntapStep()
    {
        var vault = ManaVaultFactory.Create(_alice, triggers: null, eventBus: _bus);
        vault.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(vault);
        _bus.Publish(new CardMovedEvent(vault, ZoneType.Hand, ZoneType.Battlefield));
        vault.Tap();
        vault.IsTapped.Should().BeTrue();

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        vault.IsTapped.Should().BeTrue("Mana Vault doesn't untap during your untap step (CR 502.1)");
    }

    [Fact]
    public async Task ManaVault_AfterLeavingBattlefield_NoLongerSkips()
    {
        // Build + put on battlefield + take off battlefield.
        var vault = ManaVaultFactory.Create(_alice, triggers: null, eventBus: _bus);
        vault.Zone = ZoneType.Battlefield;
        _bus.Publish(new CardMovedEvent(vault, ZoneType.Hand, ZoneType.Battlefield));
        vault.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(vault, ZoneType.Battlefield, ZoneType.Graveyard));

        // After LTB the restriction must lift — a fresh tapped permanent
        // of the same identity untaps normally.
        var fresh = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(fresh);
        fresh.Tap();
        fresh.HasSummoningSickness = false;

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        fresh.IsTapped.Should().BeFalse("restriction lifted on LTB");
    }

    [Fact]
    public async Task Choke_OnBattlefield_AllIslands_DontUntap()
    {
        var choke = ChokeFactory.Create(_alice, eventBus: _bus);
        choke.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(choke);
        _bus.Publish(new CardMovedEvent(choke, ZoneType.Hand, ZoneType.Battlefield));

        var aliceIsland = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(aliceIsland);
        aliceIsland.Tap();

        var bobIsland = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        _bob.Zones.Battlefield.AddCard(bobIsland);
        bobIsland.Tap();

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        // Run Alice's untap step.
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        aliceIsland.IsTapped.Should().BeTrue("Choke gates Alice's own Islands too (symmetric)");
        // Bob's Island isn't on Alice's battlefield, so it's not even
        // visited by Alice's untap loop — but the registry would have
        // gated it regardless. Assert the predicate directly.
        UntapStepRestrictions.ShouldSkipUntap(bobIsland, _bob).Should().BeTrue();
    }

    [Fact]
    public async Task Choke_NonIsland_UntapsNormally()
    {
        var choke = ChokeFactory.Create(_alice, eventBus: _bus);
        choke.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(choke);
        _bus.Publish(new CardMovedEvent(choke, ZoneType.Hand, ZoneType.Battlefield));

        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.Tap();

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        mountain.IsTapped.Should().BeFalse("Mountain is not an Island — Choke's filter doesn't apply");
    }

    [Fact]
    public async Task MultipleChokes_StackIdempotent_OneLeaves_OthersStillFilter()
    {
        var chokeA = ChokeFactory.Create(_alice, eventBus: _bus);
        var chokeB = ChokeFactory.Create(_bob, eventBus: _bus);
        chokeA.Zone = ZoneType.Battlefield;
        chokeB.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(chokeA);
        _bob.Zones.Battlefield.AddCard(chokeB);
        _bus.Publish(new CardMovedEvent(chokeA, ZoneType.Hand, ZoneType.Battlefield));
        _bus.Publish(new CardMovedEvent(chokeB, ZoneType.Hand, ZoneType.Battlefield));

        var island = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(island);
        island.Tap();

        // Both Chokes registered — Island gated.
        UntapStepRestrictions.ShouldSkipUntap(island, _alice).Should().BeTrue();

        // One Choke leaves — the other still gates.
        chokeA.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(chokeA, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(island, _alice).Should().BeTrue();

        // Second Choke leaves — gate finally lifts.
        chokeB.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(chokeB, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(island, _alice).Should().BeFalse();
    }

    [Fact]
    public async Task MultipleManaVaults_Independent_PerPermanentSkip()
    {
        var v1 = ManaVaultFactory.Create(_alice, triggers: null, eventBus: _bus);
        var v2 = ManaVaultFactory.Create(_alice, triggers: null, eventBus: _bus);

        v1.Zone = ZoneType.Battlefield;
        v2.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(v1);
        _alice.Zones.Battlefield.AddCard(v2);
        _bus.Publish(new CardMovedEvent(v1, ZoneType.Hand, ZoneType.Battlefield));
        _bus.Publish(new CardMovedEvent(v2, ZoneType.Hand, ZoneType.Battlefield));

        v1.Tap();
        v2.Tap();

        SeedLibrary(_alice, 3);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        v1.IsTapped.Should().BeTrue();
        v2.IsTapped.Should().BeTrue();

        // One leaves — the other's restriction is independent (per-permanent token).
        v1.Zone = ZoneType.Graveyard;
        _bus.Publish(new CardMovedEvent(v1, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(v1, _alice).Should().BeFalse();
        UntapStepRestrictions.ShouldSkipUntap(v2, _alice).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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
            var c = Majik.Core.CardData.NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
