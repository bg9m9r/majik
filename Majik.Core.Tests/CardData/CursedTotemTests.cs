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
/// Tests for Cursed Totem (Mirage, {2}).
///
/// CR 602.5c — "Activated abilities of creatures can't be activated
/// unless they're mana abilities."
/// CR 605 — mana-ability exemption.
///
/// Symmetric global creature-side analogue of Stony Silence's artifact
/// suppression — both controllers' creatures are gated.
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection with Pithing Needle, Stony Silence, Karn
/// the Great Creator, and the validator-side activation tests — the
/// <see cref="ActivatedAbilityRestrictions"/> registry is process-global,
/// and predicate restrictions can otherwise leak into concurrently-running
/// suites that consult <see cref="ActionValidator.ValidateActivateAbility"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class CursedTotemTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public CursedTotemTests()
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
    public void CursedTotem_IsArtifact_WithCorrectCost()
    {
        var card = CursedTotemFactory.Create(_alice);

        card.Name.Should().Be("Cursed Totem");
        card.ManaCost.Should().Be("{2}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesCursedTotem()
    {
        var card = NamedCardFactory.Create("Cursed Totem", _alice);

        card.Name.Should().Be("Cursed Totem");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Should().BeOfType<Artifact>();
    }

    // -----------------------------------------------------------------------
    // Printed static — global creature-activated suppression
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksOpponentsCreatureActivatedAbility_OnceOnBattlefield()
    {
        var totem = CursedTotemFactory.Create(_alice, _bus);
        totem.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(totem, ZoneType.Hand, ZoneType.Battlefield));

        // Opponent's Walking Ballista — its {X} non-mana activated ability
        // must be suppressed. Walking Ballista is an Artifact Creature, so
        // it falls under Cursed Totem (creature type present), not just
        // Stony Silence.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        ballista.HasType(CardType.Creature).Should().BeTrue(
            "precondition — Walking Ballista is an artifact creature");

        var ping = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(ping, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Cursed Totem blocks non-mana activated abilities of all creatures");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void Static_AlsoBlocksOwnCreatureActivatedAbility_Symmetric()
    {
        // Cursed Totem is symmetric — Alice's own Walking Ballista is also
        // gated. This is the printed-text behaviour, matching Stony
        // Silence's global scope.
        var totem = CursedTotemFactory.Create(_alice, _bus);
        totem.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(totem, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _alice);
        var action = new ActivateAbilityAction(ping, _alice);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse(
            "Cursed Totem is symmetric — both players' creatures are gated");
    }

    [Fact]
    public void Static_DoesNotBlockCreatureManaAbility_Cr605Exemption_BirdsOfParadise()
    {
        var totem = CursedTotemFactory.Create(_alice, _bus);
        totem.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(totem, ZoneType.Hand, ZoneType.Battlefield));

        // Birds of Paradise's {T}: Add one mana of any color is a mana
        // ability — CR 605 says "activated abilities" excludes mana
        // abilities, so Cursed Totem's printed text does NOT cover it.
        var birds = new Creature("Birds of Paradise", "{G}", 0, 1);
        birds.SetOwner(_bob);
        birds.SetController(_bob);
        birds.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(birds, _bob, ManaCost.Parse("G"));

        // Mana abilities take a separate activator path (not ActionValidator).
        // The registry's defensive guard is the right thing to assert at
        // the unit level.
        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            new ManaAbilityShim(birds, _bob, mana))
            .Should().BeFalse(
                "CR 605 — mana abilities are exempt from Cursed Totem");
    }

    [Fact]
    public void Static_DoesNotBlockNonCreatureActivatedAbility_PithingNeedleArtifact()
    {
        // A non-creature artifact's activated ability (e.g. a vanilla
        // Pithing Needle shell) must NOT be suppressed by Cursed Totem —
        // its predicate matches only creature sources.
        var totem = CursedTotemFactory.Create(_alice, _bus);
        totem.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(totem, ZoneType.Hand, ZoneType.Battlefield));

        var needle = NamedCardFactory.Create("Pithing Needle", _bob);
        needle.SetController(_bob);
        ((Card)needle).SetZone(ZoneType.Battlefield);
        needle.HasType(CardType.Artifact).Should().BeTrue();
        needle.HasType(CardType.Creature).Should().BeFalse(
            "precondition — Pithing Needle is a non-creature artifact");

        var ability = new ActivatedAbility(needle, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "Cursed Totem's predicate only matches creature sources");
    }

    [Fact]
    public void Static_LiftsWhenCursedTotemLeavesBattlefield()
    {
        var totem = CursedTotemFactory.Create(_alice, _bus);
        totem.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(totem, ZoneType.Hand, ZoneType.Battlefield));

        // Sanity — static is in effect against Bob's Ballista.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        var ping = new ActivatedAbility(ballista, _bob);
        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeFalse();

        // Cursed Totem leaves the battlefield.
        totem.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(totem, ZoneType.Battlefield, ZoneType.Graveyard));

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
    /// equivalent helper in Stony Silence / Pithing Needle / Karn tests.
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
