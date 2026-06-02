using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ManaGeodeFactory"/> (Time Spiral / The List, {3}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall 2026-05):
///   "When this artifact enters, scry 1.
///    {T}: Add one mana of any color."
///
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Unlike Crystal Grotto / Prismatic Lens (which gate "any color" behind a
/// {1} additional cost) Mana Geode's any-colour mode is <b>free</b> — only
/// {T}. And it has no separate {C} mode. So it carries exactly five mana
/// abilities (one per WUBRG) plus one ETB scry-1 triggered ability.
///
/// Covers:
/// - Identity (name, Artifact type, {3} cost, owner/controller, nonbasic /
///   non-legendary, no creature type).
/// - Five free mana abilities, one per WUBRG — the JSON encoding of "Add one
///   mana of any color" (CR 605.1 / 605.1a), same WUBRG fan-out as Pillar of
///   Origins.
/// - Each coloured ability is activatable with an empty pool (no {1} rider)
///   and adds exactly that one colour while tapping the geode.
/// - Tap-as-cost: a tapped geode cannot activate any of its abilities.
/// - One battlefield-active ETB triggered ability that scries 1 (CR 701.20);
///   the no-agent fall-back puts the single peeked card on the bottom.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class ManaGeodeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaGeode_IsArtifact_WithCorrectName()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);

        geode.Should().BeOfType<Artifact>();
        geode.Name.Should().Be("Mana Geode");
        geode.HasType(CardType.Artifact).Should().BeTrue();
        geode.HasType(CardType.Creature).Should().BeFalse();
        geode.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        geode.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        geode.Owner.Should().BeSameAs(_alice);
        geode.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Mana abilities — shape (free any-colour, one per WUBRG)
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaGeode_HasFiveManaAbilities_OnePerColor()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);
        var mana = geode.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(5, "one {T}: Add <color> ability per WUBRG");
        mana.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }

    [Fact]
    public void ManaGeode_HasNoColorlessOrActivatedAbility()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);

        // Mana Geode (unlike Prismatic Lens) has NO separate {T}: Add {C} mode.
        geode.Abilities.OfType<ManaAbility>()
            .Where(a => a.ManaGenerated.Generic >= 1)
            .Should().BeEmpty("Mana Geode produces no colourless mana");
        geode.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the geode's only activatable abilities are mana abilities");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void ManaGeode_HasAnyColorManaAbility_PerColor(string color)
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);
        var match = ManaCost.Parse(color);

        geode.Abilities.OfType<ManaAbility>().Should().ContainSingle(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green,
            $"Mana Geode can add {{{color}}} via its any-colour mode");
    }

    // -----------------------------------------------------------------------
    // Free any-colour activation (no {1} cost gate)
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaGeode_ColoredAbilities_CanActivate_WithEmptyPool()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);

        foreach (var ability in geode.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeTrue(
                "Mana Geode's any-colour mode is free ({T} only)");
        }
    }

    [Fact]
    public void ManaGeode_BlueActivation_AddsBlue_AndTapsSelf()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);
        var blue = geode.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Generic.Should().Be(0, "no {1} cost is paid");
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        geode.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    [Fact]
    public void ManaGeode_NoAbilityCanActivate_WhenTapped()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);
        var white = geode.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.White == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(white, _alice);
        geode.IsTapped.Should().BeTrue();

        foreach (var ability in geode.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "a tapped permanent cannot pay the {T} cost");
        }
    }

    // -----------------------------------------------------------------------
    // ETB scry 1 (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaGeode_EtbTrigger_IsBattlefieldActive()
    {
        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", _alice);
        var trigger = geode.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void ManaGeode_EtbEffect_ScriesOne_DefaultsTopCardToBottom()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", alice);
        var etb = geode.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → fall-back puts the single peeked card (Top)
        // on the bottom; the previously-second card is now on top.
        alice.Zones.Library.GetCards().Should().Equal(new[] { second, top });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ManaGeode_EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var geode = (Artifact)NamedCardFactory.Create("Mana Geode", alice);
        var etb = geode.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
