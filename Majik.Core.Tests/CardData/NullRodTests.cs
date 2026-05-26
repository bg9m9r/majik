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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Null Rod (Weatherlight, {2}).
///
/// CR 602.5c — "Activated abilities of artifacts can't be activated unless
/// they're mana abilities."
/// CR 605 — mana-ability exemption.
///
/// Null Rod is a functional copy of Stony Silence printed as an Artifact
/// rather than an Enchantment. The static-effect behaviour is shared
/// (<see cref="Majik.Core.Effects.StonySilenceStaticEffect"/>); these tests
/// lock in the artifact-card-shape end of the pair plus a regression that
/// confirms Null Rod's own static survives its own artifact-ness (CR 113.6).
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection with the rest of the activation-restriction
/// suites — the <see cref="ActivatedAbilityRestrictions"/> registry is
/// process-global.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class NullRodTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public NullRodTests()
    {
        ActivatedAbilityRestrictions.Clear();
    }

    public void Dispose()
    {
        ActivatedAbilityRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NullRod_IsArtifact_WithCorrectCost()
    {
        var card = NullRodFactory.Create(_alice);

        card.Name.Should().Be("Null Rod");
        card.ManaCost.Should().Be("{2}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeFalse(
            "Null Rod is the artifact half of the Null Rod / Stony Silence pair");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesNullRod()
    {
        var card = NamedCardFactory.Create("Null Rod", _alice);

        card.Name.Should().Be("Null Rod");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Should().BeOfType<Artifact>();
    }

    // -----------------------------------------------------------------------
    // Printed static — global artifact-activated suppression
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksOpponentsArtifactActivatedAbility_OnceOnBattlefield()
    {
        var rod = NullRodFactory.Create(_alice, _bus);
        rod.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(ping, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Null Rod blocks non-mana activated abilities of all artifacts");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void Static_AlsoBlocksOwnArtifactActivatedAbility_Symmetric()
    {
        // Null Rod is symmetric — Alice's own Walking Ballista is also
        // gated. This is the printed-text behaviour, identical to Stony
        // Silence.
        var rod = NullRodFactory.Create(_alice, _bus);
        rod.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _alice);
        var action = new ActivateAbilityAction(ping, _alice);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse(
            "Null Rod is symmetric — both players' artifacts are gated");
    }

    [Fact]
    public void Static_DoesNotBlockArtifactManaAbility_Cr605Exemption()
    {
        var rod = NullRodFactory.Create(_alice, _bus);
        rod.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Hand, ZoneType.Battlefield));

        // Mox Opal's {T}: Add one mana of any color is a mana ability —
        // CR 605 says "activated abilities" excludes mana abilities, so
        // Null Rod's printed text does NOT cover it.
        var moxOpal = new Artifact("Mox Opal", "{0}");
        moxOpal.SetOwner(_bob);
        moxOpal.SetController(_bob);
        moxOpal.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(moxOpal, _bob, ManaCost.Parse("W"));

        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            new ManaAbilityShim(moxOpal, _bob, mana))
            .Should().BeFalse(
                "CR 605 — mana abilities are exempt from Null Rod");
    }

    [Fact]
    public void Static_DoesNotBlockNonArtifactActivatedAbility()
    {
        // Lurrus is a creature, not an artifact. Its activated ability must
        // NOT be suppressed.
        var rod = NullRodFactory.Create(_alice, _bus);
        rod.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Hand, ZoneType.Battlefield));

        var lurrus = NamedCardFactory.Create("Lurrus of the Dream-Den", _bob);
        lurrus.SetController(_bob);
        ((Card)lurrus).SetZone(ZoneType.Battlefield);
        lurrus.HasType(CardType.Creature).Should().BeTrue();
        lurrus.HasType(CardType.Artifact).Should().BeFalse(
            "precondition — Lurrus is a creature, not an artifact");

        var ability = new ActivatedAbility(lurrus, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "Null Rod's predicate only matches artifact sources");
    }

    [Fact]
    public void Static_LiftsWhenNullRodLeavesBattlefield()
    {
        var rod = NullRodFactory.Create(_alice, _bus);
        rod.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        var ping = new ActivatedAbility(ballista, _bob);
        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeFalse();

        rod.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Battlefield, ZoneType.Graveyard));

        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeTrue("LTB removes the predicate registration");
    }

    [Fact]
    public void Static_DoesNotSelfSuppress_NullRodIsArtifactButStaticIsNotActivated()
    {
        // Regression — Null Rod is itself an artifact, but its printed
        // static is a static ability (not an activated ability), so
        // CR 113.6 / 602.5c doesn't cause Null Rod to suppress its own
        // existence. We don't ship any activated abilities on Null Rod,
        // so the simplest check is that wiring the static against Null
        // Rod's own card object does not cause an immediate stack
        // overflow / infinite recursion during Attach().
        var rod = NullRodFactory.Create(_alice, _bus);
        rod.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(rod, ZoneType.Hand, ZoneType.Battlefield));

        rod.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Null Rod has no activated abilities — only the printed static.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test-only shim — exposes a mana ability through the
    /// <see cref="IActivatedAbility"/> shape so the registry's defensive
    /// IManaAbility check can be exercised end-to-end. Mirrors the
    /// equivalent helper in the Stony Silence / Pithing Needle / Karn
    /// tests.
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
