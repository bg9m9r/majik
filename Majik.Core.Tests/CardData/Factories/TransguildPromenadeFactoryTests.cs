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
/// Tests for <see cref="TransguildPromenadeFactory"/> — Transguild Promenade
/// (Ravnica: City of Guilds). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, sacrifice it unless you pay {1}.
///    {T}: Add one mana of any color."
///
/// Functionally identical to Rupture Spire. Covers:
/// - Identity (Land type, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - Five mana abilities (one per WUBRG colour) — same any-colour fan-out as
///   Forbidden Orchard, with NO {C} mode and NO pay-life cost ({T} alone).
/// - One ETB self-trigger (CR 603.6e) carrying the "sacrifice unless you pay
///   {1}" tail (CR 603.1):
///     * pays {1} when the pool has it → the land stays;
///     * fails to pay → the land is sacrificed (Battlefield → Graveyard).
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class TransguildPromenadeFactoryTests
{
    private const string CardName = "Transguild Promenade";

    public static IEnumerable<object[]> AllColors => new[]
    {
        new object[] { "W" },
        new object[] { "U" },
        new object[] { "B" },
        new object[] { "R" },
        new object[] { "G" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TransguildPromenade_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void TransguildPromenade_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void TransguildPromenade_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — any colour
    // -----------------------------------------------------------------------

    [Fact]
    public void TransguildPromenade_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void TransguildPromenade_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);

        // No "{T}: Add {C}" mode: every mode produces one coloured mana.
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
    public void TransguildPromenade_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Should().NotBeNull($"Transguild Promenade can add {{{color}}}");
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void TransguildPromenade_TapForMana_DoesNotCostLife(string color)
    {
        // Unlike Mana Confluence / City of Brass, the {T} ability has NO
        // additional life/pain cost — the downside is the ETB tax, not life.
        var alice = new Player("Alice", 20);
        var land = TransguildPromenadeFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Activate();

        alice.LifeTotal.Should().Be(20, "tapping for mana costs no life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TransguildPromenade_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = TransguildPromenadeFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // ETB "sacrifice unless you pay {1}" trigger — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TransguildPromenade_HasOneEtbTriggeredAbility()
    {
        var alice = new Player("Alice", 20);

        var land = TransguildPromenadeFactory.Create(alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "when this land enters, sacrifice it unless you pay {1} → one trigger");
    }

    // -----------------------------------------------------------------------
    // ETB trigger resolution — pay {1} keeps the land; failure sacrifices it
    // -----------------------------------------------------------------------

    [Fact]
    public void TransguildPromenade_OnResolve_WithMana_PaysAndStays()
    {
        var alice = new Player("Alice", 20);
        var land = TransguildPromenadeFactory.Create(alice);

        // Land is on the battlefield; controller has {1} available.
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        alice.AddManaToPool(ManaCost.Parse("1"));

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        // Paid {1}: the land stays on the battlefield.
        land.Zone.Should().Be(ZoneType.Battlefield, "the {1} tax was paid");
        alice.Zones.Battlefield.GetCards().Should().Contain(land);
        alice.Zones.Graveyard.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void TransguildPromenade_OnResolve_WithoutMana_SacrificesIt()
    {
        var alice = new Player("Alice", 20);
        var land = TransguildPromenadeFactory.Create(alice);

        // Land is on the battlefield; controller has NO mana to pay {1}.
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        // Could not pay {1}: the land is sacrificed (Battlefield → Graveyard).
        land.Zone.Should().Be(ZoneType.Graveyard, "the {1} tax went unpaid");
        alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        alice.Zones.Graveyard.GetCards().Should().Contain(land);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TransguildPromenade_DispatchesThroughNamedFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(CardName, alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be(CardName);
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(alice);
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
