using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GriselbrandFactory"/>
/// (Avacyn Restored, {4}{B}{B}{B}).
///
/// Legendary Creature — Demon 7/7. Oracle text:
///   "Flying
///    Lifelink
///    Pay 7 life: Draw seven cards."
///
/// Covers:
///   - Identity (Legendary Demon, {4}{B}{B}{B}, 7/7).
///   - NamedCardFactory dispatch.
///   - Flying + Lifelink markers readable by CombatAbilities helpers.
///   - Activated ability shape (single PayLifeCost(7), draw-7 effect).
///   - Mechanic: pay 7 life → controller draws 7, life total drops.
///   - CR 119.4 — can't activate with < 7 life (CanPay gate).
/// </summary>
public class GriselbrandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Griselbrand_Identity()
    {
        var g = GriselbrandFactory.Create(_alice);

        g.Name.Should().Be("Griselbrand");
        g.ManaCost.Should().Be("{4}{B}{B}{B}");
        g.HasType(CardType.Creature).Should().BeTrue();
        g.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        g.HasSubtype(CardSubtype.Demon).Should().BeTrue();
        g.BasePower.Should().Be(7);
        g.BaseToughness.Should().Be(7);
        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasFlying(g).Should().BeTrue("CR 702.9 — Flying");
        CombatAbilities.HasLifelink(g).Should().BeTrue("CR 702.15 — Lifelink");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Griselbrand()
    {
        var card = NamedCardFactory.Create("Griselbrand", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Griselbrand");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Demon).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(7);
        ((Creature)card).BaseToughness.Should().Be(7);

        // One activated + Flying + Lifelink markers.
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Pay-7-life: Draw-seven activated ability is wired");
        card.Abilities.OfType<KeywordAbility>().Should().Contain(k => k.Keyword == "Flying");
        card.Abilities.OfType<KeywordAbility>().Should().Contain(k => k.Keyword == "Lifelink");
    }

    [Fact]
    public void Griselbrand_ActivatedAbility_HasPay7LifeCost()
    {
        var g = GriselbrandFactory.Create(_alice);
        var act = g.Abilities.OfType<ActivatedAbility>().Single();

        act.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<PayLifeCost>()
            .Which.Amount.Should().Be(GriselbrandFactory.LifeCost)
            .And.Be(7);

        act.IsSorcerySpeed.Should().BeFalse(
            "no sorcery-speed rider on printed Griselbrand — instant-speed by default");
    }

    [Fact]
    public void Griselbrand_PaysLife_AndDrawsSeven()
    {
        // Stock Alice's library with 10 unique sorceries so 7 unique
        // draws are observable without library-exhaustion confounding
        // the test.
        for (var i = 0; i < 10; i++)
        {
            var card = new Sorcery($"Filler {i}", "{1}") { Owner = _alice };
            _alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var g = GriselbrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(g);
        g.SetZone(ZoneType.Battlefield);

        var act = g.Abilities.OfType<ActivatedAbility>().Single();
        var cost = (PayLifeCost)act.Costs[0];

        cost.CanPay(_alice).Should().BeTrue(
            "Alice has 20 life — CR 119.4 gate passes");

        // Pay-then-resolve, modelling SpellCastFlow's cost-payment
        // ordering (CR 601.2h — pay costs, then put on stack, then
        // resolve).
        cost.Pay(_alice);
        foreach (var effect in act.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(13, "20 − 7 paid life");
        _alice.Zones.Hand.GetCards().Should().HaveCount(7,
            "draw seven cards lands in hand");
        _alice.Zones.Library.GetCards().Should().HaveCount(3,
            "10 − 7 drawn = 3 left in library");
    }

    [Fact]
    public void Griselbrand_CantActivate_WithLessThan7Life()
    {
        // CR 119.4 — "you can't pay life you don't have". PayLifeCost.
        // CanPay gates on LifeTotal >= 7; activation is rejected at
        // cost-validation time before the ability hits the stack.
        var lowLifeAlice = new Player("Alice", 6);
        var g = GriselbrandFactory.Create(lowLifeAlice);
        var act = g.Abilities.OfType<ActivatedAbility>().Single();
        var cost = (PayLifeCost)act.Costs[0];

        cost.CanPay(lowLifeAlice).Should().BeFalse(
            "Alice has 6 life — Griselbrand's Pay-7-life can't be activated");
    }
}
