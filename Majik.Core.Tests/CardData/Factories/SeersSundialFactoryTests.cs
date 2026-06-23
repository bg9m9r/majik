using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SeersSundialFactory"/> (Worldwake, {4}).
///
/// Card: Seer's Sundial — Artifact.
/// Oracle: "Landfall — Whenever a land you control enters, you may pay {2}.
/// If you do, draw a card."
///
/// Covers (the card's UNIQUE behaviour only — the contract test already
/// asserts dispatch + well-formedness):
/// - Identity (Artifact, {4}, colourless, owner/controller).
/// - Landfall trigger attached (CR 603.1 / 603.6a / 702.142), self-affecting
///   (no targets).
/// - Trigger condition predicate: a land entering under the controller's
///   control matches; the opponent's land does not (CR 603.6a — "a land you
///   control").
/// - Resolve: with the mana pool funded, pays {2} and draws a card.
/// - Resolve: with the mana pool empty, the trigger fizzles (CR 117.5) — no
///   draw.
/// </summary>
[Trait("Color", "C")]
public class SeersSundialFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersSundial_Identity_Artifact_Four_Colourless()
    {
        var sundial = SeersSundialFactory.Create(_alice);

        sundial.Name.Should().Be("Seer's Sundial");
        sundial.HasType(CardType.Artifact).Should().BeTrue();
        sundial.ManaCost.Should().Be("{4}");
        sundial.ManaCostValue.TotalValue.Should().Be(4);
        CardColors.GetColors(sundial).Should().BeEmpty("Seer's Sundial is a colourless artifact");
        sundial.Owner.Should().BeSameAs(_alice);
        sundial.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SeersSundial_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var sundial = SeersSundialFactory.Create(_alice);

        var trigger = sundial.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(sundial);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall draws for the controller — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Trigger condition — fires on controller's land ETB, not opponent's
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersSundial_TriggerCondition_MatchesControllersLand()
    {
        var sundial = SeersSundialFactory.Create(_alice);
        PutOnBattlefield(_alice, sundial);

        var trigger = sundial.Abilities.OfType<TriggeredAbility>().Single();

        var plains = new Land("Plains");
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        var evt = new CardMovedEvent(plains, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeTrue("a land entering under the controller's control triggers landfall");
    }

    [Fact]
    public void SeersSundial_TriggerCondition_DoesNotMatch_OpponentsLand()
    {
        var sundial = SeersSundialFactory.Create(_alice);
        PutOnBattlefield(_alice, sundial);

        var trigger = sundial.Abilities.OfType<TriggeredAbility>().Single();

        var swamp = new Land("Swamp");
        swamp.SetOwner(_bob);
        swamp.SetController(_bob);
        var evt = new CardMovedEvent(swamp, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeFalse("CR 603.6a — landfall only fires on 'a land YOU control'");
    }

    // -----------------------------------------------------------------------
    // Resolve — may pay {2}, draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void SeersSundial_Resolve_PaysTwoAndDraws_WhenManaAvailable()
    {
        var sundial = SeersSundialFactory.Create(_alice);
        PutOnBattlefield(_alice, sundial);

        var top = new Sorcery("Top Card", "{0}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Fund Alice's mana pool with {2} (generic).
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(SeersSundialFactory.OptionalManaCost));

        var trigger = sundial.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the agent-less path auto-pays {2} and draws the top of the library");
        _alice.ManaPool.Total.Should().Be(0,
            "Seer's Sundial consumed the funded generic {2}");
    }

    [Fact]
    public void SeersSundial_Resolve_NoMana_FizzlesNoDraw()
    {
        var sundial = SeersSundialFactory.Create(_alice);
        PutOnBattlefield(_alice, sundial);

        var top = new Sorcery("Top Card", "{0}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        // No mana funded.

        var trigger = sundial.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(top,
            "CR 117.5 — optional may-pay fizzles when {2} can't be paid; no draw");
        _alice.Zones.Library.GetCards().Should().Contain(top,
            "top of library stays put when the trigger fizzled");
    }
}
