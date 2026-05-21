using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GristFactory"/>.
///
/// Covers:
/// - Card identity: Legendary Planeswalker with loyalty 3
/// - V1 simplification: Creature type added unconditionally
/// - Insect + Grist subtypes present
/// - Owner/controller assignment
/// - Green Sun's Zenith integration: Grist is found because HasType(Creature) == true
/// </summary>
public class GristTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Grist_IsLegendaryPlaneswalker()
    {
        var grist = GristFactory.Create(_alice);

        grist.HasType(CardType.Planeswalker).Should().BeTrue("Grist is a Planeswalker");
        grist.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Grist is Legendary");
    }

    [Fact]
    public void Grist_HasCreatureType_ForTutorTargeting()
    {
        // V1 simplification: Creature type is added unconditionally so that
        // tutors like Green Sun's Zenith can target Grist in all zones.
        var grist = GristFactory.Create(_alice);

        grist.HasType(CardType.Creature).Should().BeTrue(
            "Grist's Creature type is added unconditionally in v1 to enable tutor targeting");
    }

    [Fact]
    public void Grist_HasInsectSubtype()
    {
        var grist = GristFactory.Create(_alice);

        grist.HasSubtype(CardSubtype.Insect).Should().BeTrue();
    }

    [Fact]
    public void Grist_HasGristSubtype()
    {
        var grist = GristFactory.Create(_alice);

        grist.HasSubtype(CardSubtype.Grist).Should().BeTrue();
    }

    [Fact]
    public void Grist_HasLoyalty3()
    {
        var grist = GristFactory.Create(_alice);

        grist.Loyalty.Should().Be(3);
        grist.StartingLoyalty.Should().Be(3);
    }

    [Fact]
    public void Grist_OwnerAndControllerAreSet()
    {
        var grist = GristFactory.Create(_alice);

        grist.Owner.Should().BeSameAs(_alice);
        grist.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Grist_ManaCostAndName()
    {
        var grist = GristFactory.Create(_alice);

        grist.Name.Should().Be("Grist, the Hunger Tide");
        grist.ManaCost.Should().Be("{1}{B}{G}");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory route
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_CreatesGrist()
    {
        var card = NamedCardFactory.Create("Grist, the Hunger Tide", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Grist, the Hunger Tide");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Green Sun's Zenith integration
    // Verifies Grist is found by GSZ because HasType(Creature) == true and
    // it is green (B/G pips) with mana value 3 (1 generic + B + G).
    // -----------------------------------------------------------------------

    [Fact]
    public void GreenSunsZenith_CanTutorGrist_BecauseItHasCreatureType()
    {
        var alice = new Player("Alice", 20);
        var grist = GristFactory.Create(alice);
        grist.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(grist);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Green Sun's Zenith",
                ManaCost = "{X}{G}",
                OracleText =
                    "Search your library for a green creature card with mana value X or less, " +
                    "put it onto the battlefield, then shuffle. " +
                    "Shuffle Green Sun's Zenith into its owner's library.",
            },
            alice, raw => raw, null);

        def.Should().NotBeNull();

        // X = 3: Grist's CMC is 3 (1 generic + B + G pip), so X=3 exactly meets it.
        var chosen = new ChosenSpellParams(null, X: 3,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Battlefield.GetCards().Should().Contain(grist,
            "GSZ with X=3 should find Grist (CMC 3, green, HasType(Creature))");
        alice.Zones.Library.GetCards().Should().NotContain(grist);
    }

    [Fact]
    public void GreenSunsZenith_XTooLow_DoesNotTutorGrist()
    {
        var alice = new Player("Alice", 20);
        var grist = GristFactory.Create(alice);
        grist.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(grist);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Green Sun's Zenith",
                ManaCost = "{X}{G}",
                OracleText =
                    "Search your library for a green creature card with mana value X or less, " +
                    "put it onto the battlefield, then shuffle. " +
                    "Shuffle Green Sun's Zenith into its owner's library.",
            },
            alice, raw => raw, null);

        def.Should().NotBeNull();

        // X = 2: Grist's CMC is 3 — should not be found.
        var chosen = new ChosenSpellParams(null, X: 2,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Library.GetCards().Should().Contain(grist,
            "GSZ with X=2 should not find Grist (CMC 3 > 2)");
        alice.Zones.Battlefield.GetCards().Should().NotContain(grist);
    }
}
