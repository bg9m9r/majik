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
    // Surveil land cycle — 5 members (DSK / Foundations / MKM)
    // -----------------------------------------------------------------------

    public static IEnumerable<object[]> AllSurveilLands => new[]
    {
        // name, subtypeA, subtypeB, colourA, colourB
        new object[] { "Underground Mortuary", CardSubtype.Swamp,    CardSubtype.Forest,   "B", "G" },
        new object[] { "Lush Portico",         CardSubtype.Forest,   CardSubtype.Plains,   "G", "W" },
        new object[] { "Meticulous Archive",   CardSubtype.Plains,   CardSubtype.Island,   "W", "U" },
        new object[] { "Shadowy Backstreet",   CardSubtype.Plains,   CardSubtype.Swamp,    "W", "B" },
        new object[] { "Thundering Falls",     CardSubtype.Island,   CardSubtype.Mountain, "U", "R" },
        new object[] { "Elegant Parlor",       CardSubtype.Mountain, CardSubtype.Plains,   "R", "W" },
    };

    [Theory]
    [MemberData(nameof(AllSurveilLands))]
    public void SurveilLand_Dispatch_ReturnsLandWithPrintedNameAndDualSubtypes(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB, string colorA, string colorB)
    {
        _ = colorA; _ = colorB;

        var card = NamedCardFactory.Create(cardName, _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
        var land = (Land)card;
        // CR 305.6 — surveil lands print as Land — TypeA TypeB; both subtypes
        // must be on the card so fetchland subtype searches (and effects like
        // Yavimaya / Urborg) treat them as real duals.
        land.HasSubtype(subtypeA).Should().BeTrue(because: $"{cardName} is a {subtypeA}");
        land.HasSubtype(subtypeB).Should().BeTrue(because: $"{cardName} is a {subtypeB}");
    }

    [Theory]
    [MemberData(nameof(AllSurveilLands))]
    public void SurveilLand_HasTwoManaAbilities_OnePerProducedColour(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB, string colorA, string colorB)
    {
        _ = subtypeA; _ = subtypeB;

        var land = NamedCardFactory.Create(cardName, _alice);

        var manaAbilities = land.Abilities.OfType<Majik.Core.Abilities.ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            because: $"{cardName} has \"{{T}}: Add {{{colorA}}} or {{{colorB}}}\" — one mana ability per colour");

        // Each mana ability produces exactly one of the named colours.
        var producedSymbols = manaAbilities
            .Select(a => SingleColorOf(a.ManaGenerated))
            .OrderBy(s => s)
            .ToList();
        var expected = new[] { colorA, colorB }.OrderBy(s => s).ToList();
        producedSymbols.Should().Equal(expected,
            because: $"{cardName} produces {{{colorA}}} and {{{colorB}}}");
    }

    [Theory]
    [MemberData(nameof(AllSurveilLands))]
    public void SurveilLand_HasOneEtbTriggeredAbility_ForSurveil1(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB, string colorA, string colorB)
    {
        _ = subtypeA; _ = subtypeB; _ = colorA; _ = colorB;

        var land = NamedCardFactory.Create(cardName, _alice);

        var triggers = land.Abilities.OfType<Majik.Core.Abilities.TriggeredAbility>().ToList();
        triggers.Should().ContainSingle(
            because: $"{cardName}'s only triggered ability is \"When this land enters, surveil 1.\"");
    }

    [Theory]
    [MemberData(nameof(AllSurveilLands))]
    public void SurveilLand_EtbTriggerEffect_PeeksOneCardFromLibrary_DefaultsAllToGraveyard(
        string cardName, CardSubtype subtypeA, CardSubtype subtypeB, string colorA, string colorB)
    {
        _ = subtypeA; _ = subtypeB; _ = colorA; _ = colorB;

        var alice = new Player("Alice", 20);
        var top = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
        {
            Owner = alice, Controller = alice,
        };
        alice.Zones.Library.AddCard(top);

        var land = NamedCardFactory.Create(cardName, alice);
        var trigger = land.Abilities.OfType<Majik.Core.Abilities.TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Default decision (no agent registered) sends every peeked card to
        // the graveyard. Surveil 1 → top library card moves to GY.
        alice.Zones.Library.GetCards().Should().NotContain(top);
        alice.Zones.Graveyard.GetCards().Should().Contain(top,
            because: "no agent registered → default surveil decision is all-to-graveyard");
    }

    /// <summary>
    /// Pluck the single-symbol colour from a <see cref="ManaCost"/> the
    /// surveil-land mana ability produces. Throws when the cost has more
    /// than one symbol — surveil lands never produce hybrid / multi-symbol
    /// mana abilities in v1.
    /// </summary>
    private static string SingleColorOf(Majik.Core.ValueObjects.ManaCost? cost)
    {
        if (cost == null) throw new InvalidOperationException("ManaCost is null");
        if (cost.White > 0) return "W";
        if (cost.Blue  > 0) return "U";
        if (cost.Black > 0) return "B";
        if (cost.Red   > 0) return "R";
        if (cost.Green > 0) return "G";
        return "C";
    }

    // -----------------------------------------------------------------------
    // Horizon-land cycle — 3 members shipped (Horizon Canopy, Fiery Islet,
    // Sunbaked Canyon)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Horizon Canopy")]
    [InlineData("Fiery Islet")]
    [InlineData("Sunbaked Canyon")]
    public void HorizonLand_Dispatch_ReturnsLandWithPrintedName(string cardName)
    {
        var card = NamedCardFactory.Create(cardName, _alice);
        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
    }

    [Theory]
    [InlineData("Horizon Canopy")]
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
