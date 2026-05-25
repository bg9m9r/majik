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
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CreepingChillFactory"/> (Guilds of Ravnica,
/// {3}{B}).
///
/// Covers:
///   - Identity (Sorcery {3}{B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - <see cref="CreepingChillFactory.BuildResolveEffect"/> deals 3 to
///     each opponent and gains 3 life to controller (cast path).
///   - Mill-trigger fires on Library → Graveyard (CR 603.6c).
///   - Mill resolution exiles the card AND deals 3 + gains 3.
///   - Mill-trigger does NOT fire on Hand → Graveyard (discard).
/// </summary>
public class CreepingChillTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CreepingChill_Identity()
    {
        var card = CreepingChillFactory.Create(_alice);

        card.Name.Should().Be("Creeping Chill");
        card.ManaCost.Should().Be("{3}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CreepingChill_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Creeping Chill", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Creeping Chill");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{B}");
    }

    [Fact]
    public void CreepingChill_HasMillTrigger_AttachedToCard()
    {
        var card = CreepingChillFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "library→graveyard mill-trigger is attached (CR 603.6c)");
    }

    // -----------------------------------------------------------------------
    // Cast resolve — BuildResolveEffect
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildResolveEffect_DealsThreeToEachOpponent_AndGainsThreeLife()
    {
        var effects = CreepingChillFactory.BuildResolveEffect(
            _alice, new[] { _bob, _carol });

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "Bob takes 3 damage");
        _carol.LifeTotal.Should().Be(17, "Carol takes 3 damage");
        _alice.LifeTotal.Should().Be(23, "Alice gains 3 life");
    }

    [Fact]
    public void BuildResolveEffect_DoesNotDamageController_EvenIfListedAsOpponent()
    {
        // Defensive — the controller-skip guard ensures self-targeting
        // never burns the caster.
        var effects = CreepingChillFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob });

        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(23, "Alice gains 3 life (not damaged)");
        _bob.LifeTotal.Should().Be(17, "Bob takes 3 damage");
    }

    // -----------------------------------------------------------------------
    // Mill-trigger — CR 603.6c
    // -----------------------------------------------------------------------

    [Fact]
    public void MillTrigger_LibraryToGraveyard_ExilesCardAndDealsDamageGainsLife()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var card = CreepingChillFactory.Create(
            _alice,
            zoneService: zones,
            triggers: triggers,
            agent: null, // auto-accept the "you may exile"
            opponentResolver: () => new[] { _bob });

        // Mill from library to graveyard via ZoneService so the event fires.
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        zones.MoveCard(card, ZoneType.Library, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(1, "mill trigger queued for Creeping Chill");

        triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        var triggerOnStack = (TriggeredAbility)stack.Pop()!;
        triggerOnStack.Resolve();

        card.Zone.Should().Be(ZoneType.Exile,
            "Creeping Chill is exiled when its mill trigger resolves");
        _alice.Zones.Exile.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);

        _bob.LifeTotal.Should().Be(17, "Bob takes 3 damage on mill");
        _alice.LifeTotal.Should().Be(23, "Alice gains 3 life on mill");
    }

    [Fact]
    public void MillTrigger_DoesNotFire_OnHandToGraveyard()
    {
        var (zones, _, triggers, _) = BuildEngine();

        var card = CreepingChillFactory.Create(
            _alice,
            zoneService: zones,
            triggers: triggers,
            agent: null,
            opponentResolver: () => new[] { _bob });

        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Discard — Hand → Graveyard, not Library → Graveyard.
        zones.MoveCard(card, ZoneType.Hand, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(0,
            "mill-trigger only fires on library→graveyard (printed text)");
        card.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(20, "no damage dealt — trigger didn't fire");
        _alice.LifeTotal.Should().Be(20, "no life gained — trigger didn't fire");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, MajikStack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
