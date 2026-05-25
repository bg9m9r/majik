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
/// Tests for <see cref="BounceLandCycleFactory"/> — the Ravnica /
/// Ravnica Allegiance bounce-land ("Karoo") cycle (10 members):
///
/// "This land enters tapped.
///  When this land enters, return a land you control to its owner's hand.
///  {T}: Add {A}{B}."
///
/// Covers:
/// - 10 land identities + dispatcher routing.
/// - Mana ability produces the right colour pair per member.
/// - ETB bounce trigger: controller picks one land → returned to hand.
/// - ETB bounce with no eligible lands → no-op (CR 608.2b).
/// - ETB-tapped replacement is registered when a <see cref="ReplacementBus"/>
///   is wired (unconditional — bounce lands always enter tapped).
/// </summary>
public class BounceLandCycleTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Cycle membership — 10 lands dispatched through NamedCardFactory
    // -----------------------------------------------------------------------

    public static IEnumerable<object[]> AllBounceLands => new[]
    {
        // cardName, colourA, colourB
        new object[] { "Azorius Chancery",     "W", "U" },
        new object[] { "Boros Garrison",       "R", "W" },
        new object[] { "Dimir Aqueduct",       "U", "B" },
        new object[] { "Golgari Rot Farm",     "B", "G" },
        new object[] { "Gruul Turf",           "R", "G" },
        new object[] { "Izzet Boilerworks",    "U", "R" },
        new object[] { "Orzhov Basilica",      "W", "B" },
        new object[] { "Rakdos Carnarium",     "B", "R" },
        new object[] { "Selesnya Sanctuary",   "G", "W" },
        new object[] { "Simic Growth Chamber", "G", "U" },
    };

    [Theory]
    [MemberData(nameof(AllBounceLands))]
    public void BounceLand_Dispatch_ReturnsLand(string cardName, string _a, string _b)
    {
        _ = _a; _ = _b;

        var card = NamedCardFactory.Create(cardName, _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "bounce lands are nonbasic");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Theory]
    [MemberData(nameof(AllBounceLands))]
    public void BounceLand_HasManaAbility_ProducingColourPair(
        string cardName, string colourA, string colourB)
    {
        var land = (Land)NamedCardFactory.Create(cardName, _alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "single {T}: Add {A}{B} ability");

        var produced = manaAbilities[0].ManaGenerated;
        ColourCount(produced, colourA).Should().BeGreaterThanOrEqualTo(1,
            $"{cardName} produces one {colourA}");
        ColourCount(produced, colourB).Should().BeGreaterThanOrEqualTo(1,
            $"{cardName} produces one {colourB}");
        (produced.White + produced.Blue + produced.Black + produced.Red + produced.Green)
            .Should().Be(2, "exactly one of each colour, no extras");
    }

    [Theory]
    [MemberData(nameof(AllBounceLands))]
    public void BounceLand_HasEtbBounceTrigger(string cardName, string _a, string _b)
    {
        _ = _a; _ = _b;

        var land = (Land)NamedCardFactory.Create(cardName, _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "single ETB bounce trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped replacement (unconditional)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllBounceLands))]
    public void BounceLand_RegistersUnconditionalEtbTappedReplacement_WhenBusWired(
        string cardName, string a, string b)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = BounceLandCycleFactory.Create(
            alice,
            new[] { cardName, a, b },
            zoneService: null,
            eventBus: null,
            triggers: null,
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "bounce lands always enter tapped (CR 614.1c)");
    }

    [Fact]
    public void BounceLand_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only path — single-arg dispatcher constructs without a
        // ReplacementBus, so the land enters untapped on this code path
        // (matches every other always-tapped factory's shape-only posture).
        // No assertion to make on bus-less construction other than that
        // the card builds successfully.
        var land = NamedCardFactory.Create("Azorius Chancery", _alice);
        land.Should().NotBeNull();
        land.Name.Should().Be("Azorius Chancery");
    }

    // -----------------------------------------------------------------------
    // ETB bounce — controller picks one land, returned to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_Bounce_ReturnsAnotherLandControllerControls_ToOwnersHand()
    {
        var chancery = (Land)NamedCardFactory.Create("Azorius Chancery", _alice);
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        _alice.Zones.Battlefield.AddCard(chancery);
        chancery.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);

        var etb = chancery.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(island,
            "the controller's other land returns to its owner's hand");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(island);
        island.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Battlefield.GetCards().Should().Contain(chancery,
            "the bounce land itself stays on the battlefield");
    }

    [Fact]
    public void Etb_Bounce_WithNoOtherLands_IsNoOp()
    {
        // CR 608.2b — no legal pick → effect does nothing.
        var aqueduct = (Land)NamedCardFactory.Create("Dimir Aqueduct", _alice);
        _alice.Zones.Battlefield.AddCard(aqueduct);
        aqueduct.SetZone(ZoneType.Battlefield);

        var etb = aqueduct.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no other lands → nothing to bounce");
        _alice.Zones.Battlefield.GetCards().Should().Contain(aqueduct,
            "the bounce land remains on the battlefield");
    }

    [Fact]
    public void Etb_Bounce_DoesNotPickSelf()
    {
        // The bounce land is on the battlefield when its own ETB resolves;
        // v1 filters self out of the candidate list to match the standard
        // "return another land" reading.
        var growth = (Land)NamedCardFactory.Create("Simic Growth Chamber", _alice);
        _alice.Zones.Battlefield.AddCard(growth);
        growth.SetZone(ZoneType.Battlefield);

        var etb = growth.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(growth,
            "the bounce land never bounces itself");
        _alice.Zones.Battlefield.GetCards().Should().Contain(growth);
    }

    [Fact]
    public void Etb_Bounce_DoesNotBounceOpponentsLand()
    {
        // "Return a land you control" — opponent's lands aren't candidates.
        var bob = new Player("Bob", 20);
        var rotFarm = (Land)NamedCardFactory.Create("Golgari Rot Farm", _alice);
        var bobForest = (Land)NamedCardFactory.Create("Forest", bob);
        _alice.Zones.Battlefield.AddCard(rotFarm);
        rotFarm.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Battlefield);

        var etb = rotFarm.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        bob.Zones.Battlefield.GetCards().Should().Contain(bobForest,
            "opponent's lands aren't eligible for the bounce");
        bob.Zones.Hand.GetCards().Should().NotContain(bobForest);
    }

    [Theory]
    [MemberData(nameof(AllBounceLands))]
    public void Etb_Bounce_AllMembers_BounceFirstControllerLand(
        string cardName, string a, string b)
    {
        _ = a; _ = b;

        var alice = new Player("Alice", 20);
        var bounceLand = (Land)NamedCardFactory.Create(cardName, alice);
        var basic = (Land)NamedCardFactory.Create("Plains", alice);

        alice.Zones.Battlefield.AddCard(bounceLand);
        bounceLand.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(basic);
        basic.SetZone(ZoneType.Battlefield);

        var etb = bounceLand.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        alice.Zones.Hand.GetCards().Should().Contain(basic,
            $"{cardName} returns a controller-owned land to hand");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int ColourCount(ManaCost cost, string colour) => colour switch
    {
        "W" => cost.White,
        "U" => cost.Blue,
        "B" => cost.Black,
        "R" => cost.Red,
        "G" => cost.Green,
        _ => 0,
    };
}
