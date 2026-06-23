using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CrystalBallFactory"/> (Magic 2015 / various).
/// Artifact. Oracle text (Scryfall-confirmed 2026-06-23):
///   "{1}, {T}: Scry 2. (Look at the top two cards of your library, then put
///    any number of them on the bottom and the rest on top in any order.)"
///
/// Scryfall type line: Artifact (no subtype). Mana cost {3}. Identity + the
/// activated ability are loaded from <c>crystal-ball.json</c> via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
///   - Identity: Artifact type, name, {3} mana cost, owner/controller,
///     nonbasic / non-legendary.
///   - No mana ability (unlike Seer's Lantern, Crystal Ball does not tap for
///     mana).
///   - {1}, {T}: Scry 2 — the activated ability's cost shape ({1} mana +
///     self-tap, CR 602.1 / 605.1) and that it carries no targets.
///   - Scry resolution (CR 701.20): the no-agent default puts both peeked top
///     cards on the bottom of the library; library size is unchanged.
/// </summary>
[Trait("Color", "C")]
public class CrystalBallFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CrystalBall_IsArtifact_WithThreeGenericCost()
    {
        var ball = CrystalBallFactory.Create(_alice);

        ball.Should().BeOfType<Artifact>();
        ball.Name.Should().Be("Crystal Ball");
        ball.HasType(CardType.Artifact).Should().BeTrue();
        ball.HasType(CardType.Creature).Should().BeFalse();
        ball.ManaCost.Should().Be("{3}");
        ball.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        ball.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        ball.Owner.Should().BeSameAs(_alice);
        ball.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CrystalBall_HasNoManaAbility()
    {
        var ball = CrystalBallFactory.Create(_alice);

        ball.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Crystal Ball only scries — it has no {T}: Add mana ability");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Scry 2 — cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CrystalBall_HasScryActivatedAbility_WithOneGenericAndSelfTapCost()
    {
        var ball = CrystalBallFactory.Create(_alice);

        ball.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "{1}, {T}: Scry 2");
        var scry = ball.Abilities.OfType<ActivatedAbility>().Single();

        // {1} generic mana.
        scry.Costs.OfType<ManaCostCost>().Single().Cost.Generic
            .Should().Be(1, "the {1} cost is one generic mana");

        // {T} — self-tap.
        scry.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        // Scry 2 has no targets.
        scry.TargetRequests.Should().BeEmpty("Scry 2 targets nothing");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Scry 2 — resolution (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void CrystalBall_ScryResolution_PutsBothTopCardsOnBottom_WithNoAgent()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        var third = new Card("Third", ""); third.SetOwner(alice);
        foreach (var c in new[] { top, second, third })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ball = CrystalBallFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ball);
        ball.SetZone(ZoneType.Battlefield);

        var scry = ball.Abilities.OfType<ActivatedAbility>().Single();

        // Pay {1} + {T}, then resolve the scry.
        alice.AddManaToPool(ManaCost.Parse("1"));
        foreach (var cost in scry.Costs)
        {
            cost.Pay(alice);
        }
        ball.IsTapped.Should().BeTrue("the {T} cost taps Crystal Ball");
        scry.Resolve();

        // No agent registered → fall-back sends both peeked cards (Top, Second)
        // to the bottom in order; Third is now on top. Library size unchanged
        // (CR 701.20 — scry never draws).
        alice.Zones.Library.GetCards().Should().Equal(new[] { third, top, second });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
