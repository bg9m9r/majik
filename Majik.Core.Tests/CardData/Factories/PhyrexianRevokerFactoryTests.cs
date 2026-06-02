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
/// Tests for Phyrexian Revoker (New Phyrexia, {2}).
///
/// CR 602.5c — "Activated abilities of sources with the chosen name
/// can't be activated unless they're mana abilities."
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection with Pithing Needle / Karn the Great
/// Creator / the validator-side activation tests. The chosen-name
/// registry is process-global; tests serialize via the collection and
/// clear in <see cref="IDisposable.Dispose"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
[Trait("Color", "C")]
public class PhyrexianRevokerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public void Dispose()
    {
        // Registry is process-global; clear between tests (same posture
        // as PithingNeedleTests).
        ActivatedAbilityRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexianRevoker_IsArtifactCreature_PhyrexianHorror_2_1_AtCost2()
    {
        var c = PhyrexianRevokerFactory.Create(_alice);

        c.Name.Should().Be("Phyrexian Revoker");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "CR 301.1 / 302.1 — Phyrexian Revoker is an Artifact Creature");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // ETB lifecycle — chosen name registers / unregisters
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbFlow_RegistersChosenName_OnceOnBattlefield()
    {
        var revoker = PhyrexianRevokerFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);

        // Not on battlefield yet — no registration.
        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse();

        // Move to battlefield.
        revoker.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(revoker, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();
    }

    [Fact]
    public void NamedSourceActivatedAbility_IsRejected_WhenRevokerNamesIt()
    {
        var revoker = PhyrexianRevokerFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        revoker.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(revoker, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.SetZone(ZoneType.Battlefield);

        var pingAbility = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(pingAbility, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Phyrexian Revoker suppresses activated abilities of the chosen name");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void LtbFlow_RemovesSuppression()
    {
        var revoker = PhyrexianRevokerFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        revoker.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(revoker, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();

        // Revoker leaves the battlefield (e.g. dies to bolt).
        revoker.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(revoker, ZoneType.Battlefield, ZoneType.Graveyard));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse("LTB removes the registration");
    }

    [Fact]
    public void NoSelector_DoesNotRegisterAnyRestriction()
    {
        // Single-arg shape path: no selector wired — the printed static
        // does not register, mirroring Pithing Needle's no-selector
        // posture.
        var revoker = PhyrexianRevokerFactory.Create(_alice);
        revoker.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(revoker, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse();
    }
}
