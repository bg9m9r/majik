using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="NecrodominanceFactory"/>.
///
/// Covers card identity, dispatcher routing, the structural skip-draw
/// + additional-draw-skip + activated-ability shape, the skip-draw
/// registry behaviour, the Pay-1-life + exile-top + cast-from-exile-
/// until-EOT activation, and the EOT cleanup hook that revokes the
/// cast permission at the next Cleanup step. <see cref="SkipDrawRegistry.Clear"/>
/// is called in <see cref="IDisposable.Dispose"/> to prevent test
/// leakage of the process-global registry.
/// </summary>
public class NecrodominanceTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NecrodominanceTests()
    {
        SkipDrawRegistry.Clear();
    }

    public void Dispose()
    {
        SkipDrawRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Necrodominance_IsEnchantment()
    {
        var necro = NecrodominanceFactory.Create(_alice);
        necro.HasType(CardType.Enchantment).Should().BeTrue();
        necro.Name.Should().Be("Necrodominance");
        necro.Owner.Should().BeSameAs(_alice);
        necro.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Necrodominance()
    {
        var card = NamedCardFactory.Create("Necrodominance", _alice);
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Name.Should().Be("Necrodominance");
    }

    // -----------------------------------------------------------------------
    // Ability shape — two static markers + the activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Necrodominance_HasStaticMarkersAndActivatedAbility()
    {
        var necro = NecrodominanceFactory.Create(_alice);

        var statics = necro.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().HaveCount(2,
            "one marker for 'Skip your draw step', one for the additional-draw-skip clause");
        statics.Should().Contain(s => s.Description.Contains("Skip your draw step"));
        statics.Should().Contain(s => s.Description.Contains("except for the first card you draw"));
        necro.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the 'Pay 1 life: ...' activated ability");
    }

    [Fact]
    public void Necrodominance_ActivatedAbility_HasPayOneLifeCost()
    {
        var necro = NecrodominanceFactory.Create(_alice);
        var ability = necro.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.PayLife,
                "the activated ability's cost is 'Pay 1 life'");
    }

    // -----------------------------------------------------------------------
    // Skip draw step — SkipDrawRegistry gate
    // -----------------------------------------------------------------------

    [Fact]
    public void SkipDraw_Registered_OnlyForControllerWhileOnBattlefield()
    {
        var wiring = NecrodominanceFactory.Create(_alice, eventBus: null);

        // Not on battlefield yet → predicate false even for the controller
        // (CR 614.12 — replacement effects only function from battlefield).
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "Necrodominance isn't on the battlefield");
        SkipDrawRegistry.ShouldSkipDraw(_bob).Should().BeFalse(
            "Bob isn't the controller");

        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeTrue(
            "Alice controls Necrodominance on the battlefield — her draw is skipped");
        SkipDrawRegistry.ShouldSkipDraw(_bob).Should().BeFalse(
            "Necrodominance does not skip the non-controller's draw step");

        wiring.Cleanup.Dispose();
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "cleanup removed the predicate");
    }

    // -----------------------------------------------------------------------
    // Activated ability: Pay 1 life, exile top, grant cast-from-exile alt cost
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_PayLife_ExilesTopOfLibrary_AndGrantsCastPermission()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var wiring = NecrodominanceFactory.Create(_alice, eventBus: null);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = ability.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.PayLife);

        lifeCost.CanPay(_alice).Should().BeTrue();
        lifeCost.Pay(_alice);

        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(startLife - 1, "the cost is Pay 1 life");
        _alice.Zones.Exile.GetCards().Should().Contain(top, "the top of the library is exiled");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        _alice.Zones.Hand.GetCards().Should().NotContain(top,
            "the exiled card is NOT drawn — it's playable from exile until EOT");
        top.Zone.Should().Be(ZoneType.Exile);

        wiring.ActiveCasts.Should().ContainSingle(
            "the activation stamps one cast-from-exile permission");
        var permission = wiring.ActiveCasts.Single();
        permission.ExiledCard.Should().BeSameAs(top);
        permission.IsActive.Should().BeTrue();
        permission.AlternativeCost.AlternativeManaCost.Should().Be(ManaCost.Zero,
            "the cast-from-exile alt cost is free (CR 118.9)");
        permission.AlternativeCost.CanCastFor(top, _alice).Should().BeTrue(
            "the exile-resident card is castable by its owner");
    }

    [Fact]
    public void CleanupStep_Revokes_CastFromExilePermission()
    {
        var bus = new EventBus();
        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var wiring = NecrodominanceFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        wiring.ActiveCasts.Should().ContainSingle();
        var permission = wiring.ActiveCasts.Single();
        permission.IsActive.Should().BeTrue();

        // Non-Cleanup steps don't revoke (CR 514.2 — "until end of turn"
        // expires at the next Cleanup step, not earlier).
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        permission.IsActive.Should().BeTrue(
            "end-step doesn't revoke — only the Cleanup step does");

        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        permission.IsActive.Should().BeFalse(
            "the Cleanup step revokes the cast-from-exile permission (CR 514.2)");
        wiring.ActiveCasts.Should().BeEmpty(
            "the permission is removed from the active-casts snapshot");
    }

    [Fact]
    public void MultipleActivations_EachGrant_IndependentPermission()
    {
        var bus = new EventBus();
        var c1 = new Card("c1", "");
        var c2 = new Card("c2", "");
        var c3 = new Card("c3", "");
        _alice.Zones.Library.AddCard(c1); c1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c2); c2.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c3); c3.SetZone(ZoneType.Library);

        var wiring = NecrodominanceFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        for (int i = 0; i < 3; i++)
        {
            foreach (var e in ability.Effects) e.Execute();
        }

        _alice.Zones.Exile.GetCards().Should().HaveCount(3,
            "each activation exiles one card from the top of the library");
        wiring.ActiveCasts.Should().HaveCount(3,
            "each activation stamps its own cast-from-exile permission");
        wiring.ActiveCasts.Select(p => p.ExiledCard).Should().BeEquivalentTo(
            new[] { c1, c2, c3 },
            "each permission references the card it exiled");

        // Cleanup revokes them all in one shot.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        wiring.ActiveCasts.Should().BeEmpty(
            "the Cleanup step revokes every outstanding permission");
    }

    [Fact]
    public void Activation_OnEmptyLibrary_IsNoop()
    {
        // Library is empty — activation cost is still paid (the engine
        // doesn't check effect-time pre-conditions before the cost), but
        // the effect body short-circuits with no exile + no permission.
        var wiring = NecrodominanceFactory.Create(_alice, eventBus: null);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().BeEmpty(
            "empty library → nothing to exile");
        wiring.ActiveCasts.Should().BeEmpty(
            "no permission stamped when nothing was exiled");
    }
}
