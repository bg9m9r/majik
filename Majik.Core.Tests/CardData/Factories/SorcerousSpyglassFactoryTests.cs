using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Tests.CardData;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Sorcerous Spyglass (Ixalan, {2}).
///
/// CR 602.5c — "Activated abilities of sources with the chosen name
/// can't be activated unless they're mana abilities."
///
/// Functional twin of Pithing Needle: an Artifact whose printed static is
/// the same name-targeted activated-ability suppression, with an
/// information-only "look at an opponent's hand" ETB rider on top. The
/// rider changes no game state, so the suppression is the entirety of the
/// observable behaviour and these tests mirror
/// <see cref="PithingNeedleTests"/> / Phyrexian Revoker.
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection — the chosen-name registry is process-global;
/// tests serialize via the collection and clear in
/// <see cref="IDisposable.Dispose"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
[Trait("Color", "C")]
public class SorcerousSpyglassFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public void Dispose()
    {
        // Registry is process-global; clear between tests.
        ActivatedAbilityRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SorcerousSpyglass_IsArtifact_AtCost2()
    {
        var c = SorcerousSpyglassFactory.Create(_alice);

        c.Name.Should().Be("Sorcerous Spyglass");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeFalse(
            "Sorcerous Spyglass is a noncreature Artifact");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_CreatesSorcerousSpyglass()
    {
        var c = NamedCardFactory.Create("Sorcerous Spyglass", _alice);

        c.Name.Should().Be("Sorcerous Spyglass");
        c.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB lifecycle — chosen name registers / unregisters
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbFlow_RegistersChosenName_OnceOnBattlefield()
    {
        var spyglass = SorcerousSpyglassFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);

        // Not on battlefield yet — no registration.
        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse();

        // Move to battlefield.
        spyglass.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(spyglass, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();
    }

    [Fact]
    public void NamedSourceActivatedAbility_IsRejected_WhenSpyglassNamesIt()
    {
        var spyglass = SorcerousSpyglassFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        spyglass.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(spyglass, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.SetZone(ZoneType.Battlefield);

        var pingAbility = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(pingAbility, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Sorcerous Spyglass suppresses activated abilities of the chosen name");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void DifferentName_DoesNotSuppress()
    {
        var spyglass = SorcerousSpyglassFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        spyglass.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(spyglass, ZoneType.Hand, ZoneType.Battlefield));

        var other = new Artifact("Sensei's Divining Top", "{1}");
        other.SetOwner(_bob);
        other.SetController(_bob);
        other.SetZone(ZoneType.Battlefield);
        var ability = new ActivatedAbility(other, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "different name — Spyglass's restriction does not apply");
    }

    [Fact]
    public void LtbFlow_RemovesSuppression()
    {
        var spyglass = SorcerousSpyglassFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        spyglass.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(spyglass, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();

        // Spyglass leaves the battlefield.
        spyglass.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(spyglass, ZoneType.Battlefield, ZoneType.Graveyard));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse("LTB removes the registration");
    }

    [Fact]
    public void NoSelector_DoesNotRegisterAnyRestriction()
    {
        // Single-arg shape path: no selector wired — the printed static
        // does not register, mirroring Pithing Needle's no-selector posture.
        var spyglass = SorcerousSpyglassFactory.Create(_alice);
        spyglass.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(spyglass, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse();
    }
}
