using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GildedGooseFactory"/>.
///
/// Gilded Goose (Throne of Eldraine, {G}). Creature — Bird 0/2.
/// Oracle (verified against Scryfall):
///   "Flying
///    When this creature enters, create a Food token.
///    {1}{G}, {T}: Create a Food token.
///    {T}, Sacrifice a Food: Add one mana of any color."
///
/// Coverage:
/// - Identity (name, type, Bird subtype, cost, colour, P/T, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying keyword marker (CR 702.9).
/// - One ETB <see cref="TriggeredAbility"/> over a CardMovedEvent to the
///   battlefield, gated to this card; resolving it mints one Food token.
/// - "{1}{G}, {T}: Create a Food token" activated ability shape + resolution.
/// - Five sacrifice-a-Food mana abilities (one per WUBRG); activation
///   sacrifices a Food token + produces one mana of the chosen colour;
///   cannot activate with no Food available.
/// </summary>
[Trait("Color", "G")]
public class GildedGooseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void GildedGoose_Identity()
    {
        var c = GildedGooseFactory.Create(_alice);

        c.Name.Should().Be("Gilded Goose");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.ManaCost.Should().Be("{G}");
        c.ManaCostValue.TotalValue.Should().Be(1);
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Flying ──────────────────────────────────────────────────────────

    [Fact]
    public void GildedGoose_HasFlying()
    {
        var c = GildedGooseFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Flying")
            .Should().HaveCount(1, "CR 702.9 — Flying is attached as a keyword marker.");
        CombatAbilities.HasFlying(c).Should().BeTrue("Gilded Goose prints Flying (CR 702.9).");
    }

    // ── ETB trigger — structural ────────────────────────────────────────

    [Fact]
    public void GildedGoose_HasOneEtbTrigger()
    {
        var card = GildedGooseFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the ETB Food-token trigger is attached.");
        triggers[0].Source.Should().BeSameAs(card);
        triggers[0].Controller.Should().BeSameAs(_alice);
        triggers[0].Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
    }

    [Fact]
    public void EtbTrigger_Matches_OnlyThisCardEnteringBattlefield()
    {
        var card = GildedGooseFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var cond = (EventTriggerCondition<CardMovedEvent>)trigger.Condition;

        cond.Matches(
            new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeTrue("this card entering the battlefield triggers the ability.");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeFalse("another creature entering does not trigger this ability.");

        cond.Matches(
            new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard), trigger)
            .Should().BeFalse("leaving the battlefield does not trigger the ETB.");
    }

    [Fact]
    public void GildedGoose_EtbEffect_CreatesFoodUnderController()
    {
        var goose = GildedGooseFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goose);
        goose.SetZone(ZoneType.Battlefield);

        var trigger = goose.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        FoodTokens().Should().HaveCount(1, "the ETB effect creates one Food token (CR 111.10).");
    }

    // ── "{1}{G}, {T}: Create a Food token" activated ability ────────────

    [Fact]
    public void GildedGoose_HasCreateFoodActivatedAbility_WithManaAndTapCost()
    {
        var goose = GildedGooseFactory.Create(_alice);

        var ability = goose.Abilities.OfType<ActivatedAbility>().Single();
        ability.Source.Should().BeSameAs(goose);
        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "the activated ability costs {1}{G}.");
        ability.Costs.OfType<AdditionalCost>().Should().HaveCount(1,
            "the activated ability includes {T}.");
    }

    [Fact]
    public void GildedGoose_CreateFoodEffect_MintsOneFood()
    {
        var goose = GildedGooseFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goose);
        goose.SetZone(ZoneType.Battlefield);

        var ability = goose.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        FoodTokens().Should().HaveCount(1,
            "resolving the activated ability creates one Food token.");
    }

    // ── "{T}, Sacrifice a Food: Add one mana of any color" ──────────────

    [Fact]
    public void GildedGoose_HasFiveManaAbilities_OnePerColor()
    {
        var goose = GildedGooseFactory.Create(_alice);
        goose.Abilities.OfType<GildedGooseManaAbility>().Should().HaveCount(5);
    }

    [Fact]
    public void GildedGoose_ManaAbilities_ShareSacrificeCost()
    {
        var goose = GildedGooseFactory.Create(_alice);
        var abilities = goose.Abilities.OfType<GildedGooseManaAbility>().ToList();
        var first = abilities[0].SacrificeChoice;
        foreach (var ab in abilities)
        {
            ab.SacrificeChoice.Should().BeSameAs(first,
                "a single SacrificeAFoodCost is shared across all five colour abilities.");
        }
    }

    [Fact]
    public void GildedGoose_AllFiveColors_Producible()
    {
        var goose = GildedGooseFactory.Create(_alice);
        var abilities = goose.Abilities.OfType<GildedGooseManaAbility>().ToList();

        abilities.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }

    [Fact]
    public void GildedGoose_ManaAbility_CannotActivate_WhenNoFood()
    {
        var goose = GildedGooseFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goose);
        goose.SetZone(ZoneType.Battlefield);
        goose.ClearSummoningSickness();

        var ability = goose.Abilities.OfType<GildedGooseManaAbility>().First();
        ability.CanActivate().Should().BeFalse(
            "no Food on the battlefield to sacrifice.");
    }

    [Fact]
    public void GildedGoose_ManaAbility_Activate_SacrificesFood_TapsGoose_ProducesMana()
    {
        var goose = GildedGooseFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goose);
        goose.SetZone(ZoneType.Battlefield);
        goose.ClearSummoningSickness();

        // A Food token to feed the ability.
        var food = TokenFactory.CreateFood(_alice);
        food.HasSubtype(CardSubtype.Food).Should().BeTrue();

        var blueAbility = goose.Abilities
            .OfType<GildedGooseManaAbility>()
            .First(a => a.ManaGenerated.Blue == 1);

        blueAbility.CanActivate().Should().BeTrue();
        var mana = blueAbility.Activate();

        mana.Blue.Should().Be(1);
        mana.Generic.Should().Be(0);
        goose.IsTapped.Should().BeTrue(
            "the ability's {T} taps Gilded Goose (CR 605.1).");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(food);
        _alice.Zones.Graveyard.GetCards().Should().Contain(food);
    }

    private List<Artifact> FoodTokens() =>
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.Name == "Food" && a.IsToken)
            .ToList();
}
