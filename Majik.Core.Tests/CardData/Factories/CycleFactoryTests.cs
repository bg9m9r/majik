using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the parametric cycle factories introduced when consolidating
/// the per-card fetchland and horizon-land factory files.
///
/// Each cycle factory class wears multiple <c>[CardName("...", payload...)]</c>
/// attributes; the source generator dispatches each printed name to the
/// shared <c>Create(Player, string[])</c> overload with the args array
/// shaped as <c>[name, ...payload]</c>.
/// </summary>
public class CycleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Fetchland cycle — 10 members, dispatched via the shared factory
    // -----------------------------------------------------------------------

    public static IEnumerable<object[]> AllFetchlands => new[]
    {
        new object[] { "Bloodstained Mire", CardSubtype.Swamp,    CardSubtype.Mountain },
        new object[] { "Arid Mesa",         CardSubtype.Plains,   CardSubtype.Mountain },
        new object[] { "Wooded Foothills",  CardSubtype.Mountain, CardSubtype.Forest   },
        new object[] { "Polluted Delta",    CardSubtype.Island,   CardSubtype.Swamp    },
        new object[] { "Windswept Heath",   CardSubtype.Forest,   CardSubtype.Plains   },
        new object[] { "Scalding Tarn",     CardSubtype.Island,   CardSubtype.Mountain },
        new object[] { "Misty Rainforest",  CardSubtype.Forest,   CardSubtype.Island   },
        new object[] { "Flooded Strand",    CardSubtype.Plains,   CardSubtype.Island   },
        new object[] { "Verdant Catacombs", CardSubtype.Swamp,    CardSubtype.Forest   },
        new object[] { "Marsh Flats",       CardSubtype.Plains,   CardSubtype.Swamp    },
    };

    [Theory]
    [MemberData(nameof(AllFetchlands))]
    public void Fetchland_Dispatch_ReturnsLandWithPrintedName(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB)
    {
        _ = subtypeA; _ = subtypeB;

        var card = NamedCardFactory.Create(cardName, _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
    }

    [Theory]
    [MemberData(nameof(AllFetchlands))]
    public void Fetchland_HasFetchActivatedAbility(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB)
    {
        _ = subtypeA; _ = subtypeB;

        var land = NamedCardFactory.Create(cardName, _alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap);
    }

    [Theory]
    [MemberData(nameof(AllFetchlands))]
    public void Fetchland_FetchesEitherMatchingSubtype(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB)
    {
        // Stage one basic of each subtype in the library; activating the
        // fetch should move one of them to the battlefield.
        var alice = new Player("Alice", 20);
        var landA = new Land($"Basic-{subtypeA}", new[] { CardSupertype.Basic }, new[] { subtypeA });
        var landB = new Land($"Basic-{subtypeB}", new[] { CardSupertype.Basic }, new[] { subtypeB });
        alice.Zones.Library.AddCard(landA);
        alice.Zones.Library.AddCard(landB);
        landA.SetZone(ZoneType.Library);
        landB.SetZone(ZoneType.Library);

        var fetch = NamedCardFactory.Create(cardName, alice) as Land;
        fetch.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(fetch!);
        fetch!.SetZone(ZoneType.Battlefield);

        var ability = fetch!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // One of the two staged basics should now be on the battlefield.
        alice.Zones.Battlefield.GetCards()
            .Where(c => c == landA || c == landB)
            .Should().HaveCount(1,
                because: $"{cardName} fetched a {subtypeA} or {subtypeB} land");

        // Fetch land itself sacrificed.
        alice.Zones.Graveyard.GetCards().Should().Contain(fetch);

        // Life paid (CR 119.4).
        alice.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // Horizon-land cycle — 2 members shipped (Fiery Islet, Sunbaked Canyon)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Fiery Islet")]
    [InlineData("Sunbaked Canyon")]
    public void HorizonLand_Dispatch_ReturnsLandWithPrintedName(string cardName)
    {
        var card = NamedCardFactory.Create(cardName, _alice);
        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
    }

    [Theory]
    [InlineData("Fiery Islet")]
    [InlineData("Sunbaked Canyon")]
    public void HorizonLand_HasManaAndSacDrawAbilities(string cardName)
    {
        var land = NamedCardFactory.Create(cardName, _alice);

        // Two pay-life mana abilities + one sac-draw activated ability.
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            because: $"{cardName} produces two colours via pay-life mana abilities");
        land.Abilities.OfType<ActivatedAbility>().Should().NotBeEmpty(
            because: $"{cardName} has a {{1}}, {{T}}, Sacrifice: Draw a card ability");
    }
}
