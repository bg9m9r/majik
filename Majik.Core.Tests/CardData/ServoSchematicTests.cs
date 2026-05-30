using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ServoSchematicFactory"/>.
///
/// Servo Schematic — Artifact {2} (Aether Revolt).
///   "When this artifact enters or is put into a graveyard from the
///    battlefield, create a 1/1 colorless Servo artifact creature token."
///
/// Closest analogue is <see cref="IchorWellspringFactory"/> (the
/// symmetric "enters or is put into a graveyard from the battlefield"
/// dual <see cref="TriggeredAbility"/>) with the per-leg effect swapped
/// from "draw a card" to the 1/1 colourless Servo artifact-creature token
/// minted by <see cref="Majik.Core.Tokens.TokenFactory.CreateOnBattlefield"/>
/// — the same Servo wiring as <see cref="AnimationModuleFactory"/>.
///
/// Both legs are plain <see cref="TriggeredAbility"/> over
/// <see cref="CardMovedEvent"/>: <see cref="Triggers.OnEnterBattlefieldSelf"/>
/// for the ETB leg and <see cref="Triggers.OnDies"/> for the Battlefield →
/// Graveyard leg (CR 603.6 — both are CardMovedEvent-driven; OnDies is
/// permanent-agnostic despite the creature-flavoured name).
///
/// Covers:
/// - Identity (Artifact, {2}) + NamedCardFactory dispatch.
/// - Two triggered abilities (ETB + LTB), no others.
/// - ETB trigger condition matches Battlefield-entering self.
/// - LTB trigger condition matches Battlefield → Graveyard self only.
/// - Each trigger creates one 1/1 colourless Servo artifact creature token.
/// </summary>
public class ServoSchematicTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void ServoSchematic_IsArtifact_TwoCost()
    {
        var schematic = ServoSchematicFactory.Create(_alice);

        schematic.Name.Should().Be("Servo Schematic");
        schematic.HasType(CardType.Artifact).Should().BeTrue();
        schematic.ManaCost.Should().Be("{2}");
        schematic.Owner.Should().BeSameAs(_alice);
        schematic.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ServoSchematic()
    {
        var card = NamedCardFactory.Create("Servo Schematic", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Servo Schematic");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
    }

    // --------------------------------------------------------------
    // Ability shape — exactly two triggered abilities (ETB + LTB)
    // --------------------------------------------------------------

    [Fact]
    public void ServoSchematic_HasTwoTriggeredAbilities()
    {
        var schematic = ServoSchematicFactory.Create(_alice);
        schematic.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // --------------------------------------------------------------
    // ETB trigger — entering the battlefield
    // --------------------------------------------------------------

    [Fact]
    public void ServoSchematic_EtbTrigger_ConditionMatchesEnterBattlefield()
    {
        var schematic = ServoSchematicFactory.Create(_alice);
        schematic.SetZone(ZoneType.Battlefield);

        var triggers = schematic.Abilities.OfType<TriggeredAbility>().ToList();
        var enters = new CardMovedEvent(schematic, ZoneType.Hand, ZoneType.Battlefield);
        triggers.Should().ContainSingle(t => t.IsTriggered(enters),
            "exactly one trigger (the ETB leg) fires when the artifact enters");
    }

    // --------------------------------------------------------------
    // LTB trigger — Battlefield → Graveyard for the source
    // --------------------------------------------------------------

    [Fact]
    public void ServoSchematic_LtbTrigger_ConditionMatchesBattlefieldToGraveyard()
    {
        var schematic = ServoSchematicFactory.Create(_alice);
        schematic.SetZone(ZoneType.Battlefield);

        var triggers = schematic.Abilities.OfType<TriggeredAbility>().ToList();

        var dies = new CardMovedEvent(schematic, ZoneType.Battlefield, ZoneType.Graveyard);
        triggers.Should().ContainSingle(t => t.IsTriggered(dies),
            "exactly one trigger (the LTB leg) fires on Battlefield → Graveyard");

        var bounce = new CardMovedEvent(schematic, ZoneType.Battlefield, ZoneType.Hand);
        triggers.Should().NotContain(t => t.IsTriggered(bounce),
            "Battlefield → Hand is a bounce, not LTB-to-graveyard");

        var exile = new CardMovedEvent(schematic, ZoneType.Battlefield, ZoneType.Exile);
        triggers.Should().NotContain(t => t.IsTriggered(exile),
            "Battlefield → Exile bypasses the graveyard step entirely");
    }

    // --------------------------------------------------------------
    // Resolution — each leg creates a 1/1 colourless Servo artifact
    // creature token.
    // --------------------------------------------------------------

    [Fact]
    public void ServoSchematic_EtbTrigger_Resolve_CreatesServoToken()
    {
        var schematic = ServoSchematicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(schematic);
        schematic.SetZone(ZoneType.Battlefield);

        var enters = new CardMovedEvent(schematic, ZoneType.Hand, ZoneType.Battlefield);
        var etb = schematic.Abilities.OfType<TriggeredAbility>().Single(t => t.IsTriggered(enters));
        etb.Resolve();

        AssertSingleServoToken();
    }

    [Fact]
    public void ServoSchematic_LtbTrigger_Resolve_CreatesServoToken()
    {
        var schematic = ServoSchematicFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(schematic);
        schematic.SetZone(ZoneType.Graveyard);

        var dies = new CardMovedEvent(schematic, ZoneType.Battlefield, ZoneType.Graveyard);
        var ltb = schematic.Abilities.OfType<TriggeredAbility>().Single(t => t.IsTriggered(dies));
        ltb.Resolve();

        AssertSingleServoToken();
    }

    private void AssertSingleServoToken()
    {
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Servo")
            .ToList();

        tokens.Should().ContainSingle("the trigger creates exactly one Servo token");
        var servo = tokens.Single();
        servo.Power.Should().Be(1);
        servo.Toughness.Should().Be(1);
        servo.HasType(CardType.Artifact).Should().BeTrue("Servo is an artifact creature (CR 111.1)");
        servo.HasType(CardType.Creature).Should().BeTrue();
        servo.HasSubtype(CardSubtype.Servo).Should().BeTrue();
        CardColors.GetColors(servo).Should().BeEmpty("the Servo token is colourless (CR 111.4)");
    }
}
