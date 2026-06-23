using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GatewayPlazaFactory"/> — Gateway Plaza (Guilds of
/// Ravnica). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, sacrifice it unless you pay {1}.
///    {T}: Add one mana of any color."
///
/// Functionally identical to Rupture Spire, but printed with the <b>Gate</b>
/// land subtype (Type line "Land — Gate"). Covers:
/// - Identity: Land type + Gate subtype (the one thing that distinguishes it
///   from Rupture Spire).
/// - Five mana abilities (one per WUBRG colour) — any-colour fan-out, NO {C}
///   mode and NO pay-life cost ({T} alone).
/// - One ETB self-trigger (CR 603.6e) carrying the "sacrifice unless you pay
///   {1}" tail (CR 603.1): pays {1} → land stays; can't pay → sacrificed.
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no dispatch test is duplicated here.
/// </summary>
[Trait("Color", "C")]
public class GatewayPlazaFactoryTests
{
    private const string CardName = "Gateway Plaza";

    public static IEnumerable<object[]> AllColors => new[]
    {
        new object[] { "W" },
        new object[] { "U" },
        new object[] { "B" },
        new object[] { "R" },
        new object[] { "G" },
    };

    // -----------------------------------------------------------------------
    // Identity — Land + Gate subtype (the differentiator from Rupture Spire)
    // -----------------------------------------------------------------------

    [Fact]
    public void GatewayPlaza_IsLand_WithGateSubtype()
    {
        var alice = new Player("Alice", 20);

        var land = GatewayPlazaFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be(CardName);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Gate).Should().BeTrue(
            "Gateway Plaza is a \"Land — Gate\"");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — any colour, no {C} mode, no life cost
    // -----------------------------------------------------------------------

    [Fact]
    public void GatewayPlaza_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = GatewayPlazaFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void GatewayPlaza_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = GatewayPlazaFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().NotContain(m =>
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0 &&
                m.ManaGenerated.Generic == 1);
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void GatewayPlaza_TapForMana_DoesNotCostLife(string color)
    {
        // Unlike Mana Confluence / City of Brass, the {T} ability has NO
        // additional life/pain cost — the downside is the ETB tax, not life.
        var alice = new Player("Alice", 20);
        var land = GatewayPlazaFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Activate();

        alice.LifeTotal.Should().Be(20, "tapping for mana costs no life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void GatewayPlaza_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = GatewayPlazaFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // ETB "sacrifice unless you pay {1}" trigger — shape + resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void GatewayPlaza_HasOneEtbTriggeredAbility()
    {
        var alice = new Player("Alice", 20);

        var land = GatewayPlazaFactory.Create(alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "when this land enters, sacrifice it unless you pay {1} → one trigger");
    }

    [Fact]
    public void GatewayPlaza_OnResolve_WithMana_PaysAndStays()
    {
        var alice = new Player("Alice", 20);
        var land = GatewayPlazaFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        alice.AddManaToPool(ManaCost.Parse("1"));

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Battlefield, "the {1} tax was paid");
        alice.Zones.Battlefield.GetCards().Should().Contain(land);
        alice.Zones.Graveyard.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void GatewayPlaza_OnResolve_WithoutMana_SacrificesIt()
    {
        var alice = new Player("Alice", 20);
        var land = GatewayPlazaFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Graveyard, "the {1} tax went unpaid");
        alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        alice.Zones.Graveyard.GetCards().Should().Contain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility FindColoredAbility(Land land, string color)
    {
        var match = ManaCost.Parse(color);
        return land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            m.ManaGenerated.Generic == match.Generic);
    }
}
