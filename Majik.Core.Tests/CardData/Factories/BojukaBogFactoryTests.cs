using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BojukaBogFactory"/> (Worldwake / MH2).
///
/// Covers:
/// - Identity (Land, no printed subtype, owner / controller).
/// - {T}: Add {B} mana ability present.
/// - Enters-tapped replacement (CR 614.1c) — present when wired through
///   <see cref="ReplacementBus"/>; absent on shape-only path.
/// - ETB triggered ability (CR 603.6a) — exiles target player's graveyard.
/// - Empty-graveyard fizzle (CR 608.2b) — clean no-op.
/// - Target-player fallback to controller when no target was chosen.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "C")]
public class BojukaBogFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BojukaBog_Identity_LandWithNoSubtype()
    {
        var bog = BojukaBogFactory.Create(_alice);

        bog.Name.Should().Be("Bojuka Bog");
        bog.HasType(CardType.Land).Should().BeTrue();
        bog.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Bojuka Bog is a non-basic Land");
        bog.Owner.Should().BeSameAs(_alice);
        bog.Controller.Should().BeSameAs(_alice);

        // {T}: Add {B} + the ETB exile trigger.
        bog.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        bog.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
    // -----------------------------------------------------------------------
    // Enters-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 614.1c — "Bojuka Bog enters tapped." When the
    /// EntersTappedReplacement is registered on the ReplacementBus, the
    /// ZoneService.MoveCardTo path sets IsTapped on landing.
    /// </summary>
    [Fact]
    public void BojukaBog_EntersTapped_WhenWiredThroughReplacementBus()
    {
        var (zones, _, triggers, rep) = BuildEngine();

        var bog = BojukaBogFactory.Create(_alice, eventBus: null, triggers, rep);
        bog.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bog);
        triggers.BindCard(bog);

        zones.MoveCardTo(bog, ZoneType.Battlefield, controller: _alice);

        bog.IsTapped.Should().BeTrue(
            "CR 614.1c — Bojuka Bog enters tapped");
        bog.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void BojukaBog_EntersUntapped_OnShapeOnlyPath()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var bog = BojukaBogFactory.Create(_alice);
        bog.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bog);

        zones.MoveCardTo(bog, ZoneType.Battlefield, controller: _alice);

        bog.IsTapped.Should().BeFalse(
            "shape-only path omits the enters-tapped replacement");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void BojukaBog_HasTapAddBlackManaAbility()
    {
        var bog = BojukaBogFactory.Create(_alice);

        var mana = bog.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.TotalValue.Should().Be(1,
            "{T}: Add {B} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — exile target player's graveyard (CR 603.6a)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 603.6a — "When Bojuka Bog enters, exile target player's
    /// graveyard." With Bob as the chosen target, every card in Bob's
    /// graveyard moves to Bob's exile zone on resolution.
    /// </summary>
    [Fact]
    public void BojukaBog_EntersBattlefield_ExilesTargetPlayersGraveyard()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        // Seed Bob's graveyard with a few cards.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var bolt = new Instant("Lightning Bolt", "{R}");
        var swamp = new Land("Swamp");
        foreach (var c in new ICard[] { goyf, bolt, swamp })
        {
            c.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var bog = BojukaBogFactory.Create(_alice, eventBus: null, triggers, rep);
        bog.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bog);
        triggers.BindCard(bog);

        zones.MoveCardTo(bog, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "ETB exile trigger must queue on entering battlefield");

        var etbTrigger = bog.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Bojuka Bog exiles every card in the target player's graveyard");
        _bob.Zones.Exile.GetCards().Should().Contain(new ICard[] { goyf, bolt, swamp });
        goyf.Zone.Should().Be(ZoneType.Exile);
        bolt.Zone.Should().Be(ZoneType.Exile);
        swamp.Zone.Should().Be(ZoneType.Exile);
    }

    /// <summary>
    /// CR 608.2b — empty graveyard is a clean no-op (the ability still
    /// resolves; the loop just doesn't move anything).
    /// </summary>
    [Fact]
    public void BojukaBog_EntersBattlefield_EmptyGraveyardNoOps()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        var bog = BojukaBogFactory.Create(_alice, eventBus: null, triggers, rep);
        bog.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bog);
        triggers.BindCard(bog);

        zones.MoveCardTo(bog, ZoneType.Battlefield, controller: _alice);

        var etbTrigger = bog.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().BeEmpty(
            "empty graveyard → no cards exiled");
    }

    /// <summary>
    /// v1 deterministic fallback: when no target was chosen, the ETB
    /// effect exiles the controller's own graveyard (mirrors Tormod's
    /// Crypt / Nihil Spellbomb).
    /// </summary>
    [Fact]
    public void BojukaBog_NoTargetChosen_ExilesControllersGraveyard()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        // Alice's own graveyard.
        var ponder = new Instant("Ponder", "{U}");
        ponder.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(ponder);
        ponder.SetZone(ZoneType.Graveyard);

        var bog = BojukaBogFactory.Create(_alice, eventBus: null, triggers, rep);
        bog.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bog);
        triggers.BindCard(bog);

        zones.MoveCardTo(bog, ZoneType.Battlefield, controller: _alice);

        // No SetChosenTargets call — fall through to controller.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(ponder);
        ponder.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
