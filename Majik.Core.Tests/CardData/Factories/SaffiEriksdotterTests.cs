using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SaffiEriksdotterFactory"/>.
///
/// Card: Saffi Eriksdotter — Legendary Creature — Human Scout {G}{W} 2/2
/// (Time Spiral).
///   "Sacrifice Saffi Eriksdotter: When target creature is put into a
///    graveyard this turn, return that card to the battlefield under
///    its owner's control."
/// </summary>
[Trait("Color", "M")]
public class SaffiEriksdotterTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public SaffiEriksdotterTests()
    {
        _zones = new ZoneService(_bus);
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SaffiEriksdotter_Identity()
    {
        var c = SaffiEriksdotterFactory.Create(_alice);

        c.Name.Should().Be("Saffi Eriksdotter");
        c.ManaCost.Should().Be("{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Activated_HasTargetCreatureRequest()
    {
        var saffi = SaffiEriksdotterFactory.Create(_alice);
        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();

        activated.TargetRequests.Should().HaveCount(1);
        var req = activated.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    [Fact]
    public void Activated_NoManaCost()
    {
        var saffi = SaffiEriksdotterFactory.Create(_alice);
        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();

        activated.Costs.Should().BeEmpty(
            "sacrifice is the entire activation cost; no mana / tap component");
    }

    // -----------------------------------------------------------------------
    // Sacrifice self
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolution_SacrificesSaffiToOwnersGraveyard()
    {
        var saffi = SaffiEriksdotterFactory.Create(_alice, _zones, triggers: null);
        PutOnBattlefield(_alice, saffi);

        // Target some creature (irrelevant for the sac-self test).
        var target = new Creature("Test Creature", "{1}", 1, 1);
        target.SetOwner(_alice);
        PutOnBattlefield(_alice, target);

        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var e in activated.Effects) e.Execute();

        saffi.Zone.Should().Be(ZoneType.Graveyard, "Saffi sacrificed herself");
        _alice.Zones.Graveyard.GetCards().Should().Contain(saffi);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(saffi);
    }

    // -----------------------------------------------------------------------
    // Delayed reanimate trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetDies_DelayedTriggerReturnsItToBattlefield()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var saffi = SaffiEriksdotterFactory.Create(_alice, _zones, triggers);
        PutOnBattlefield(_alice, saffi);

        // Some Saffi-protected creature.
        var ally = new Creature("Reveillark", "{4}{W}", 4, 3);
        ally.SetOwner(_alice);
        PutOnBattlefield(_alice, ally);

        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });

        // Resolve the activation: Saffi sacrifices, delayed trigger arms.
        foreach (var e in activated.Effects) e.Execute();
        saffi.Zone.Should().Be(ZoneType.Graveyard);

        // The ally now dies. Route the move through ZoneService so the
        // CardMovedEvent publishes (CR 603.6c — dies trigger).
        _zones.MoveCard(ally, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        ally.Zone.Should().Be(ZoneType.Graveyard,
            "ally is in the graveyard before the delayed trigger fires");

        // Fire the delayed trigger.
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        ally.Zone.Should().Be(ZoneType.Battlefield,
            "CR 603.7 — delayed trigger reanimates the target creature this turn");
        _alice.Zones.Battlefield.GetCards().Should().Contain(ally);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ally);
    }

    [Fact]
    public void TargetReturned_UnderOwnersControl_NotPriorController()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var saffi = SaffiEriksdotterFactory.Create(_alice, _zones, triggers);
        PutOnBattlefield(_alice, saffi);

        // Bob owns the creature; Alice doesn't currently control it
        // (simulating a scenario where Saffi targets an opponent-owned
        // creature). The "under its owner's control" rider in Saffi's
        // text is critical for stolen creatures — the engine must wire
        // Controller = Owner at reanimation time, not preserve whatever
        // controller the card had pre-death.
        var stolen = new Creature("Bob's Creature", "{2}", 2, 2);
        stolen.SetOwner(_bob);
        PutOnBattlefield(_bob, stolen);

        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { stolen } });

        foreach (var e in activated.Effects) e.Execute();

        // Bob's creature dies. Route move through ZoneService (Bob owns,
        // Bob controls → Bob's graveyard per CR 404.2).
        _zones.MoveCard(stolen, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        _bob.Zones.Graveyard.GetCards().Should().Contain(stolen,
            "owner's graveyard receives the card — CR 404.2");

        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        stolen.Zone.Should().Be(ZoneType.Battlefield);
        stolen.Controller.Should().BeSameAs(_bob,
            "rider: 'under its owner's control' — owner = Bob");
        _bob.Zones.Battlefield.GetCards().Should().Contain(stolen);
    }

    [Fact]
    public void TargetNeverDies_NoReanimation()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var saffi = SaffiEriksdotterFactory.Create(_alice, _zones, triggers);
        PutOnBattlefield(_alice, saffi);

        var ally = new Creature("Stable Creature", "{1}", 1, 1);
        ally.SetOwner(_alice);
        PutOnBattlefield(_alice, ally);

        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });

        foreach (var e in activated.Effects) e.Execute();

        // Ally stays put. No CardMovedEvent battlefield→graveyard fires.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.IsEmpty.Should().BeTrue("no death = no trigger queued");

        ally.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void TargetDies_DifferentCard_DoesNotTrigger()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var saffi = SaffiEriksdotterFactory.Create(_alice, _zones, triggers);
        PutOnBattlefield(_alice, saffi);

        var ally = new Creature("Target", "{1}", 1, 1);
        ally.SetOwner(_alice);
        PutOnBattlefield(_alice, ally);

        var bystander = new Creature("Bystander", "{1}", 1, 1);
        bystander.SetOwner(_alice);
        PutOnBattlefield(_alice, bystander);

        var activated = saffi.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });

        foreach (var e in activated.Effects) e.Execute();

        // Bystander dies, NOT the target. Delayed trigger watches the
        // target card by reference — shouldn't fire.
        _zones.MoveCard(bystander, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.IsEmpty.Should().BeTrue(
            "the trigger fences on ReferenceEquals(e.Card, target) — bystander doesn't match");

        bystander.Zone.Should().Be(ZoneType.Graveyard);
        ally.Zone.Should().Be(ZoneType.Battlefield);
    }
}
