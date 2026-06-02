using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DesertOfTheMindfulFactory"/> — the Amonkhet
/// mono-blue cycling Desert ("Land — Desert", enters tapped, {T}: Add {U},
/// Cycling {1}{U}).
///
/// Covers:
/// - Identity (Land + Desert subtype).
/// - {T}: Add {U} mana ability.
/// - Cycling ability shape (ManaCostCost + DiscardSelfCost via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - Cycling cost is {1}{U} specifically.
/// - End-to-end cycle: pays {1}{U}, discards self, draws one card,
///   publishes <see cref="Majik.Core.Events.CardCycledEvent"/>.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "U")]
public class DesertOfTheMindfulFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertOfTheMindful_IsLandWithDesertSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Mindful", _alice);

        land.Name.Should().Be("Desert of the Mindful");
        land.Subtypes.Should().Contain(CardSubtype.Desert);
    }

    [Fact]
    public void DesertOfTheMindful_HasManaAbilityProducingBlue()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Mindful", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Blue.Should().Be(1, "{T}: Add {U}");
        mana.ManaGenerated.White.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertOfTheMindful_HasCyclingActivatedAbility_WithManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Mindful", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);
    }

    [Fact]
    public void DesertOfTheMindful_Cycling_ChargesGenericAndBlueManaSpecifically()
    {
        // CR 702.32 — Desert of the Mindful's printed cycling cost is {1}{U}.
        var land = (Land)NamedCardFactory.Create("Desert of the Mindful", _alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var cycling = land.Abilities.OfType<ActivatedAbility>().Single();
        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;

        mana.Blue.Should().Be(1, "cycling {1}{U} includes one {U}");
        mana.Generic.Should().Be(1, "cycling {1}{U} includes one generic");
        mana.White.Should().Be(0);
    }

    [Fact]
    public void DesertOfTheMindful_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Mindful", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {1}{U}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertOfTheMindful_Cycling_EndToEnd_PaysOneUDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Opt", "{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var desert = DesertOfTheMindfulFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(desert);
        desert.SetZone(ZoneType.Hand);

        // {1}{U} — one generic + one blue.
        _alice.AddManaToPool(ManaCost.Parse("{1}{U}"));

        var cycling = desert.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        desert.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(desert);
    }
}
