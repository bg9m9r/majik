using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WornPowerstoneFactory"/> — Worn Powerstone, the
/// {3} artifact mana rock.
///
/// Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    {T}: Add {C}{C}."
///
/// Covers:
/// - Identity (Artifact type, printed name, {3} cost, owner/controller).
/// - Exactly one mana ability producing two colourless ({C}{C}).
///   CR 107.4c — {C} folds into the generic bucket via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>; "CC" yields
///   <c>Generic == 2</c> (same as Mana Crypt / Eldrazi Temple).
/// - No activated / triggered abilities (the rock is a pure mana source).
/// - Dispatch through <see cref="NamedCardFactory"/> resolves the name.
/// - <b>ETB tapped</b> — CR 614.1c. The factory itself builds the rock
///   untapped (mirrors the JSON-driven land cycle); the unconditional
///   ETB-tapped is applied on the production load path by
///   <see cref="EntersTappedBinder"/> matching the seed oracle text. The
///   final test pins that the binder fires on Worn Powerstone's exact
///   oracle text and the rock enters the battlefield tapped.
/// </summary>
public class WornPowerstoneFactoryTests
{
    // The exact Scryfall oracle text for Worn Powerstone.
    private const string OracleText = "This artifact enters tapped.\n{T}: Add {C}{C}.";

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WornPowerstone_IsArtifact_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var stone = WornPowerstoneFactory.Create(alice);

        stone.Should().BeOfType<Artifact>();
        stone.HasType(CardType.Artifact).Should().BeTrue();
        stone.Name.Should().Be("Worn Powerstone");
    }

    [Fact]
    public void WornPowerstone_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var stone = WornPowerstoneFactory.Create(alice);

        stone.Owner.Should().BeSameAs(alice);
        stone.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void WornPowerstone_HasPrintedManaCostThree()
    {
        var alice = new Player("Alice", 20);

        var stone = WornPowerstoneFactory.Create(alice);

        // {3} — three generic, no coloured pips.
        var cost = stone.ManaCostValue;
        cost.Generic.Should().Be(3);
        cost.White.Should().Be(0);
        cost.Blue.Should().Be(0);
        cost.Black.Should().Be(0);
        cost.Red.Should().Be(0);
        cost.Green.Should().Be(0);
    }

    [Fact]
    public void WornPowerstone_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var stone = WornPowerstoneFactory.Create(alice);

        stone.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        stone.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void WornPowerstone_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Worn Powerstone", alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be("Worn Powerstone");
    }

    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WornPowerstone_HasExactlyOneManaAbility_ProducingTwoColorless()
    {
        var alice = new Player("Alice", 20);

        var stone = WornPowerstoneFactory.Create(alice);

        var manaAbilities = stone.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().ContainSingle("Worn Powerstone has one {T}: Add {C}{C} ability");

        // CR 107.4c — {C}{C} folds into the generic bucket (Generic == 2).
        var produced = manaAbilities.Single().ManaGenerated;
        produced.Generic.Should().Be(2);
        produced.TotalValue.Should().Be(2);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
    }

    [Fact]
    public void WornPowerstone_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var stone = WornPowerstoneFactory.Create(alice);

        stone.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only ability is a mana ability");
        stone.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB tapped (CR 614.1c) — applied by EntersTappedBinder in production.
    // -----------------------------------------------------------------------

    [Fact]
    public void WornPowerstone_OracleText_TriggersEntersTappedBinder()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var stone = WornPowerstoneFactory.Create(alice);
        var entity = new CardEntity
        {
            Name = "Worn Powerstone",
            OracleText = OracleText,
            TypeLine = "Artifact",
        };

        // CR 614.1c — the unconditional "enters tapped" sentence binds.
        EntersTappedBinder.Bind(stone, entity, bus).Should().BeTrue();
    }

    [Fact]
    public void WornPowerstone_EntersTapped_WhenBoundAndMovedToBattlefield()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);

        var alice = new Player("Alice", 20);
        var stone = WornPowerstoneFactory.Create(alice);
        alice.Zones.Hand.AddCard(stone);
        stone.SetZone(ZoneType.Hand);

        var entity = new CardEntity
        {
            Name = "Worn Powerstone",
            OracleText = OracleText,
            TypeLine = "Artifact",
        };

        EntersTappedBinder.Bind(stone, entity, rep).Should().BeTrue();

        zones.MoveCardTo(stone, ZoneType.Battlefield, controller: alice);

        stone.Zone.Should().Be(ZoneType.Battlefield);
        stone.IsTapped.Should().BeTrue("CR 614.1c — Worn Powerstone enters tapped");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void WornPowerstone_Create_ThrowsOnNullOwner()
    {
        var act = () => WornPowerstoneFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
