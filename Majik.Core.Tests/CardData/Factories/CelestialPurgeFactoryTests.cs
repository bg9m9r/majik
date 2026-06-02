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
/// TDD tests for Celestial Purge (Magic 2011 / Modern Masters, {1}{W}).
/// Oracle: "Exile target black or red permanent."
///
/// Coverage:
///   - Card identity: Instant, {1}{W}, white, CMC 2.
///   - NamedCardFactory dispatch by name.
///   - SpellDefinition shape: 1 target request, no X, no modes.
///   - Resolving against a black permanent → exiles it (CR 701.21).
///   - Resolving against a red permanent → exiles it.
///   - Resolving against a black-AND-red permanent → exiles it.
///   - Resolving against a green/white/colourless permanent → no-op (CR 608.2b).
///   - Target no longer on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class CelestialPurgeFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CelestialPurgeFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ------------------------------------------------------------------
    // Identity / dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_WhiteManaCostTwo()
    {
        var cp = CelestialPurgeFactory.Create(_alice);

        cp.Name.Should().Be("Celestial Purge");
        cp.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(cp).Should().Contain(ManaColor.White);
        cp.ManaCostValue.TotalValue.Should().Be(2);
        cp.Owner.Should().BeSameAs(_alice);
        cp.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BuildDefinition_OneRequiredTarget_NoXNoModes()
    {
        var def = CelestialPurgeFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target black or red permanent");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Resolve — exile black permanent
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingBlackPermanent_ExilesIt()
    {
        // A black creature (mana cost {B}) — purely black.
        var darkRitual = new Creature("Dross Rat", "{B}", 1, 1);
        darkRitual.SetOwner(_bob);
        darkRitual.SetController(_bob);
        _zones.MoveCard(darkRitual, ZoneType.Library, ZoneType.Battlefield, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        await CastAndResolveAsync(cp, darkRitual);

        darkRitual.Zone.Should().Be(ZoneType.Exile, because: "Celestial Purge exiles black permanents");
        _bob.Zones.Exile.GetCards().Should().Contain(darkRitual);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(darkRitual);
    }

    // ------------------------------------------------------------------
    // Resolve — exile red permanent
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingRedPermanent_ExilesIt()
    {
        // A red creature (mana cost {R}).
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        _zones.MoveCard(goblin, ZoneType.Library, ZoneType.Battlefield, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        await CastAndResolveAsync(cp, goblin);

        goblin.Zone.Should().Be(ZoneType.Exile, because: "Celestial Purge exiles red permanents");
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
    }

    // ------------------------------------------------------------------
    // Resolve — exile black-and-red permanent
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingBlackRedPermanent_ExilesIt()
    {
        // A black-red creature (mana cost {B}{R}).
        var kolaghan = new Creature("Kolaghan Dragon", "{B}{R}", 3, 3);
        kolaghan.SetOwner(_bob);
        kolaghan.SetController(_bob);
        _zones.MoveCard(kolaghan, ZoneType.Library, ZoneType.Battlefield, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        await CastAndResolveAsync(cp, kolaghan);

        kolaghan.Zone.Should().Be(ZoneType.Exile, because: "Celestial Purge exiles black-red permanents");
    }

    // ------------------------------------------------------------------
    // Resolve — no-op on non-black, non-red permanent (CR 608.2b)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingGreenPermanent_IsNoOp()
    {
        // A green creature (mana cost {G}).
        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        elf.SetOwner(_bob);
        elf.SetController(_bob);
        _zones.MoveCard(elf, ZoneType.Library, ZoneType.Battlefield, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        await CastAndResolveAsync(cp, elf);

        elf.Zone.Should().Be(ZoneType.Battlefield,
            because: "Green is not black or red — Celestial Purge has no effect");
    }

    [Fact]
    public async Task TargetingWhitePermanent_IsNoOp()
    {
        var soldier = new Creature("Elite Vanguard", "{W}", 2, 1);
        soldier.SetOwner(_bob);
        soldier.SetController(_bob);
        _zones.MoveCard(soldier, ZoneType.Library, ZoneType.Battlefield, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        await CastAndResolveAsync(cp, soldier);

        soldier.Zone.Should().Be(ZoneType.Battlefield,
            because: "White is not black or red — Celestial Purge has no effect");
    }

    [Fact]
    public async Task TargetingColourlessPermanent_IsNoOp()
    {
        // A colourless artifact (mana cost {2}).
        var sphere = new Card("Mox Opal", "{0}", new[] { CardType.Artifact });
        sphere.SetOwner(_bob);
        sphere.SetController(_bob);
        _zones.MoveCard(sphere, ZoneType.Library, ZoneType.Battlefield, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        await CastAndResolveAsync(cp, sphere);

        sphere.Zone.Should().Be(ZoneType.Battlefield,
            because: "Colourless is not black or red — Celestial Purge has no effect");
    }

    // ------------------------------------------------------------------
    // Resolve — target left battlefield before resolution (CR 608.2b)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetLeavesFieldBeforeResolution_IsNoOp()
    {
        // A black creature that moves to the graveyard before resolution.
        var shade = new Creature("Highborn Ghoul", "{B}", 2, 1);
        shade.SetOwner(_bob);
        shade.SetController(_bob);
        _zones.MoveCard(shade, ZoneType.Library, ZoneType.Battlefield, _bob);

        // Move it off the battlefield before casting/resolving the purge.
        _zones.MoveCard(shade, ZoneType.Battlefield, ZoneType.Graveyard, _bob);

        var cp = CelestialPurgeFactory.Create(_alice);
        cp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cp);

        // targetResolver passes the already-off-battlefield creature.
        await CastAndResolveAsync(cp, shade);

        shade.Zone.Should().Be(ZoneType.Graveyard,
            because: "Target was not on the battlefield at resolution — no-op per CR 608.2b");
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    private async Task CastAndResolveAsync(Instant cp, object target)
    {
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, cp,
            CelestialPurgeFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
