using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Snap (Urza's Saga, {U}{U}).
/// Mirrors the Snapback test shape:
///   * Card shape + dispatch.
///   * Cast with printed mana — bounces target creature, untaps two lands.
///   * Zero land picks — bounce still happens, no untap side-effect.
///   * Illegal bounce target (off-battlefield) → bounce no-ops, untap still
///     fires on its own targets.
///   * Non-land pick is silently skipped at resolve (CR 608.2b).
/// </summary>
public class SnapFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SnapFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var snap = SnapFactory.Create(_alice);

        snap.Name.Should().Be("Snap");
        snap.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(snap).Should().Contain(ManaColor.Blue);
        snap.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSnapShape()
    {
        var dispatched = NamedCardFactory.Create("Snap", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Snap");
    }

    [Fact]
    public async Task Cast_BouncesCreature_AndUntapsTwoLands()
    {
        var snap = SnapFactory.Create(_alice);
        snap.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snap);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var island1 = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice };
        island1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island1);
        island1.Tap();

        var island2 = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice };
        island2.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island2);
        island2.Tap();

        var agent = new ScriptedAgent();
        // Slot 0 — bounce target.
        agent.QueueTargets(new[] { (object)bobBear });
        // Slot 1 — up to two land targets.
        agent.QueueTargets(new[] { (object)island1, (object)island2 });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snap,
            SnapFactory.BuildDefinition(o => o, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Hand,
            because: "Snap returns target creature to owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bobBear);
        island1.IsTapped.Should().BeFalse(because: "first land target is untapped");
        island2.IsTapped.Should().BeFalse(because: "second land target is untapped");
    }

    [Fact]
    public async Task Cast_BounceWithZeroLandPicks_StillBounces()
    {
        var snap = SnapFactory.Create(_alice);
        snap.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snap);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobBear });
        // Slot 1 — pick zero lands (open-cardinality lower bound = 0).
        agent.QueueTargets(Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snap,
            SnapFactory.BuildDefinition(o => o, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NonLandPick_IsSilentlySkipped()
    {
        // CR 608.2b — non-land pick is a no-op at resolve (defensive
        // type check). Bounce still fires on its own target.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        // A tapped creature — not a land — handed in as the "land" pick.
        var tappedBear = new Creature("Tapped Bear", "{1}{G}", 2, 2)
            { Owner = _alice, Controller = _alice };
        tappedBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tappedBear);
        tappedBear.Tap();

        var def = SnapFactory.BuildDefinition(o => o, _zones);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[]
            {
                new object[] { bobBear },
                new object[] { tappedBear },
            },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Hand);
        tappedBear.IsTapped.Should().BeTrue(
            because: "non-land picks are silently skipped");
    }

    [Fact]
    public void Resolve_IllegalBounceTarget_IsNoOp_UntapStillFires()
    {
        // CR 608.2b — bounce target moved off battlefield → no-op. The
        // untap half still resolves on its own targets.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobBear);

        var island = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _alice, Controller = _alice };
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);
        island.Tap();

        var def = SnapFactory.BuildDefinition(o => o, _zones);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[]
            {
                new object[] { bobBear },
                new object[] { island },
            },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "illegal bounce target → no bounce");
        island.IsTapped.Should().BeFalse(
            because: "untap target half still fires on its own pick");
    }
}
