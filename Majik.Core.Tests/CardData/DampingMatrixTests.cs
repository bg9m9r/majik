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
/// Tests for Damping Matrix (Mirrodin, {3}).
///
/// CR 602.5c — "Activated abilities of artifacts and creatures can't be
/// activated unless they're mana abilities."
/// CR 605 — mana-ability exemption.
///
/// Functionally the union of Stony Silence (artifact suppression) and
/// Cursed Totem (creature suppression) — symmetric global; both players'
/// artifacts and creatures are gated.
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection with Pithing Needle, Stony Silence, Cursed
/// Totem, Karn the Great Creator, and the validator-side activation tests
/// — the <see cref="ActivatedAbilityRestrictions"/> registry is process-
/// global, and predicate restrictions can otherwise leak into concurrently-
/// running suites that consult <see cref="ActionValidator.ValidateAction"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class DampingMatrixTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public DampingMatrixTests()
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
    public void DampingMatrix_IsArtifact_WithCorrectCost()
    {
        var card = DampingMatrixFactory.Create(_alice);

        card.Name.Should().Be("Damping Matrix");
        card.ManaCost.Should().Be("{3}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesDampingMatrix()
    {
        var card = NamedCardFactory.Create("Damping Matrix", _alice);

        card.Name.Should().Be("Damping Matrix");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Should().BeOfType<Artifact>();
    }

    // -----------------------------------------------------------------------
    // Printed static — artifact-activated suppression (Stony Silence side)
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksNonCreatureArtifactActivatedAbility_OnceOnBattlefield()
    {
        var matrix = DampingMatrixFactory.Create(_alice, _bus);
        matrix.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Hand, ZoneType.Battlefield));

        // A non-creature artifact's activated ability (Pithing Needle shell)
        // must be suppressed — the artifact branch of Damping Matrix.
        var needle = NamedCardFactory.Create("Pithing Needle", _bob);
        needle.SetController(_bob);
        ((Card)needle).SetZone(ZoneType.Battlefield);
        needle.HasType(CardType.Artifact).Should().BeTrue();
        needle.HasType(CardType.Creature).Should().BeFalse(
            "precondition — Pithing Needle is a non-creature artifact");

        var ability = new ActivatedAbility(needle, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Damping Matrix blocks non-mana activated abilities of artifacts");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    // -----------------------------------------------------------------------
    // Printed static — creature-activated suppression (Cursed Totem side)
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksOpponentsCreatureActivatedAbility_WalkingBallista()
    {
        var matrix = DampingMatrixFactory.Create(_alice, _bus);
        matrix.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Hand, ZoneType.Battlefield));

        // Walking Ballista is an artifact creature — its {X} non-mana
        // activated ability falls under either branch of Damping Matrix.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        ballista.HasType(CardType.Creature).Should().BeTrue(
            "precondition — Walking Ballista is an artifact creature");

        var ping = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(ping, _bob);

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Damping Matrix blocks non-mana activated abilities of creatures");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void Static_AlsoBlocksOwnPermanentsActivatedAbility_Symmetric()
    {
        // Damping Matrix is symmetric — Alice's own Walking Ballista is also
        // gated, matching the printed text (no "you control" qualifier).
        var matrix = DampingMatrixFactory.Create(_alice, _bus);
        matrix.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Hand, ZoneType.Battlefield));

        var ballista = NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var ping = new ActivatedAbility(ballista, _alice);
        var action = new ActivateAbilityAction(ping, _alice);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse(
            "Damping Matrix is symmetric — both players' permanents are gated");
    }

    // -----------------------------------------------------------------------
    // CR 605 mana-ability exemption
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_DoesNotBlockManaAbility_Cr605Exemption_BirdsOfParadise()
    {
        var matrix = DampingMatrixFactory.Create(_alice, _bus);
        matrix.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Hand, ZoneType.Battlefield));

        // Birds of Paradise's {T}: Add one mana of any color is a mana
        // ability — CR 605 says "activated abilities" excludes mana
        // abilities, so Damping Matrix's printed text does NOT cover it.
        var birds = new Creature("Birds of Paradise", "{G}", 0, 1);
        birds.SetOwner(_bob);
        birds.SetController(_bob);
        birds.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(birds, _bob, ManaCost.Parse("G"));

        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            new ManaAbilityShim(birds, _bob, mana))
            .Should().BeFalse(
                "CR 605 — mana abilities are exempt from Damping Matrix");
    }

    // -----------------------------------------------------------------------
    // Scope — non-artifact non-creature sources are not gated
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_DoesNotBlockNonArtifactNonCreatureActivatedAbility()
    {
        // An enchantment / planeswalker-style activated ability whose source
        // is neither artifact nor creature must NOT be suppressed — Damping
        // Matrix's predicate matches only artifact and creature sources.
        var matrix = DampingMatrixFactory.Create(_alice, _bus);
        matrix.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Hand, ZoneType.Battlefield));

        var enchantment = new Enchantment("Test Enchantment", "{1}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        enchantment.SetZone(ZoneType.Battlefield);
        enchantment.HasType(CardType.Artifact).Should().BeFalse();
        enchantment.HasType(CardType.Creature).Should().BeFalse();

        var ability = new ActivatedAbility(enchantment, _bob);
        var action = new ActivateAbilityAction(ability, _bob);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "Damping Matrix's predicate only matches artifact / creature sources");
    }

    // -----------------------------------------------------------------------
    // Lifecycle — LTB lifts the suppression
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_LiftsWhenDampingMatrixLeavesBattlefield()
    {
        var matrix = DampingMatrixFactory.Create(_alice, _bus);
        matrix.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Hand, ZoneType.Battlefield));

        // Sanity — static is in effect against Bob's Ballista.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        var ping = new ActivatedAbility(ballista, _bob);
        new ActionValidator().ValidateAction(new ActivateAbilityAction(ping, _bob))
            .IsValid.Should().BeFalse();

        // Damping Matrix leaves the battlefield.
        matrix.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(matrix, ZoneType.Battlefield, ZoneType.Graveyard));

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
    /// equivalent helper in Stony Silence / Cursed Totem tests.
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
