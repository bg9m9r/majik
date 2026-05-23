using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PyromancersGogglesFactory"/>.
///
/// Pyromancer's Goggles — Legendary Artifact {5}.
/// "{T}: Add {R}. When you spend this mana to cast an instant or sorcery
///  spell, copy that spell. You may choose new targets for the copy."
///
/// Covers:
/// - Card identity (Legendary Artifact, mana cost {5}).
/// - NamedCardFactory dispatch.
/// - Single {T}: Add {R} ManaAbility, tap → +R, then locked out on tap.
/// - Structural copy-rider TriggeredAbility attached.
///
/// Deferred: mana-provenance gate + stack-copy primitive (see factory xmldoc).
/// </summary>
public class PyromancersGogglesTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void PyromancersGoggles_IsLegendaryArtifact_FiveCost()
    {
        var goggles = PyromancersGogglesFactory.Create(_alice);

        goggles.Name.Should().Be("Pyromancer's Goggles");
        goggles.HasType(CardType.Artifact).Should().BeTrue("Pyromancer's Goggles is an Artifact");
        goggles.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Pyromancer's Goggles is Legendary");
        goggles.ManaCost.Should().Be("{5}");
        goggles.Owner.Should().BeSameAs(_alice);
        goggles.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PyromancersGoggles()
    {
        var card = NamedCardFactory.Create("Pyromancer's Goggles", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Pyromancer's Goggles");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // --------------------------------------------------------------
    // Mana ability shape + activation
    // --------------------------------------------------------------

    [Fact]
    public void PyromancersGoggles_HasSingleManaAbility_AddR()
    {
        var goggles = PyromancersGogglesFactory.Create(_alice);
        var mas = goggles.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(1, "single {T}: Add {R} ability");
        var ma = mas[0];
        ma.ManaGenerated.Red.Should().Be(1);
        ma.ManaGenerated.TotalValue.Should().Be(1, "exactly one pip of {R}");
    }

    [Fact]
    public void PyromancersGoggles_Tap_AddsR_AndLocksOut()
    {
        var goggles = PyromancersGogglesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goggles);

        var ma = goggles.Abilities.OfType<ManaAbility>().Single();

        ma.CanActivate().Should().BeTrue("untapped Goggles can activate");

        var produced = ma.Activate();
        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        goggles.IsTapped.Should().BeTrue("activating tapped the Goggles");

        ma.CanActivate().Should().BeFalse("tapped Goggles cannot activate again");
    }

    // --------------------------------------------------------------
    // Copy rider — structural only (deferred behaviour)
    // --------------------------------------------------------------

    [Fact]
    public void PyromancersGoggles_HasStructuralCopyTrigger()
    {
        var goggles = PyromancersGogglesFactory.Create(_alice);

        var triggers = goggles.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single structural copy rider attached");

        var copy = triggers[0];
        copy.Source.Should().BeSameAs(goggles);
        copy.Controller.Should().BeSameAs(_alice);
        copy.Effects.Should().HaveCount(1, "single deferred-no-op effect for the copy rider");
    }
}
