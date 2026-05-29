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
/// Unit tests for <see cref="IchorWellspringFactory"/>.
///
/// Ichor Wellspring — Artifact {2}.
///   "When this artifact enters or is put into a graveyard from the
///    battlefield, draw a card."
///
/// Closest analogue is <see cref="ChromaticStarFactory"/> (Battlefield →
/// Graveyard cantrip), with the symmetric ETB cantrip added. Both legs are
/// plain <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>:
/// <see cref="Triggers.OnEnterBattlefieldSelf"/> for the ETB leg and
/// <see cref="Triggers.OnDies"/> for the Battlefield → Graveyard leg
/// (CR 603.6 — both are CardMovedEvent-driven; OnDies is permanent-agnostic
/// despite the creature-flavoured name).
///
/// Covers:
/// - Identity (Artifact, {2}) + NamedCardFactory dispatch.
/// - Two triggered abilities (ETB + LTB), no others.
/// - ETB trigger condition matches Battlefield-entering self.
/// - LTB trigger condition matches Battlefield → Graveyard self only.
/// - Each trigger draws one card on resolve.
/// </summary>
public class IchorWellspringTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void IchorWellspring_IsArtifact_TwoCost()
    {
        var well = IchorWellspringFactory.Create(_alice);

        well.Name.Should().Be("Ichor Wellspring");
        well.HasType(CardType.Artifact).Should().BeTrue();
        well.ManaCost.Should().Be("{2}");
        well.Owner.Should().BeSameAs(_alice);
        well.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IchorWellspring()
    {
        var card = NamedCardFactory.Create("Ichor Wellspring", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Ichor Wellspring");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
    }

    // --------------------------------------------------------------
    // Ability shape — exactly two triggered abilities (ETB + LTB)
    // --------------------------------------------------------------

    [Fact]
    public void IchorWellspring_HasTwoTriggeredAbilities()
    {
        var well = IchorWellspringFactory.Create(_alice);
        well.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // --------------------------------------------------------------
    // ETB trigger — entering the battlefield
    // --------------------------------------------------------------

    [Fact]
    public void IchorWellspring_EtbTrigger_ConditionMatchesEnterBattlefield()
    {
        var well = IchorWellspringFactory.Create(_alice);
        // The artifact is on the battlefield when its ETB trigger is
        // evaluated (CR 603.10 — the trigger's zone gate is checked against
        // the post-move zone). activeZones={Battlefield} for the ETB leg.
        well.SetZone(ZoneType.Battlefield);

        var triggers = well.Abilities.OfType<TriggeredAbility>().ToList();
        // The ETB leg is the one that fires on ToZone == Battlefield.
        var enters = new CardMovedEvent(well, ZoneType.Hand, ZoneType.Battlefield);
        triggers.Should().ContainSingle(t => t.IsTriggered(enters),
            "exactly one trigger (the ETB leg) fires when the artifact enters");
    }

    // --------------------------------------------------------------
    // LTB trigger — Battlefield → Graveyard for the source
    // --------------------------------------------------------------

    [Fact]
    public void IchorWellspring_LtbTrigger_ConditionMatchesBattlefieldToGraveyard()
    {
        var well = IchorWellspringFactory.Create(_alice);
        well.SetZone(ZoneType.Battlefield);

        var triggers = well.Abilities.OfType<TriggeredAbility>().ToList();

        var dies = new CardMovedEvent(well, ZoneType.Battlefield, ZoneType.Graveyard);
        triggers.Should().ContainSingle(t => t.IsTriggered(dies),
            "exactly one trigger (the LTB leg) fires on Battlefield → Graveyard");

        var bounce = new CardMovedEvent(well, ZoneType.Battlefield, ZoneType.Hand);
        triggers.Should().NotContain(t => t.IsTriggered(bounce),
            "Battlefield → Hand is a bounce, not LTB-to-graveyard");

        var exile = new CardMovedEvent(well, ZoneType.Battlefield, ZoneType.Exile);
        triggers.Should().NotContain(t => t.IsTriggered(exile),
            "Battlefield → Exile bypasses the graveyard step entirely");
    }

    // --------------------------------------------------------------
    // Resolution — each leg draws one card
    // --------------------------------------------------------------

    [Fact]
    public void IchorWellspring_EtbTrigger_Resolve_DrawsACard()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var well = IchorWellspringFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(well);
        well.SetZone(ZoneType.Battlefield);

        var enters = new CardMovedEvent(well, ZoneType.Hand, ZoneType.Battlefield);
        var etb = well.Abilities.OfType<TriggeredAbility>().Single(t => t.IsTriggered(enters));
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "ETB cantrip drew one card");
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void IchorWellspring_LtbTrigger_Resolve_DrawsACard()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var well = IchorWellspringFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(well);
        well.SetZone(ZoneType.Graveyard);

        var dies = new CardMovedEvent(well, ZoneType.Battlefield, ZoneType.Graveyard);
        var ltb = well.Abilities.OfType<TriggeredAbility>().Single(t => t.IsTriggered(dies));
        ltb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "LTB cantrip drew one card");
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
