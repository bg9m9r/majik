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
/// Tests for <see cref="CheckLandCycleFactory"/> — the 10-card Magic 2010
/// allied + Innistrad enemy "check land" cycle.
///
/// Oracle (canonical, all 10):
/// "This land enters tapped unless you control a [Basic A] or a [Basic B].
///  {T}: Add {A} or {B}."
///
/// Covers, per cycle member:
/// - Identity (Land type, printed name, owner/controller wiring,
///   non-Basic, non-Legendary).
/// - Two mana abilities producing the right coloured pair.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>:
///   no matching basic → tapped; matching basic A → untapped; matching
///   basic B → untapped; only opponent's matching basic → tapped (predicate
///   reads CONTROLLER's battlefield).
/// - <see cref="NamedCardFactory"/> dispatch resolves each printed name.
/// - Args validation: null owner, too few args, unknown subtype.
/// </summary>
public class CheckLandCycleTests
{
    /// <summary>
    /// All 10 check lands with their canonical subtype + colour args.
    /// </summary>
    public static IEnumerable<object[]> AllCheckLands => new[]
    {
        // cardName, basicA, basicB, colourA, colourB
        new object[] { "Glacial Fortress",   "Plains",   "Island",   "W", "U" },
        new object[] { "Drowned Catacomb",   "Island",   "Swamp",    "U", "B" },
        new object[] { "Dragonskull Summit", "Swamp",    "Mountain", "B", "R" },
        new object[] { "Rootbound Crag",     "Mountain", "Forest",   "R", "G" },
        new object[] { "Sunpetal Grove",     "Forest",   "Plains",   "G", "W" },
        new object[] { "Isolated Chapel",    "Plains",   "Swamp",    "W", "B" },
        new object[] { "Clifftop Retreat",   "Mountain", "Plains",   "R", "W" },
        new object[] { "Hinterland Harbor",  "Forest",   "Island",   "G", "U" },
        new object[] { "Sulfur Falls",       "Island",   "Mountain", "U", "R" },
        new object[] { "Woodland Cemetery",  "Swamp",    "Forest",   "B", "G" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_IsLand_WithCorrectName(
        string cardName, string a, string b, string ca, string cb)
    {
        var alice = new Player("Alice", 20);

        var land = CheckLandCycleFactory.Create(alice, new[] { cardName, a, b, ca, cb });

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(cardName);
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_IsNotBasic_NotLegendary(
        string cardName, string a, string b, string ca, string cb)
    {
        var alice = new Player("Alice", 20);

        var land = CheckLandCycleFactory.Create(alice, new[] { cardName, a, b, ca, cb });

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "check lands are nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_Dispatch_ResolvesViaNamedCardFactory(
        string cardName, string a, string b, string ca, string cb)
    {
        _ = a; _ = b; _ = ca; _ = cb;
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(cardName, alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_HasTwoColouredManaAbilities(
        string cardName, string a, string b, string colourA, string colourB)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var land = CheckLandCycleFactory.Create(alice, new[] { cardName, a, b, colourA, colourB });

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
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_HasNoActivatedOrTriggeredAbilities(
        string cardName, string a, string b, string ca, string cb)
    {
        var alice = new Player("Alice", 20);

        var land = CheckLandCycleFactory.Create(alice, new[] { cardName, a, b, ca, cb });

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "check lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "check lands have no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_EntersTapped_WhenControllerHasNoMatchingBasic(
        string cardName, string a, string b, string ca, string cb)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = CheckLandCycleFactory.Create(
            alice,
            new[] { cardName, a, b, ca, cb },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"{cardName} enters tapped when controller has no {a} or {b}");
    }

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_EntersUntapped_WhenControllerHasMatchingBasicA(
        string cardName, string a, string b, string ca, string cb)
    {
        _ = b;
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        // Seed a controller-owned basic of subtype A.
        var basicA = (Land)NamedCardFactory.Create(a, alice);
        alice.Zones.Battlefield.AddCard(basicA);
        basicA.SetZone(ZoneType.Battlefield);

        var land = CheckLandCycleFactory.Create(
            alice,
            new[] { cardName, a, b, ca, cb },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            $"{cardName} enters untapped when controller has a {a}");
    }

    [Theory]
    [MemberData(nameof(AllCheckLands))]
    public void CheckLand_EntersUntapped_WhenControllerHasMatchingBasicB(
        string cardName, string a, string b, string ca, string cb)
    {
        _ = a;
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var basicB = (Land)NamedCardFactory.Create(b, alice);
        alice.Zones.Battlefield.AddCard(basicB);
        basicB.SetZone(ZoneType.Battlefield);

        var land = CheckLandCycleFactory.Create(
            alice,
            new[] { cardName, a, b, ca, cb },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            $"{cardName} enters untapped when controller has a {b}");
    }

    [Fact]
    public void CheckLand_EntersTapped_WhenOnlyOpponentHasMatchingBasic()
    {
        // "you control" — opponent's basics don't satisfy the predicate.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bobPlains = (Land)NamedCardFactory.Create("Plains", bob);
        bob.Zones.Battlefield.AddCard(bobPlains);
        bobPlains.SetZone(ZoneType.Battlefield);

        var fortress = CheckLandCycleFactory.Create(
            alice,
            new[] { "Glacial Fortress", "Plains", "Island", "W", "U" },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: fortress,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Glacial Fortress enters tapped when only the opponent has a Plains");
    }

    [Fact]
    public void CheckLand_PredicateExcludesSelf()
    {
        // Check lands have no printed basic subtype, so the "exclude self"
        // filter is structurally a no-op for this cycle, but we exercise
        // the path to lock the contract in.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var fortress = CheckLandCycleFactory.Create(
            alice,
            new[] { "Glacial Fortress", "Plains", "Island", "W", "U" },
            replacements: bus);

        // The check land is on the battlefield at predicate time but it
        // doesn't carry the Plains / Island subtype itself, so the
        // controller still has zero matching subtypes.
        alice.Zones.Battlefield.AddCard(fortress);
        fortress.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: fortress,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the check land's own subtypes don't satisfy the predicate (it isn't a Plains / Island)");
    }

    [Fact]
    public void CheckLand_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — single-arg dispatcher constructs
        // without a ReplacementBus, so the ETB-tapped predicate is not
        // wired. Matches every other ETB-replacement factory's
        // shape-only posture.
        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Glacial Fortress", alice);
        land.Should().NotBeNull();
        land.Name.Should().Be("Glacial Fortress");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void CheckLand_Create_ThrowsOnNullOwner()
    {
        var act = () => CheckLandCycleFactory.Create(
            null!,
            new[] { "Glacial Fortress", "Plains", "Island", "W", "U" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckLand_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => CheckLandCycleFactory.Create(
            alice,
            new[] { "Glacial Fortress", "Plains", "Island", "W" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*CheckLandCycleFactory needs args*");
    }

    [Fact]
    public void CheckLand_Create_ThrowsOnUnknownSubtype()
    {
        var alice = new Player("Alice", 20);

        var act = () => CheckLandCycleFactory.Create(
            alice,
            new[] { "Glacial Fortress", "NotASubtype", "Island", "W", "U" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*unknown basic subtype*");
    }

    [Fact]
    public void CheckLand_FallbackOverload_BuildsGlacialFortress()
    {
        var alice = new Player("Alice", 20);

        var land = CheckLandCycleFactory.Create(alice);

        land.Name.Should().Be("Glacial Fortress");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool SameCost(ManaCost a, ManaCost b) =>
        a.White == b.White &&
        a.Blue == b.Blue &&
        a.Black == b.Black &&
        a.Red == b.Red &&
        a.Green == b.Green &&
        a.Generic == b.Generic;
}
