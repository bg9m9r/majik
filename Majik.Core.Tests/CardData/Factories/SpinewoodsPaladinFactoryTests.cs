using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Spinewoods Paladin (Bloomburrow, {4}{G}).
///
/// Creature — Human Knight 5/4 with Trample and
///   "When this creature enters, you gain 3 life."
///
/// The unique behavioural surface vs a vanilla body is (a) Trample
/// (CR 702.19) and (b) the ETB self-trigger granting the controller 3 life
/// (CR 603.6e). Plot {3}{G} (CR 718) is deferred — guarded below. Dispatch +
/// well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so this suite covers only the unique body.
/// </summary>
[Trait("Color", "G")]
public class SpinewoodsPaladinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity — non-vanilla stats: exact mana cost / P-T / subtypes.
    // -----------------------------------------------------------------------

    [Fact]
    public void SpinewoodsPaladin_Identity_HumanKnight_5_4_AtCost4G()
    {
        var c = SpinewoodsPaladinFactory.Create(_alice);

        c.Name.Should().Be("Spinewoods Paladin");
        c.ManaCost.Should().Be("{4}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Trample — CR 702.19
    // -----------------------------------------------------------------------

    [Fact]
    public void SpinewoodsPaladin_HasTrampleKeyword()
    {
        var c = SpinewoodsPaladinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "CR 702.19 — Spinewoods Paladin has Trample");
        CombatAbilities.HasTrample(c).Should().BeTrue(
            "CR 702.19 — the combat damage-assignment path reads the Trample keyword");
    }

    // -----------------------------------------------------------------------
    // ETB gain 3 life — CR 603.6e
    // -----------------------------------------------------------------------

    [Fact]
    public void SpinewoodsPaladin_HasOneEtbTriggeredAbility()
    {
        var c = SpinewoodsPaladinFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SpinewoodsPaladin_SelfEnters_TriggerMatches()
    {
        var paladin = SpinewoodsPaladinFactory.Create(_alice);
        paladin.SetZone(ZoneType.Hand);

        var trigger = paladin.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(paladin, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "CR 603.6e — 'when this creature enters' fires when the Paladin itself enters");
    }

    [Fact]
    public void SpinewoodsPaladin_OnResolve_ControllerGainsThreeLife()
    {
        var paladin = SpinewoodsPaladinFactory.Create(_alice);
        paladin.SetZone(ZoneType.Battlefield);

        var trigger = paladin.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(23, "the ETB grants the controller 3 life");
    }

    // -----------------------------------------------------------------------
    // Plot deferral guardrail — Spinewoods Paladin ships without Plot
    // activation (CR 718), mirroring Slickshot Show-Off. Pins the gap so
    // future Plot wiring (pay {3}{G}, exile-with-plot-marker from hand +
    // a sorcery-speed-on-a-later-turn cast-from-exile permission) is
    // observable as a behavioural change.
    // -----------------------------------------------------------------------

    [Fact]
    public void SpinewoodsPaladin_PlotMechanicDeferred_NoActivatedAbilityFromHand()
    {
        var c = SpinewoodsPaladinFactory.Create(_alice);

        // The only abilities on the card are the Trample keyword marker and
        // the ETB lifegain trigger. Plot (the only printed activated surface)
        // is deferred — no activated ability is wired.
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().ContainSingle(k => k.Keyword == "Trample");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}
