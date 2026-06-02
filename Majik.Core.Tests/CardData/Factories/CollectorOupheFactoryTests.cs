using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Collector Ouphe (Modern Horizons, {1}{G}).
///
/// Creature — Ouphe 2/2.
/// CR 602.5c — "Activated abilities of artifacts can't be activated."
/// CR 605.1a — mana abilities are excluded from the term "activated
/// abilities", so artifact mana abilities are NOT suppressed (the same
/// exemption Stony Silence enjoys, even though Collector Ouphe's printed
/// text omits the explicit clause).
///
/// Functional reprint of Stony Silence on a creature body — the suppression
/// is the identical symmetric, global artifact-activated gate.
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection because the
/// <see cref="ActivatedAbilityRestrictions"/> registry is process-global and
/// predicate restrictions would otherwise leak into concurrently-running
/// suites that consult <see cref="ActionValidator"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class CollectorOupheFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public CollectorOupheFactoryTests()
    {
        // Defensive — ensure no other test left predicates behind.
        ActivatedAbilityRestrictions.Clear();
    }

    public void Dispose()
    {
        // Registry is process-global; clear between tests.
        ActivatedAbilityRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CollectorOuphe_IsCreature_WithCorrectCostAndStats()
    {
        var card = CollectorOupheFactory.Create(_alice);

        card.Name.Should().Be("Collector Ouphe");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeFalse();
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesCollectorOuphe()
    {
        var card = NamedCardFactory.Create("Collector Ouphe", _alice);

        card.Name.Should().Be("Collector Ouphe");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Should().BeOfType<Creature>();
    }

    // -----------------------------------------------------------------------
    // Printed static — global artifact-activated suppression (CR 602.5c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksOpponentsArtifactActivatedAbility_OnceOnBattlefield()
    {
        var ouphe = CollectorOupheFactory.Create(_alice, _bus);
        ((Card)ouphe).SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(ouphe, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(ping, _bob);

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Collector Ouphe blocks non-mana activated abilities of all artifacts");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void Static_AlsoBlocksOwnArtifactActivatedAbility_Symmetric()
    {
        // Collector Ouphe is symmetric — Alice's own artifact is also gated.
        var ouphe = CollectorOupheFactory.Create(_alice, _bus);
        ((Card)ouphe).SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(ouphe, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _alice);
        var action = new ActivateAbilityAction(ping, _alice);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse(
            "Collector Ouphe is symmetric — both players' artifacts are gated");
    }

    [Fact]
    public void Static_DoesNotBlockArtifactManaAbility_Cr605Exemption()
    {
        var ouphe = CollectorOupheFactory.Create(_alice, _bus);
        ((Card)ouphe).SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(ouphe, ZoneType.Hand, ZoneType.Battlefield));

        // CR 605.1a — "activated abilities" excludes mana abilities, so a
        // {T}: Add … artifact mana ability is NOT covered, even though
        // Collector Ouphe's printed text omits the explicit clause.
        var moxOpal = new Artifact("Mox Opal", "{0}");
        moxOpal.SetOwner(_bob);
        moxOpal.SetController(_bob);
        moxOpal.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(moxOpal, _bob, ManaCost.Parse("W"));

        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            new ManaAbilityShim(moxOpal, _bob, mana))
            .Should().BeFalse(
                "CR 605.1a — mana abilities are exempt from Collector Ouphe");
    }

    [Fact]
    public void Static_DoesNotBlockNonArtifactActivatedAbility()
    {
        var ouphe = CollectorOupheFactory.Create(_alice, _bus);
        ((Card)ouphe).SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(ouphe, ZoneType.Hand, ZoneType.Battlefield));

        // A non-artifact creature's activated ability must NOT be suppressed.
        var lurrus = NamedCardFactory.Create("Lurrus of the Dream-Den", _bob);
        lurrus.SetController(_bob);
        ((Card)lurrus).SetZone(ZoneType.Battlefield);
        lurrus.HasType(CardType.Artifact).Should().BeFalse(
            "precondition — the source is not an artifact");

        var ability = new ActivatedAbility(lurrus, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "Collector Ouphe's predicate only matches artifact sources");
    }

    [Fact]
    public void Static_LiftsWhenCollectorOupheLeavesBattlefield()
    {
        var ouphe = CollectorOupheFactory.Create(_alice, _bus);
        ((Card)ouphe).SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(ouphe, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        var ping = new ActivatedAbility(ballista, _bob);
        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeFalse("sanity — static is in effect");

        // Collector Ouphe dies / leaves the battlefield.
        ((Card)ouphe).SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(ouphe, ZoneType.Battlefield, ZoneType.Graveyard));

        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeTrue("LTB removes the predicate registration");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test-only shim — exposes a mana ability through the
    /// <see cref="IActivatedAbility"/> shape so the registry's CR 605.1a
    /// IManaAbility short-circuit is exercised. Mirrors the helper in
    /// StonySilenceTests.
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
