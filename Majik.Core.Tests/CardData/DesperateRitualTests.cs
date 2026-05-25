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
/// Unit tests for <see cref="DesperateRitualFactory"/>.
///
/// Desperate Ritual (Champions of Kamigawa, {1}{R}, Instant — Arcane):
///   "Add {R}{R}{R}.
///    Splice onto Arcane {1}{R}."
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Splice onto Arcane is attached as a v1 documented
///     <see cref="KeywordAbility"/> marker only — no behavioural impact
///     until the Splice primitive lands (see class docs).
///   - Resolve: adds three red mana to the controller's mana pool
///     (CR 106.4).
/// </summary>
public class DesperateRitualTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DesperateRitual_HasExpectedShape()
    {
        var card = DesperateRitualFactory.Create(_alice);

        card.Name.Should().Be("Desperate Ritual");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DesperateRitual()
    {
        var card = NamedCardFactory.Create("Desperate Ritual", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Desperate Ritual");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DesperateRitual_HasSpliceOntoArcaneKeywordMarker()
    {
        var card = DesperateRitualFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Splice onto Arcane",
                "v1 attaches a documentation-only marker — Splice primitive deferred");
    }

    [Fact]
    public void Resolve_AddsThreeRedMana()
    {
        _alice.ManaPool.Total.Should().Be(0);

        var effect = DesperateRitualFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(3);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(3);
    }

    [Fact]
    public void Resolve_TwoCopiesInSameStep_StackToSixRed()
    {
        // CR 106.4 — mana from multiple ritual resolutions accumulates in
        // the same pool until the end of the current step/phase.
        var effect1 = DesperateRitualFactory.BuildResolveEffect(_alice).Single();
        var effect2 = DesperateRitualFactory.BuildResolveEffect(_alice).Single();

        effect1.Execute();
        effect2.Execute();

        _alice.ManaPool.Red.Should().Be(6);
        _alice.ManaPool.Total.Should().Be(6);
    }

    [Fact]
    public void SpliceCostText_IsOneR()
    {
        DesperateRitualFactory.SpliceCostText.Should().Be("{1}{R}");
    }
}
