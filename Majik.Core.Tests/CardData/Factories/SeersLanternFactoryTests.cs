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
/// Tests for <see cref="SeersLanternFactory"/> (Khans of Tarkir / Dragons of
/// Tarkir). Artifact. Oracle text (Scryfall-confirmed 2026-06):
///   "{T}: Add {C}.
///    {2}, {T}: Scry 1. (Look at the top card of your library. You may put
///    that card on the bottom.)"
///
/// Scryfall type line: Artifact (no subtype). Mana cost {3}. Identity + both
/// abilities are loaded from <c>seers-lantern.json</c> via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
///   - Identity: Artifact type, name, {3} mana cost, owner/controller, no
///     creature type, nonbasic / non-legendary.
///   - {T}: Add {C} — a single vanilla colourless mana ability (CR 605.1 /
///     107.4c); produces exactly one colourless and taps the lantern.
///   - {2}, {T}: Scry 1 — the activated ability's cost shape ({2} mana +
///     self-tap, CR 602.1 / 605.1) and that it carries no targets.
///   - Scry resolution (CR 701.20): the no-agent default puts the single
///     peeked top card on the bottom of the library.
/// </summary>
[Trait("Color", "C")]
public class SeersLanternFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersLantern_IsArtifact_WithThreeGenericCost()
    {
        var lantern = SeersLanternFactory.Create(_alice);

        lantern.Should().BeOfType<Artifact>();
        lantern.Name.Should().Be("Seer's Lantern");
        lantern.HasType(CardType.Artifact).Should().BeTrue();
        lantern.HasType(CardType.Creature).Should().BeFalse();
        lantern.ManaCost.Should().Be("{3}");
        lantern.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        lantern.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        lantern.Owner.Should().BeSameAs(_alice);
        lantern.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersLantern_TapForColorless_ProducesOneColorless_AndTapsSelf()
    {
        var lantern = SeersLanternFactory.Create(_alice);

        var mana = lantern.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue();

        var produced = mana.Activate();

        // {C} is colourless (CR 107.4c) — Colorless tracks the {C} pip; it also
        // counts toward Generic in v1's ManaCost model. No coloured pips.
        produced.Colorless.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        lantern.IsTapped.Should().BeTrue("{T} is part of the mana ability cost");
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: Scry 1 — cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersLantern_HasScryActivatedAbility_WithTwoGenericAndSelfTapCost()
    {
        var lantern = SeersLanternFactory.Create(_alice);

        lantern.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "{2}, {T}: Scry 1");
        var scry = lantern.Abilities.OfType<ActivatedAbility>().Single();

        // {2} generic mana.
        scry.Costs.OfType<ManaCostCost>().Single().Cost.Generic
            .Should().Be(2, "the {2} cost is two generic mana");

        // {T} — self-tap.
        scry.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        // Scry 1 has no targets.
        scry.TargetRequests.Should().BeEmpty("Scry 1 targets nothing");
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: Scry 1 — resolution (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersLantern_ScryResolution_PutsTopCardOnBottom_WithNoAgent()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var lantern = SeersLanternFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(lantern);
        lantern.SetZone(ZoneType.Battlefield);

        var scry = lantern.Abilities.OfType<ActivatedAbility>().Single();

        // Pay {2} + {T}, then resolve the scry.
        alice.AddManaToPool(ManaCost.Parse("2"));
        foreach (var cost in scry.Costs)
        {
            cost.Pay(alice);
        }
        lantern.IsTapped.Should().BeTrue("the {T} cost taps the lantern");
        scry.Resolve();

        // No agent registered → fall-back sends the single peeked card (Top) to
        // the bottom; the previously-second card is now on top. Library size is
        // unchanged (CR 701.20 — scry never draws).
        alice.Zones.Library.GetCards().Should().Equal(new[] { second, top });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
