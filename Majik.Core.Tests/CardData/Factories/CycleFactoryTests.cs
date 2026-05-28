using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
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

        // CR 117.5 — fetchlands have a three-part real-card cost:
        // {T}, Pay 1 life, Sacrifice this land. The factory must declare all
        // three as proper ICosts (not bury them in the effect closure) so
        // CostPayment runs them once and the stack-resolution effect does
        // only the tutor.
        var costTypes = ability.Costs.OfType<AdditionalCost>().Select(c => c.CostType).ToList();
        costTypes.Should().Contain(AdditionalCostType.Tap);
        costTypes.Should().Contain(AdditionalCostType.PayLife);
        costTypes.Should().Contain(AdditionalCostType.Sacrifice);
    }

    [Theory]
    [MemberData(nameof(AllFetchlands))]
    public void Fetchland_FetchesEitherMatchingSubtype(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB)
    {
        // Stage one basic of each subtype in the library; activating the
        // fetch via CostPayment (sacrifice + life) then resolving its
        // effects (tutor) should move one of them to the battlefield.
        var alice = new Player("Alice", 20);
        var landA = new Land($"Basic-{subtypeA}", new[] { CardSupertype.Basic }, new[] { subtypeA });
        var landB = new Land($"Basic-{subtypeB}", new[] { CardSupertype.Basic }, new[] { subtypeB });
        landA.SetOwner(alice);
        landB.SetOwner(alice);
        alice.Zones.Library.AddCard(landA);
        alice.Zones.Library.AddCard(landB);
        landA.SetZone(ZoneType.Library);
        landB.SetZone(ZoneType.Library);

        var fetch = NamedCardFactory.Create(cardName, alice) as Land;
        fetch.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(fetch!);
        fetch!.SetZone(ZoneType.Battlefield);

        var ability = fetch!.Abilities.OfType<ActivatedAbility>().Single();

        // Drive costs through the real CostPayment pipeline — taps the
        // fetchland, pays 1 life, sacrifices the fetchland to graveyard.
        var payer = new CostPayment();
        payer.PayCosts(alice, ability.Costs);

        // Then the resolve closure tutors a matching land onto the battlefield.
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // One of the two staged basics should now be on the battlefield.
        alice.Zones.Battlefield.GetCards()
            .Where(c => c == landA || c == landB)
            .Should().HaveCount(1,
                because: $"{cardName} fetched a {subtypeA} or {subtypeB} land");

        // Fetch land itself sacrificed (CR 701.16).
        alice.Zones.Graveyard.GetCards().Should().Contain(fetch);

        // Life paid (CR 119.4).
        alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void Fetchland_ActivatedThroughGameFlow_SacrificesSelfAndFetches()
    {
        // End-to-end: stage a fetchland on the battlefield, two basics in
        // the library, activate the fetch ability via AbilityActivator (the
        // production path), then resolve the top of the stack via
        // StackResolver. This exercises the full bug surface — the activator
        // must preserve the effect closure when pushing the wrapper onto the
        // stack, and the resolver must invoke that closure.
        var alice = new Player("Alice", 20);
        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        forest.SetOwner(alice);
        island.SetOwner(alice);
        alice.Zones.Library.AddCard(forest);
        alice.Zones.Library.AddCard(island);
        forest.SetZone(ZoneType.Library);
        island.SetZone(ZoneType.Library);

        var fetch = NamedCardFactory.Create("Misty Rainforest", alice) as Land;
        fetch.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(fetch!);
        fetch!.SetZone(ZoneType.Battlefield);

        var ability = fetch!.Abilities.OfType<ActivatedAbility>().Single();

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var activator = new AbilityActivator(stack, bus);

        // Live activation: pay costs + push onto the stack.
        activator.ActivateAbility(ability, alice, targets: null, costs: ability.Costs);

        stack.Count.Should().Be(1, because: "the fetchland ability is on the stack until resolution");

        // Resolve the top — must invoke the tutor effect closure.
        var resolver = new StackResolver(bus);
        resolver.ResolveTop(stack);

        // Fetchland sacrificed.
        alice.Zones.Graveyard.GetCards().Should().Contain(fetch,
            because: "the Sacrifice additional cost moved Misty Rainforest to the graveyard");

        // One basic should now be on the battlefield.
        alice.Zones.Battlefield.GetCards()
            .Where(c => c == forest || c == island)
            .Should().HaveCount(1,
                because: "Misty Rainforest tutored a Forest or Island onto the battlefield");

        // Life paid.
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
