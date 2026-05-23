using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="PithingNeedleFactory"/>, <see cref="PithingNeedleStaticEffect"/>,
/// and <see cref="ActivatedAbilityRestrictions"/>.
///
/// CR 602.5c — "Activated abilities of sources with the chosen name
/// can't be activated unless they're mana abilities."
/// CR 605 — mana-ability exemption.
/// </summary>
public class PithingNeedleTests : IDisposable
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
    public void PithingNeedle_IsArtifact_WithCorrectCost()
    {
        var needle = PithingNeedleFactory.Create(_alice);

        needle.HasType(CardType.Artifact).Should().BeTrue();
        needle.Name.Should().Be("Pithing Needle");
        needle.ManaCost.Should().Be("{1}");
        needle.Owner.Should().BeSameAs(_alice);
        needle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_CreatesPithingNeedle()
    {
        var needle = NamedCardFactory.Create("Pithing Needle", _alice);

        needle.Name.Should().Be("Pithing Needle");
        needle.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB lifecycle — chosen name registers / unregisters
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbFlow_RegistersChosenName_OnceOnBattlefield()
    {
        var needle = PithingNeedleFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);

        // Not on battlefield yet — no registration.
        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse();

        // Move to battlefield.
        needle.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();
    }

    [Fact]
    public void NamedSourceActivatedAbility_IsRejected_WhenNeedleNamesIt()
    {
        // Pithing Needle on battlefield naming "Walking Ballista".
        var needle = PithingNeedleFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        needle.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

        // Walking Ballista's {X}: damage activated ability — built directly
        // here rather than via the JSON factory to keep the test focused
        // on the suppression gate.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.SetZone(ZoneType.Battlefield);

        var pingAbility = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(pingAbility, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse("Pithing Needle suppresses activated abilities of the chosen name");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void ManaAbility_IsExempt_FromNeedleSuppression()
    {
        // Needle naming "Sol Ring" should not gate Sol Ring's {T}: Add {C}{C}
        // mana ability (CR 605). ManaAbilityActivator runs on a separate
        // path entirely; the registry's IsActivatedAbilityRestricted also
        // defends in depth by returning false for IManaAbility.
        var needle = PithingNeedleFactory.Create(
            _alice,
            nameSelector: _ => "Sol Ring",
            eventBus: _bus);
        needle.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_bob);
        solRing.SetController(_bob);
        solRing.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(solRing, _bob, ManaCost.Parse("CC"));

        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            // ManaAbility doesn't implement IActivatedAbility — this guard
            // is defensive. The compile-time path through
            // ActivateAbilityAction already rejects mana abilities (they
            // don't satisfy the IActivatedAbility ctor parameter), so the
            // engine couldn't even build the action.
            new ActivatedAbility(solRing, _bob)).Should().BeTrue(
            "the source name still matches; suppression of non-mana activated abilities applies");

        // But the mana-ability path itself is bypassed: the activator uses
        // ManaAbilityActivator, not ActionValidator. We assert here on the
        // registry's typed exemption guard.
        IActivatedAbility manaShaped = new ManaAbilityShim(solRing, _bob, mana);
        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(manaShaped)
            .Should().BeFalse("CR 605 — mana abilities are exempt from Pithing Needle");
    }

    [Fact]
    public void DifferentName_DoesNotSuppress()
    {
        var needle = PithingNeedleFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        needle.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

        // Some other artifact with an activated ability — name not chosen.
        var other = new Artifact("Sensei's Divining Top", "{1}");
        other.SetOwner(_bob);
        other.SetController(_bob);
        other.SetZone(ZoneType.Battlefield);
        var ability = new ActivatedAbility(other, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "different name — Needle's restriction does not apply");
    }

    [Fact]
    public void LtbFlow_RemovesSuppression()
    {
        var needle = PithingNeedleFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        needle.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();

        // Needle leaves the battlefield.
        needle.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(needle, ZoneType.Battlefield, ZoneType.Graveyard));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse("LTB removes the registration");

        // And activation is again legal.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.SetZone(ZoneType.Battlefield);
        var pingAbility = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(pingAbility, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TwoNeedles_TwoDifferentNames_BothSuppressionsActive()
    {
        var needle1 = PithingNeedleFactory.Create(
            _alice,
            nameSelector: _ => "Walking Ballista",
            eventBus: _bus);
        var needle2 = PithingNeedleFactory.Create(
            _bob,
            nameSelector: _ => "Aether Vial",
            eventBus: _bus);

        needle1.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle1, ZoneType.Hand, ZoneType.Battlefield));
        needle2.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(needle2, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeTrue();
        ActivatedAbilityRestrictions.IsNameRestricted("Aether Vial")
            .Should().BeTrue();

        // First Needle leaves — only its restriction lifts.
        needle1.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(needle1, ZoneType.Battlefield, ZoneType.Graveyard));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
            .Should().BeFalse();
        ActivatedAbilityRestrictions.IsNameRestricted("Aether Vial")
            .Should().BeTrue("the second Needle is still on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test-only shim — exposes a mana ability through the
    /// <see cref="IActivatedAbility"/> shape so the registry's defensive
    /// IManaAbility check can be exercised end-to-end. (Production code
    /// would never wrap a mana ability this way; it exists only to assert
    /// CR 605 exemption logic in the registry itself.)
    /// </summary>
    private sealed class ManaAbilityShim : ActivatedAbility, IManaAbility
    {
        private readonly IManaAbility _inner;

        public ManaAbilityShim(ICard source, Player controller, IManaAbility inner)
            : base(source, controller)
        {
            _inner = inner;
        }

        object IManaAbility.Source => _inner.Source;
        Player IManaAbility.Controller => _inner.Controller;
        ManaCost IManaAbility.ManaGenerated => _inner.ManaGenerated;
        bool IManaAbility.CanActivate() => _inner.CanActivate();
        ManaCost IManaAbility.Activate() => _inner.Activate();
    }
}
