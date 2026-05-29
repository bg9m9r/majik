using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SignetCycleFactory"/> — the Ravnica guild "Signet"
/// two-colour artifact mana-rock cycle.
///
/// Each member shares the same printed shape (CR 605.1 mana ability):
/// <code>
/// Artifact {2}.
/// {1}, {T}: Add {A}{B}.
/// </code>
///
/// Only the produced colour pair (A, B) differs, so one factory handles the
/// whole 10-card cycle. Modelled exactly like
/// <see cref="FilterLandCycleFactory"/>'s filter mode: the {T} tap is paid by
/// the default tap path; the {1} extra cost is paid from the controller's mana
/// pool via <c>additionalCostPayer = p => p.PayMana({1})</c>; both colour pips
/// are emitted at once via a single <see cref="ManaAbility"/> producing {A}{B}.
///
/// Covers, per cycle member:
/// - Identity (Artifact type, {2} mana cost, printed name, owner/controller).
/// - Exactly one mana ability that produces both colour pips at once.
/// - Activating deducts {1} from the controller's mana pool and taps the
///   signet (CR 605.1 — paid atomically with the {T} tap).
/// - Cannot activate without {1} available, nor once tapped.
/// - Dispatch through <see cref="NamedCardFactory"/> resolves each printed
///   name to the parametric Create overload.
/// </summary>
public class SignetCycleFactoryTests
{
    /// <summary>
    /// All 10 signets with their canonical produced colour pair.
    /// First five: Ravnica: City of Guilds allied (WU, UB, BR, RG, GW).
    /// Last five: Guildpact / Dissension enemy (WB, GU, BG, WR, UR).
    /// </summary>
    public static IEnumerable<object[]> AllSignets => new[]
    {
        new object[] { "Azorius Signet",  "W", "U" },
        new object[] { "Dimir Signet",    "U", "B" },
        new object[] { "Rakdos Signet",   "B", "R" },
        new object[] { "Gruul Signet",    "R", "G" },
        new object[] { "Selesnya Signet", "G", "W" },
        new object[] { "Orzhov Signet",   "W", "B" },
        new object[] { "Simic Signet",    "G", "U" },
        new object[] { "Golgari Signet",  "B", "G" },
        new object[] { "Boros Signet",    "W", "R" },
        new object[] { "Izzet Signet",    "U", "R" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_IsArtifact_TwoCost_WithCorrectName(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });

        s.Should().BeOfType<Artifact>();
        s.HasType(CardType.Artifact).Should().BeTrue();
        s.Name.Should().Be(cardName);
        s.ManaCost.Should().Be("{2}");
    }

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_OwnerAndControllerAreSet(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });

        s.Owner.Should().BeSameAs(alice);
        s.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_Dispatch_ResolvesViaNamedCardFactory(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(cardName, alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be(cardName);
        card.ManaCost.Should().Be("{2}");
    }

    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_HasExactlyOneManaAbility_ProducingBothPips(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var expected = ManaCost.Parse(a + b);

        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });

        var mana = s.Abilities.OfType<ManaAbility>().Should().ContainSingle(
            "the signet has a single {1}, {T}: Add {A}{B} mana ability").Subject;
        mana.ManaGenerated.White.Should().Be(expected.White);
        mana.ManaGenerated.Blue.Should().Be(expected.Blue);
        mana.ManaGenerated.Black.Should().Be(expected.Black);
        mana.ManaGenerated.Red.Should().Be(expected.Red);
        mana.ManaGenerated.Green.Should().Be(expected.Green);
        // Both pips emitted at once — exactly two coloured units, no generic.
        (mana.ManaGenerated.White + mana.ManaGenerated.Blue + mana.ManaGenerated.Black +
         mana.ManaGenerated.Red + mana.ManaGenerated.Green).Should().Be(2);
        mana.ManaGenerated.Generic.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_HasNoActivatedOrTriggeredAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });

        s.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        s.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add {A}{B} — payment behaviour
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_Activation_PaysOneGeneric_ReturnsBothPips_AndTaps(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(s);
        s.SetZone(ZoneType.Battlefield);
        // Seed the {1} the additional cost consumes (e.g. a colourless source).
        alice.AddManaToPool(ManaCost.Parse("1"));

        var mana = s.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue("the {1} additional cost is affordable");

        // ManaAbility.Activate returns the produced mana ({A}{B}); the
        // additional-cost payer deducts {1} from the pool as part of
        // activation (mirrors FilterLandCycleFactory's filter mode).
        var produced = mana.Activate();

        s.IsTapped.Should().BeTrue("{T} is part of the activation cost");
        var expected = ManaCost.Parse(a + b);
        produced.White.Should().Be(expected.White);
        produced.Blue.Should().Be(expected.Blue);
        produced.Black.Should().Be(expected.Black);
        produced.Red.Should().Be(expected.Red);
        produced.Green.Should().Be(expected.Green);
        // The {1} additional cost was consumed from the controller's pool.
        alice.ManaPool.Generic.Should().Be(0, "the {1} was consumed by the additional cost");
    }

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_CannotActivate_WithoutOneGeneric(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(s);
        s.SetZone(ZoneType.Battlefield);
        // Empty mana pool — the {1} additional cost can't be paid.

        var mana = s.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeFalse(
            "the {1} additional cost requires mana already in the pool (no auto-tap fixer)");
    }

    [Theory]
    [MemberData(nameof(AllSignets))]
    public void Signet_CannotActivate_WhenTapped(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var s = SignetCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(s);
        s.SetZone(ZoneType.Battlefield);
        alice.AddManaToPool(ManaCost.Parse("2"));

        var mana = s.Abilities.OfType<ManaAbility>().Single();
        mana.Activate();

        mana.CanActivate().Should().BeFalse("the {T} cost can't be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Args validation + fallback
    // -----------------------------------------------------------------------

    [Fact]
    public void Signet_Create_ThrowsOnNullOwner()
    {
        var act = () => SignetCycleFactory.Create(null!, new[] { "Azorius Signet", "W", "U" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Signet_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => SignetCycleFactory.Create(alice, new[] { "Azorius Signet", "W" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SignetCycleFactory needs args*");
    }

    [Fact]
    public void Signet_FallbackOverload_BuildsAzoriusSignet()
    {
        var alice = new Player("Alice", 20);

        var s = SignetCycleFactory.Create(alice);

        s.Name.Should().Be("Azorius Signet");
        s.ManaCost.Should().Be("{2}");
        s.Abilities.OfType<ManaAbility>().Should().ContainSingle();
    }
}
