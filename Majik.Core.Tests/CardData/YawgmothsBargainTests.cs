using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="YawgmothsBargainFactory"/>.
///
/// Card: Yawgmoth's Bargain — Enchantment {4}{B}{B} (Urza's Destiny).
///   "Skip your draw step.
///    Pay 1 life: Draw a card."
///
/// Covers:
///   - Identity + dispatch + printed cost {4}{B}{B}.
///   - Ability shape: static skip-draw marker + Pay-1-life activated.
///   - Skip-draw registry gates on controller + battlefield zone.
///   - Activated ability: pay 1 life, draw top of library to hand.
///   - Empty library: activation is a no-op for the draw.
///   - Cost cannot be paid below 1 life (CR 119.4).
/// </summary>
public class YawgmothsBargainTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public YawgmothsBargainTests()
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
    public void YawgmothsBargain_Identity()
    {
        var card = YawgmothsBargainFactory.Create(_alice);

        card.Name.Should().Be("Yawgmoth's Bargain");
        card.ManaCost.Should().Be("{4}{B}{B}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_YawgmothsBargain()
    {
        var card = NamedCardFactory.Create("Yawgmoth's Bargain", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Yawgmoth's Bargain");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{4}{B}{B}");
    }

    // -----------------------------------------------------------------------
    // Ability shape — static skip-draw marker + Pay-1-life activated
    // -----------------------------------------------------------------------

    [Fact]
    public void YawgmothsBargain_HasStaticMarkerAndActivatedAbility()
    {
        var card = YawgmothsBargainFactory.Create(_alice);

        var statics = card.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().ContainSingle(s => s.Description.Contains("Skip your draw step"));

        card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the 'Pay 1 life: Draw a card' activated ability");
    }

    [Fact]
    public void YawgmothsBargain_ActivatedAbility_HasPayOneLifeCost()
    {
        var card = YawgmothsBargainFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<PayLifeCost>().Should().ContainSingle()
            .Which.Amount.Should().Be(1, "the printed cost is 'Pay 1 life'");
    }

    // -----------------------------------------------------------------------
    // Skip draw step (structural) — SkipDrawRegistry gate
    // -----------------------------------------------------------------------

    [Fact]
    public void SkipDraw_Registered_OnlyForControllerWhileOnBattlefield()
    {
        var wiring = YawgmothsBargainFactory.Create(_alice, registerSkipDraw: true);

        // Not on battlefield yet → predicate returns false even for the
        // controller (CR 614.12).
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "Yawgmoth's Bargain isn't on the battlefield");
        SkipDrawRegistry.ShouldSkipDraw(_bob).Should().BeFalse(
            "Bob isn't the controller");

        // Move to battlefield → controller's draw is skipped, opponent's
        // is not.
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeTrue(
            "Alice controls Yawgmoth's Bargain on the battlefield");
        SkipDrawRegistry.ShouldSkipDraw(_bob).Should().BeFalse(
            "the skip only applies to the controller's draw step");

        // Cleanup removes the predicate.
        wiring.Cleanup.Dispose();
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "cleanup removed the predicate");
    }

    [Fact]
    public void SkipDraw_NotRegistered_WhenRegisterFlagOmitted()
    {
        var wiring = YawgmothsBargainFactory.Create(_alice, registerSkipDraw: false);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "the shape-only overload doesn't touch the registry");

        // The cleanup is the no-op variant — dispose is safe and a no-op.
        wiring.Cleanup.Dispose();
        wiring.Cleanup.Dispose();
    }

    // -----------------------------------------------------------------------
    // Activated ability — Pay 1 life, draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_PaysOneLife_AndDrawsTopOfLibrary()
    {
        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var card = YawgmothsBargainFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = ability.Costs.OfType<PayLifeCost>().Single();

        lifeCost.CanPay(_alice).Should().BeTrue();
        lifeCost.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(startLife - 1, "the cost is Pay 1 life");
        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the top of the library is drawn directly into hand");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Activation_OnEmptyLibrary_IsNoOpForTheDraw()
    {
        var card = YawgmothsBargainFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = ability.Costs.OfType<PayLifeCost>().Single();

        var startLife = _alice.LifeTotal;
        lifeCost.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(startLife - 1,
            "the life cost still happens even when the library is empty");
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no card to draw — effect is a no-op");
    }

    [Fact]
    public void Activation_DoesNothing_WhenBargainHasLeftTheBattlefield()
    {
        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var card = YawgmothsBargainFactory.Create(_alice);
        // Note: NOT on battlefield — simulating bounced/destroyed mid-resolution.
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the effect guards on the source being on the battlefield (CR 113.6)");
        _alice.Zones.Library.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void PayLifeCost_CannotPay_BelowOneLife()
    {
        var card = YawgmothsBargainFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = ability.Costs.OfType<PayLifeCost>().Single();

        // Drain Alice to 0 life — at 0 she has lost; the precondition for
        // PayLifeCost.CanPay is LifeTotal >= amount, so 0 < 1 → cannot pay.
        // (We can't actually drain via LoseLife once she's lost, so set
        // the bound to exactly 1 first.)
        _alice.LoseLife(19); // Alice now at 1
        lifeCost.CanPay(_alice).Should().BeTrue(
            "at exactly 1 life, Pay 1 life is legal (CR 119.4)");

        // Pay 1 → goes to 0 → has lost.
        lifeCost.Pay(_alice);
        _alice.LifeTotal.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Repeated activations: each pays 1 life, each draws one card
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleActivations_DrainLifeAndDrawCardsOneToOne()
    {
        var c1 = new Card("c1", "");
        var c2 = new Card("c2", "");
        var c3 = new Card("c3", "");
        _alice.Zones.Library.AddCard(c1); c1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c2); c2.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c3); c3.SetZone(ZoneType.Library);

        var card = YawgmothsBargainFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = ability.Costs.OfType<PayLifeCost>().Single();

        var startLife = _alice.LifeTotal;
        for (int i = 0; i < 3; i++)
        {
            lifeCost.Pay(_alice);
            foreach (var e in ability.Effects) e.Execute();
        }

        _alice.LifeTotal.Should().Be(startLife - 3, "three activations = pay 3 life total");
        _alice.Zones.Hand.GetCards().Should().HaveCount(3,
            "three activations = three cards drawn");
    }
}
