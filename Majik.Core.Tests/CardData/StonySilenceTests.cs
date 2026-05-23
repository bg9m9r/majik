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
/// Tests for Stony Silence (Return to Ravnica, {1}{W}).
///
/// CR 602.5c — "Activated abilities of artifacts can't be activated unless
/// they're mana abilities."
/// CR 605 — mana-ability exemption.
///
/// Symmetric global variant of Karn the Great Creator's opponent-only
/// artifact-activated suppression — both controllers' artifacts are gated.
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection with Pithing Needle, Karn the Great Creator,
/// and the validator-side activation tests — the
/// <see cref="ActivatedAbilityRestrictions"/> registry is process-global,
/// and predicate restrictions can otherwise leak into concurrently-running
/// suites that consult <see cref="ActionValidator.ValidateActivateAbility"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class StonySilenceTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public StonySilenceTests()
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
    public void StonySilence_IsEnchantment_WithCorrectCost()
    {
        var card = StonySilenceFactory.Create(_alice);

        card.Name.Should().Be("Stony Silence");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesStonySilence()
    {
        var card = NamedCardFactory.Create("Stony Silence", _alice);

        card.Name.Should().Be("Stony Silence");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Should().BeOfType<Enchantment>();
    }

    // -----------------------------------------------------------------------
    // Printed static — global artifact-activated suppression
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksOpponentsArtifactActivatedAbility_OnceOnBattlefield()
    {
        var silence = StonySilenceFactory.Create(_alice, _bus);
        silence.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(silence, ZoneType.Hand, ZoneType.Battlefield));

        // Opponent's Walking Ballista — its {X} non-mana activated ability
        // must be suppressed.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(ping, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Stony Silence blocks non-mana activated abilities of all artifacts");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void Static_AlsoBlocksOwnArtifactActivatedAbility_Symmetric()
    {
        // Stony Silence is symmetric — Alice's own Walking Ballista is also
        // gated. This is the printed-text behaviour and the key difference
        // versus Karn the Great Creator's opponent-only static.
        var silence = StonySilenceFactory.Create(_alice, _bus);
        silence.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(silence, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _alice);
        var action = new ActivateAbilityAction(ping, _alice);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse(
            "Stony Silence is symmetric — both players' artifacts are gated");
    }

    [Fact]
    public void Static_DoesNotBlockArtifactManaAbility_Cr605Exemption()
    {
        var silence = StonySilenceFactory.Create(_alice, _bus);
        silence.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(silence, ZoneType.Hand, ZoneType.Battlefield));

        // Mox Opal's {T}: Add one mana of any color is a mana ability —
        // CR 605 says "activated abilities" excludes mana abilities, so
        // Stony Silence's printed text does NOT cover it.
        var moxOpal = new Artifact("Mox Opal", "{0}");
        moxOpal.SetOwner(_bob);
        moxOpal.SetController(_bob);
        moxOpal.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(moxOpal, _bob, ManaCost.Parse("W"));

        // Mana abilities take a separate activator path (not ActionValidator).
        // The registry's defensive guard is the right thing to assert at
        // the unit level.
        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            new ManaAbilityShim(moxOpal, _bob, mana))
            .Should().BeFalse(
                "CR 605 — mana abilities are exempt from Stony Silence");
    }

    [Fact]
    public void Static_DoesNotBlockNonArtifactActivatedAbility_LurrusCreature()
    {
        // Lurrus of the Dream-Den is a creature, not an artifact. Its
        // activated ability (the engine's stand-in here — a plain
        // ActivatedAbility on the card) must NOT be suppressed.
        var silence = StonySilenceFactory.Create(_alice, _bus);
        silence.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(silence, ZoneType.Hand, ZoneType.Battlefield));

        var lurrus = NamedCardFactory.Create("Lurrus of the Dream-Den", _bob);
        lurrus.SetController(_bob);
        ((Card)lurrus).SetZone(ZoneType.Battlefield);
        lurrus.HasType(CardType.Creature).Should().BeTrue();
        lurrus.HasType(CardType.Artifact).Should().BeFalse(
            "precondition — Lurrus is a creature, not an artifact");

        var ability = new ActivatedAbility(lurrus, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "Stony Silence's predicate only matches artifact sources");
    }

    [Fact]
    public void Static_LiftsWhenStonySilenceLeavesBattlefield()
    {
        var silence = StonySilenceFactory.Create(_alice, _bus);
        silence.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(silence, ZoneType.Hand, ZoneType.Battlefield));

        // Sanity — static is in effect against Bob's Ballista.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        var ping = new ActivatedAbility(ballista, _bob);
        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeFalse();

        // Stony Silence leaves the battlefield.
        silence.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(silence, ZoneType.Battlefield, ZoneType.Graveyard));

        // Activation legal again.
        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeTrue("LTB removes the predicate registration");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test-only shim — exposes a mana ability through the
    /// <see cref="IActivatedAbility"/> shape so the registry's defensive
    /// IManaAbility check can be exercised end-to-end. Mirrors the
    /// equivalent helper in Pithing Needle / Karn tests.
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
