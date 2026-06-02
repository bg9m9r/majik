using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PheliaExuberantShepherdFactory"/>.
///
/// Covers:
/// - Identity (Legendary Dog Wizard 2/1 at {1}{W}; Lifelink keyword
///   attached; exactly one TriggeredAbility wired on the attack event).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Attack trigger exiles another target nonland permanent.
/// - Exile + delayed return: at the next end step, the exiled card comes
///   back to the battlefield under its owner's control.
/// - Resolution-time legality re-check: a Land target is rejected.
/// - Resolution-time legality re-check: Phelia herself is rejected
///   ("another target").
/// </summary>
[Trait("Color", "W")]
public class PheliaExuberantShepherdFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Phelia_Identity()
    {
        var p = PheliaExuberantShepherdFactory.Create(_alice);

        p.Name.Should().Be("Phelia, Exuberant Shepherd");
        p.ManaCost.Should().Be("{1}{W}");
        p.HasType(CardType.Creature).Should().BeTrue();
        p.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        p.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        p.BasePower.Should().Be(2);
        p.BaseToughness.Should().Be(1);
        p.Supertypes.Should().Contain(CardSupertype.Legendary);
        p.Owner.Should().BeSameAs(_alice);

        p.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Lifelink");

        p.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
    [Fact]
    public void AttackTrigger_ExilesAnotherNonlandPermanent_AndDelayedReturnFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phelia = PheliaExuberantShepherdFactory.Create(_alice, triggers);
        phelia.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(phelia);

        // Bob has a nonland creature on the battlefield to be exiled.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var attack = phelia.Abilities.OfType<TriggeredAbility>().Single();
        attack.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in attack.Effects) e.Execute();

        grizzly.Zone.Should().Be(ZoneType.Exile,
            "Phelia's attack trigger exiles the target nonland permanent");
        _bob.Zones.Exile.GetCards().Should().Contain(grizzly,
            "the exiled card lands in its OWNER's exile pile (CR 614)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(grizzly);

        // Fire the next end step — the delayed trigger should queue.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the delayed return rider fires on the first end step after the attack");

        triggers.PutPendingTriggersOnStack(_alice);
        // Resolve everything on the stack so the return effect runs.
        while (stack.Count > 0) stack.Pop()!.Resolve();

        grizzly.Zone.Should().Be(ZoneType.Battlefield,
            "the exiled card returns to the battlefield at the next end step");
        _bob.Zones.Battlefield.GetCards().Should().Contain(grizzly,
            "the return is 'under its owner's control' — Bob");
        _bob.Zones.Exile.GetCards().Should().NotContain(grizzly);
        grizzly.Controller.Should().BeSameAs(_bob,
            "CR 614 — 'under its owner's control'");
    }

    [Fact]
    public void AttackTrigger_LandTarget_IsRejectedOnResolve()
    {
        var phelia = PheliaExuberantShepherdFactory.Create(_alice);
        phelia.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(phelia);

        var forest = new Land("Forest",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_bob);
        forest.SetController(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);

        var attack = phelia.Abilities.OfType<TriggeredAbility>().Single();
        attack.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { forest },
        });
        foreach (var e in attack.Effects) e.Execute();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "Lands are not 'nonland permanents' — CR 305; resolve-time re-check rejects");
        _bob.Zones.Exile.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void AttackTrigger_SelfTarget_IsRejectedOnResolve()
    {
        var phelia = PheliaExuberantShepherdFactory.Create(_alice);
        phelia.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(phelia);

        var attack = phelia.Abilities.OfType<TriggeredAbility>().Single();
        attack.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { phelia },
        });
        foreach (var e in attack.Effects) e.Execute();

        phelia.Zone.Should().Be(ZoneType.Battlefield,
            "'another target' (CR 115.5b) excludes Phelia herself");
        _alice.Zones.Exile.GetCards().Should().NotContain(phelia);
    }
}
