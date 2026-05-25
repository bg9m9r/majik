using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FastLandCycleFactory"/> — the Scars of Mirrodin
/// allied half of the fast-land cycle (Blackcleave Cliffs, Copperline
/// Gorge, Darkslick Shores, Razorverge Thicket, Seachrome Coast).
///
/// Oracle (canonical, all 5):
/// "This land enters tapped unless you control two or fewer other lands.
///  {T}: Add {A} or {B}."
///
/// Covers, per cycle member:
/// - Identity (Land type, printed name, owner/controller wiring,
///   non-Basic, non-Legendary).
/// - Two mana abilities producing the right coloured pair.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>:
///   0 other lands → untapped; 2 other lands → untapped (boundary);
///   3 other lands → tapped; self is excluded from the count.
/// - <see cref="NamedCardFactory"/> dispatch resolves each printed name.
/// - Args validation: null owner, too few args.
/// </summary>
public class FastLandCycleTests
{
    /// <summary>
    /// All 5 Scars fastlands with their canonical colour-pair args.
    /// </summary>
    public static IEnumerable<object[]> AllFastLands => new[]
    {
        // cardName, colourA, colourB
        new object[] { "Blackcleave Cliffs", "B", "R" },
        new object[] { "Copperline Gorge",   "R", "G" },
        new object[] { "Darkslick Shores",   "U", "B" },
        new object[] { "Razorverge Thicket", "G", "W" },
        new object[] { "Seachrome Coast",    "W", "U" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_IsLand_WithCorrectName(
        string cardName, string colourA, string colourB)
    {
        var alice = new Player("Alice", 20);

        var land = FastLandCycleFactory.Create(alice, new[] { cardName, colourA, colourB });

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(cardName);
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_IsNotBasic_NotLegendary(
        string cardName, string colourA, string colourB)
    {
        var alice = new Player("Alice", 20);

        var land = FastLandCycleFactory.Create(alice, new[] { cardName, colourA, colourB });

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "fastlands are nonbasic / typeless duals");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_Dispatch_ResolvesViaNamedCardFactory(
        string cardName, string colourA, string colourB)
    {
        _ = colourA; _ = colourB;
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(cardName, alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_HasTwoColouredManaAbilities(
        string cardName, string colourA, string colourB)
    {
        var alice = new Player("Alice", 20);

        var land = FastLandCycleFactory.Create(alice, new[] { cardName, colourA, colourB });

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "one ManaAbility per produced colour (A and B)");

        var matchA = ManaCost.Parse(colourA);
        var matchB = ManaCost.Parse(colourB);
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, matchA),
            $"{cardName} produces {{{colourA}}}");
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, matchB),
            $"{cardName} produces {{{colourB}}}");
    }

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_HasNoActivatedOrTriggeredAbilities(
        string cardName, string colourA, string colourB)
    {
        var alice = new Player("Alice", 20);

        var land = FastLandCycleFactory.Create(alice, new[] { cardName, colourA, colourB });

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "fastlands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "fastlands have no triggered abilities — the ETB-tapped clause is a replacement effect");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c — "two or fewer other lands")
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_EntersUntapped_WhenControllerHasZeroOtherLands(
        string cardName, string colourA, string colourB)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = FastLandCycleFactory.Create(
            alice,
            new[] { cardName, colourA, colourB },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            $"{cardName} enters untapped when controller has 0 other lands (0 ≤ 2)");
    }

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_EntersUntapped_WhenControllerHasTwoOtherLands(
        string cardName, string colourA, string colourB)
    {
        // 2 other lands is the boundary — "two or fewer" includes 2.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedBasicLands(alice, count: 2);

        var land = FastLandCycleFactory.Create(
            alice,
            new[] { cardName, colourA, colourB },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            $"{cardName} enters untapped when controller has exactly 2 other lands (boundary)");
    }

    [Theory]
    [MemberData(nameof(AllFastLands))]
    public void FastLand_EntersTapped_WhenControllerHasThreeOtherLands(
        string cardName, string colourA, string colourB)
    {
        // 3 other lands fails the "two or fewer" check ⇒ enters tapped.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedBasicLands(alice, count: 3);

        var land = FastLandCycleFactory.Create(
            alice,
            new[] { cardName, colourA, colourB },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"{cardName} enters tapped when controller has 3 other lands (3 > 2)");
    }

    [Fact]
    public void FastLand_PredicateExcludesSelf()
    {
        // The fastland itself must not be counted toward "other lands".
        // Place 2 other lands plus the fastland on the battlefield — the
        // predicate should see 2 (not 3) when it tallies the controller's
        // other lands at ETB time.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedBasicLands(alice, count: 2);

        var fastland = FastLandCycleFactory.Create(
            alice,
            new[] { "Blackcleave Cliffs", "B", "R" },
            replacements: bus);

        // Simulate the card already being on the battlefield to prove the
        // "self" exclusion in the predicate (CountOtherLands filters by
        // reference equality, mirroring ConditionalEntersTappedBinder).
        alice.Zones.Battlefield.AddCard(fastland);
        fastland.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: fastland,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "the fastland itself must be excluded from the other-lands count (2 other lands ≤ 2)");
    }

    [Fact]
    public void FastLand_OpponentLandsDoNotCount()
    {
        // "you control" — opponent's lands don't satisfy or fail the
        // predicate; they're simply not visible.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        SeedBasicLands(bob, count: 5); // opponent has 5 lands, irrelevant.

        var cliffs = FastLandCycleFactory.Create(
            alice,
            new[] { "Blackcleave Cliffs", "B", "R" },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: cliffs,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Alice controls 0 other lands; Bob's 5 lands don't count toward Alice's tally");
    }

    [Fact]
    public void FastLand_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — single-arg dispatcher constructs
        // without a ReplacementBus, so the ETB-tapped predicate is not
        // wired. Matches every other ETB-replacement factory's
        // shape-only posture (see CheckLandCycleFactory).
        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Blackcleave Cliffs", alice);
        land.Should().NotBeNull();
        land.Name.Should().Be("Blackcleave Cliffs");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void FastLand_Create_ThrowsOnNullOwner()
    {
        var act = () => FastLandCycleFactory.Create(
            null!,
            new[] { "Blackcleave Cliffs", "B", "R" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FastLand_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => FastLandCycleFactory.Create(
            alice,
            new[] { "Blackcleave Cliffs", "B" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*FastLandCycleFactory needs args*");
    }

    [Fact]
    public void FastLand_FallbackOverload_BuildsBlackcleaveCliffs()
    {
        var alice = new Player("Alice", 20);

        var land = FastLandCycleFactory.Create(alice);

        land.Name.Should().Be("Blackcleave Cliffs");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Seed <paramref name="count"/> plain Lands on <paramref name="player"/>'s
    /// battlefield. The fastland predicate is type-based ("other lands"), not
    /// subtype-based, so any Land instance suffices — using bare
    /// <see cref="Land"/>s keeps the test independent of the basic-land
    /// factory.
    /// </summary>
    private static void SeedBasicLands(Player player, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var swamp = new Land($"Test Land {i}", supertypes: null, subtypes: null);
            swamp.SetOwner(player);
            swamp.SetController(player);
            player.Zones.Battlefield.AddCard(swamp);
            swamp.SetZone(ZoneType.Battlefield);
        }
    }

    private static bool SameCost(ManaCost a, ManaCost b) =>
        a.White == b.White &&
        a.Blue == b.Blue &&
        a.Black == b.Black &&
        a.Red == b.Red &&
        a.Green == b.Green &&
        a.Generic == b.Generic;
}
