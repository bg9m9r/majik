using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="WishclawTalismanFactory"/> — Artifact {1}{B},
/// ETB tapped, {T}/Pay 3 life: tutor any card → opponent gains control.
///
/// Covers:
/// - Card identity (Artifact, {1}{B}, name) + <see cref="NamedCardFactory"/> dispatch.
/// - ETB-tapped replacement effect (CR 614.1c) — applies to its own
///   battlefield-bound ZoneMoveIntent.
/// - Activated ability shape: {T} + Pay 3 life cost pair.
/// - Activation: tutor any card → hand, life cost paid.
/// - Control change via <see cref="ContinuousEffectsService"/> after
///   activation (CR 613.2 — Layer 2 control swap).
/// - Sorcery-speed restriction is a documented v1 deferral (no per-
///   activated-ability gate yet). The xmldoc on the factory captures
///   the deferral; tests do not exercise the missing gate.
/// </summary>
public class WishclawTalismanTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void WishclawTalisman_IsArtifactWithCorrectIdentity()
    {
        var card = WishclawTalismanFactory.Create(_alice);

        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Name.Should().Be("Wishclaw Talisman");
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WishclawTalisman()
    {
        var card = NamedCardFactory.Create("Wishclaw Talisman", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Wishclaw Talisman");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB tapped (CR 614.1c) — EntersTappedReplacement applied to own ETB intent
    // -----------------------------------------------------------------------

    [Fact]
    public void WishclawTalisman_EntersTapped_Replacement_RewritesOwnETBIntent()
    {
        var wiring = WishclawTalismanFactory.Create(_alice, effects: null, opponentChooser: null);

        // ETB intent for the Talisman itself: hand → battlefield, no
        // EntersTapped flag yet. The replacement should apply and rewrite
        // EntersTapped = true.
        var intent = new ZoneMoveIntent(
            Card: wiring.Card,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        wiring.EntersTappedReplacement.Applies(intent, Array.Empty<object>())
            .Should().BeTrue("the ETB replacement targets this card's hand→battlefield move");

        var replaced = wiring.EntersTappedReplacement.Replace(intent, Array.Empty<object>());

        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Wishclaw Talisman enters tapped — the replacement sets the side-channel flag");
        replaced.ToZone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape — {T} + Pay 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public void WishclawTalisman_HasActivatedAbility_WithTapAndPay3LifeCosts()
    {
        var card = WishclawTalismanFactory.Create(_alice);

        var activated = card.Abilities.OfType<ActivatedAbility>().Single();
        var addCosts = activated.Costs.OfType<AdditionalCost>().ToList();

        addCosts.Should().Contain(c => c.CostType == AdditionalCostType.Tap,
            "the printed cost includes {T}");
        addCosts.Should().Contain(c => c.CostType == AdditionalCostType.PayLife,
            "the printed cost includes 'Pay 3 life'");
    }

    // -----------------------------------------------------------------------
    // Activation — Pay 3 life, tutor a card, then control changes to opponent
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_PaysThreeLife_TutorsCardToHand_AndSwapsControlToOpponent()
    {
        // Seed Alice's library with a target tutor pick.
        var target = new Card("Target Card", "");
        _alice.Zones.Library.AddCard(target);
        target.SetZone(ZoneType.Library);

        var effects = new ContinuousEffectsService();
        var wiring = WishclawTalismanFactory.Create(
            _alice,
            effects: effects,
            opponentChooser: () => _bob);

        // Put the Talisman on the battlefield so the control-swap guard
        // (zone check) passes.
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var activated = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        var tapCost = activated.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Tap);
        var lifeCost = activated.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.PayLife);

        var startLife = _alice.LifeTotal;

        // Pay the costs.
        tapCost.CanPay(_alice).Should().BeTrue();
        tapCost.Pay(_alice);
        lifeCost.CanPay(_alice).Should().BeTrue();
        lifeCost.Pay(_alice);

        wiring.Card.IsTapped.Should().BeTrue("{T} cost taps the Talisman");
        _alice.LifeTotal.Should().Be(startLife - 3, "the cost is 'Pay 3 life'");

        // Resolve the effects: tutor first, then opponent gains control.
        foreach (var e in activated.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(target,
            "tutor moves the chosen card from library to hand");
        _alice.Zones.Library.GetCards().Should().NotContain(target);
        target.Zone.Should().Be(ZoneType.Hand);

        // CR 613.2 — Layer 2 control-change applies via EffectiveController.
        // Permanent.Controller stays Alice; EffectiveController returns Bob.
        wiring.Card.Controller.Should().BeSameAs(_alice,
            "underlying Permanent.Controller is left untouched (CR 613.2 layered swap)");
        effects.EffectiveController(wiring.Card).Should().BeSameAs(_bob,
            "an opponent gains control of Wishclaw Talisman after activation");
    }

    // -----------------------------------------------------------------------
    // Activation — no opponentChooser / no effects service = no control swap
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_WithoutContinuousEffectsService_DoesNotChangeControl()
    {
        var target = new Card("Target Card", "");
        _alice.Zones.Library.AddCard(target);
        target.SetZone(ZoneType.Library);

        // Single-arg dispatcher path: no ContinuousEffectsService / opponentChooser
        // wired. The activated ability still tutors but the control-swap is a no-op
        // — matches the documented dispatcher behaviour.
        var card = WishclawTalismanFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var activated = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(target,
            "tutor still fires in the no-runtime-services dispatcher path");
        card.Controller.Should().BeSameAs(_alice,
            "without a ContinuousEffectsService the control-swap step is a no-op");
    }
}
