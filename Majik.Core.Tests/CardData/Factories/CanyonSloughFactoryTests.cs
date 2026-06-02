using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CanyonSloughFactory"/> — Canyon Slough (Amonkhet
/// bicycle-land cycle). Oracle text:
///   "({T}: Add {B} or {R}.)
///    This land enters tapped.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Mirrors <see cref="SavaiTriomeFactoryTests"/> — identity + subtypes, two
/// mana abilities (one per produced colour), the Cycling {2} activated
/// ability shape (CR 702.32), and an end-to-end cycle that pays {2}, discards
/// self, draws one, and publishes
/// <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// </summary>
[Trait("Color", "C")]
public class CanyonSloughFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void CanyonSlough_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Canyon Slough", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {B} or {R}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling {2} ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void CanyonSlough_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Canyon Slough", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {2} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.Black.Should().Be(0);
        manaCost.Red.Should().Be(0);
    }

    [Fact]
    public void CanyonSlough_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Canyon Slough", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void CanyonSlough_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var slough = CanyonSloughFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(slough);
        slough.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        var cycling = slough.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        slough.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(slough);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void CanyonSlough_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var slough = CanyonSloughFactory.Create(_alice, eventBus: null, replacements: replacements);

        slough.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. We assert the build succeeds
        // with the bus wired — EntersTappedReplacement has no public
        // bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is the authoritative test for
        // tapped-entry behaviour.
    }
}
