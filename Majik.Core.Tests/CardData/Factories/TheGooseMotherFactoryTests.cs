using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TheGooseMotherFactory"/> (Wilds of Eldraine,
/// {X}{G}{U}). Legendary Creature — Bird Hydra 2/2.
///
/// Oracle (verified against Scryfall):
///   "Flying
///    The Goose Mother enters with X +1/+1 counters on it.
///    When The Goose Mother enters, create half X Food tokens, rounded up.
///    Whenever The Goose Mother attacks, you may sacrifice a Food. If you
///    do, draw a card."
///
/// Covers:
/// - Identity (Legendary Creature — Bird Hydra, {X}{G}{U}, 2/2, GU colours).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="Card.ManaCostValue.HasX"/> reports true.
/// - Flying keyword marker (CR 702.9).
/// - ETB +1/+1 counters trigger: PendingCastX=4 → 4 counters (CR 122.1g).
/// - ETB Food trigger: half X rounded up (X=4 → 2 Food; X=3 → 2 Food; X=0 → 0).
/// - Attack trigger: with a Food + "yes", sacrifices the Food and draws.
/// - Attack trigger: with no Food, draws nothing (nothing to sacrifice).
/// </summary>
[Trait("Color", "M")]
public class TheGooseMotherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void TheGooseMother_Identity()
    {
        var c = TheGooseMotherFactory.Create(_alice);

        c.Name.Should().Be("The Goose Mother");
        c.ManaCost.Should().Be("{X}{G}{U}");
        c.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("The Goose Mother is Legendary");
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.HasSubtype(CardSubtype.Hydra).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Flying ──────────────────────────────────────────────────────────

    [Fact]
    public void TheGooseMother_HasFlying()
    {
        var c = TheGooseMotherFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Flying")
            .Should().HaveCount(1, "CR 702.9 — Flying is attached as a keyword marker.");
        CombatAbilities.HasFlying(c).Should().BeTrue("The Goose Mother prints Flying (CR 702.9).");
    }

    // ── Trigger shape ───────────────────────────────────────────────────

    [Fact]
    public void TheGooseMother_AttachesThreeTriggers()
    {
        var c = TheGooseMotherFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(3,
            "ETB-counters, ETB-Food, and attacks-sac-Food-draw triggers");
    }

    // ── ETB +1/+1 counters (CR 122.1g) ──────────────────────────────────

    [Fact]
    public void TheGooseMother_EtbWithXEquals4_GainsFourPlusOneCounters()
    {
        var c = TheGooseMotherFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX after ChooseXAsync; simulate.
        c.SetPendingCastX(4);

        var etb = c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("enters with X")));
        foreach (var e in etb.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4,
            "The Goose Mother enters with X (=4) +1/+1 counters per CR 122.1g");
        c.PendingCastX.Should().BeNull("PendingCastX stamp consumed by the ETB counters effect");
    }

    // ── ETB Food: half X, rounded up ────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void TheGooseMother_EtbFood_CreatesHalfXRoundedUp(int x, int expectedFood)
    {
        var c = TheGooseMotherFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.SetPendingCastX(x);

        var foodEtb = c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("Food")));
        foreach (var e in foodEtb.Effects) e.Execute();

        FoodTokens().Should().HaveCount(expectedFood,
            $"create half X (={x}) Food tokens, rounded up (CR 111.10)");
    }

    // ── Attack trigger ──────────────────────────────────────────────────

    [Fact]
    public void TheGooseMother_HasAttackTrigger()
    {
        var c = TheGooseMotherFactory.Create(_alice);

        var attack = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
        attack.Source.Should().BeSameAs(c);
        attack.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AttackTrigger_Matches_OnlyThisCardAttacking()
    {
        var c = TheGooseMotherFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
        var cond = (EventTriggerCondition<CreatureAttacksEvent>)trigger.Condition;

        var bob = new Player("Bob", 20);
        cond.Matches(new CreatureAttacksEvent(c, bob), trigger).Should().BeTrue(
            "this card attacking triggers the ability (CR 508.1f).");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        cond.Matches(new CreatureAttacksEvent(other, bob), trigger).Should().BeFalse(
            "another creature attacking does not trigger this ability.");
    }

    [Fact]
    public void TheGooseMother_AttackEffect_WithFood_SacrificesFoodAndDraws()
    {
        var c = TheGooseMotherFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Stock the library so the draw has a card to take.
        var top = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var food = TokenFactory.CreateFood(_alice);
        food.HasSubtype(CardSubtype.Food).Should().BeTrue();

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var attack = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
        // No agent registered → "you may" auto-takes the upside (sac + draw).
        foreach (var e in attack.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(food,
            "the Food was sacrificed (CR 701.16).");
        _alice.Zones.Graveyard.GetCards().Should().Contain(food);
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "If you do, draw a card (CR 120.2).");
    }

    [Fact]
    public void TheGooseMother_AttackEffect_NoFood_DrawsNothing()
    {
        var c = TheGooseMotherFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var top = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var attack = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
        foreach (var e in attack.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "no Food to sacrifice → \"If you do\" fails → no draw (CR 120.2).");
    }

    private List<Artifact> FoodTokens() =>
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.Name == "Food" && a.IsToken)
            .ToList();
}
