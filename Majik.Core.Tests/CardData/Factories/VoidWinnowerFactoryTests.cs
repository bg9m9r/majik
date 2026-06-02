using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Void Winnower — Creature — Eldrazi {9} 11/9 (CR 601.3 / 202.3
/// printed statics: "Your opponents can't cast spells with even mana values.
/// (Zero is even.) Your opponents can't block with creatures with even mana
/// values.").
///
/// Covers:
/// - Card identity / subtype / P/T / dispatcher routing.
/// - The cast-restriction static (even mana value) wired via
///   <see cref="EvenManaValueCastRestrictionEffect"/> +
///   <see cref="CastingRestrictions"/>, observed through
///   <see cref="ActionValidator"/>; applies to every spell type; zero is even;
///   the controller is never restricted; LTB releases the restriction.
/// - The block-restriction static (even mana value) wired via a predicate-mode
///   <see cref="CombatRestrictionEffect"/>, observed through
///   <see cref="Majik.Core.Combat.CombatValidator.CanBlock"/>.
///
/// Tests dispose-clean the static <see cref="CastingRestrictions"/> registry to
/// prevent cross-test leakage.
/// </summary>
public class VoidWinnowerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public VoidWinnowerFactoryTests()
    {
        _zones = new ZoneService(_bus);
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    private void PutOnBattlefield(Creature card, Player controller)
    {
        controller.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VoidWinnower_HasCorrectIdentity_AndPT_AndSubtype()
    {
        var winnower = VoidWinnowerFactory.Create(_alice);

        winnower.Name.Should().Be("Void Winnower");
        winnower.ManaCost.Should().Be("{9}");
        winnower.HasType(CardType.Creature).Should().BeTrue();
        winnower.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        winnower.Power.Should().Be(11);
        winnower.Toughness.Should().Be(9);
        winnower.Owner.Should().BeSameAs(_alice);
        winnower.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesVoidWinnower_ToFactory()
    {
        var card = NamedCardFactory.Create("Void Winnower", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Void Winnower");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).Power.Should().Be(11);
        ((Creature)card).Toughness.Should().Be(9);
    }

    // -----------------------------------------------------------------------
    // Cast restriction — CR 601.3 / 202.3 even mana value
    // -----------------------------------------------------------------------

    [Fact]
    public void WinnowerOnBattlefield_BlocksOpponentCast_OfEvenManaValueSpell()
    {
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: () => new[] { _bob }, eventBus: _bus, effects: null);
        PutOnBattlefield(winnower, _alice);

        // {2} = mana value 2 (even) — rejected.
        var even = new Sorcery("Divination", "{2}") { Owner = _bob };
        var action = new CastSpellAction(even, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void WinnowerOnBattlefield_BlocksOpponentCast_OfZeroManaValueSpell()
    {
        // CR 202.3 — "Zero is even."
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: () => new[] { _bob }, eventBus: _bus, effects: null);
        PutOnBattlefield(winnower, _alice);

        var zero = new Instant("Ornithopter", "{0}") { Owner = _bob };
        var action = new CastSpellAction(zero, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse();
    }

    [Fact]
    public void WinnowerOnBattlefield_BlocksOpponentCast_OfEvenCreatureSpell()
    {
        // Applies to EVERY spell type, not just noncreature (unlike Sanctum
        // Prelate).
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: () => new[] { _bob }, eventBus: _bus, effects: null);
        PutOnBattlefield(winnower, _alice);

        var evenCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob };
        var action = new CastSpellAction(evenCreature, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse();
    }

    [Fact]
    public void WinnowerOnBattlefield_AllowsOpponentCast_OfOddManaValueSpell()
    {
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: () => new[] { _bob }, eventBus: _bus, effects: null);
        PutOnBattlefield(winnower, _alice);

        // {R} = mana value 1 (odd) — allowed.
        var odd = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(odd, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void WinnowerOnBattlefield_DoesNotRestrictController()
    {
        // CR 109.5 — "your opponents" never includes Void Winnower's controller.
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: () => new[] { _bob }, eventBus: _bus, effects: null);
        PutOnBattlefield(winnower, _alice);

        var even = new Sorcery("Divination", "{2}") { Owner = _alice };
        var action = new CastSpellAction(even, _alice, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void WinnowerLeavingBattlefield_ReleasesCastRestriction()
    {
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: () => new[] { _bob }, eventBus: _bus, effects: null);
        PutOnBattlefield(winnower, _alice);

        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeTrue();

        _zones.MoveCard(winnower, ZoneType.Battlefield, ZoneType.Graveyard);

        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeFalse();

        var even = new Sorcery("Divination", "{2}") { Owner = _bob };
        var action = new CastSpellAction(even, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ShapeOnly_DoesNotRegisterCastRestriction()
    {
        var winnower = VoidWinnowerFactory.Create(_alice);
        PutOnBattlefield(winnower, _alice);

        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CastingRestrictions registry — direct unit-level coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingRestrictions_AddAndRemove_EvenManaValueBlock_Toggles()
    {
        var token = new object();
        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeFalse();

        CastingRestrictions.AddEvenManaValueCastBlock(token, _bob);
        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeTrue();

        // Idempotent for the same (token, player).
        CastingRestrictions.AddEvenManaValueCastBlock(token, _bob);
        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeTrue();

        CastingRestrictions.RemoveEvenManaValueCastBlock(token);
        CastingRestrictions.CannotCastEvenManaValueSpell(_bob).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Block restriction — CR 509.1c / 202.3 even mana value
    // -----------------------------------------------------------------------

    [Fact]
    public void WinnowerOnBattlefield_RestrictsOpponentEvenManaValueBlocker()
    {
        var effects = new ContinuousEffectsService();
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: null, eventBus: _bus, effects: effects);
        PutOnBattlefield(winnower, _alice);

        // Bob's even-MV creature ({1}{G} = 2) can't block.
        var evenBlocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        evenBlocker.SetOwner(_bob);
        evenBlocker.SetController(_bob);

        effects.HasRestriction(evenBlocker, CombatRestriction.CannotBlock)
            .Should().BeTrue();
    }

    [Fact]
    public void WinnowerOnBattlefield_AllowsOpponentOddManaValueBlocker()
    {
        var effects = new ContinuousEffectsService();
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: null, eventBus: _bus, effects: effects);
        PutOnBattlefield(winnower, _alice);

        // Bob's odd-MV creature ({G} = 1) can still block.
        var oddBlocker = new Creature("Llanowar Elves", "{G}", 1, 1);
        oddBlocker.SetOwner(_bob);
        oddBlocker.SetController(_bob);

        effects.HasRestriction(oddBlocker, CombatRestriction.CannotBlock)
            .Should().BeFalse();
    }

    [Fact]
    public void WinnowerOnBattlefield_DoesNotRestrictOwnEvenManaValueBlocker()
    {
        // CR 109.5 — only "your opponents" creatures are restricted.
        var effects = new ContinuousEffectsService();
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: null, eventBus: _bus, effects: effects);
        PutOnBattlefield(winnower, _alice);

        var aliceEvenBlocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceEvenBlocker.SetOwner(_alice);
        aliceEvenBlocker.SetController(_alice);

        effects.HasRestriction(aliceEvenBlocker, CombatRestriction.CannotBlock)
            .Should().BeFalse();
    }

    [Fact]
    public void WinnowerLeavingBattlefield_ReleasesBlockRestriction()
    {
        var effects = new ContinuousEffectsService();
        var winnower = VoidWinnowerFactory.Create(
            _alice, opponentResolver: null, eventBus: _bus, effects: effects);
        PutOnBattlefield(winnower, _alice);

        var evenBlocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        evenBlocker.SetOwner(_bob);
        evenBlocker.SetController(_bob);

        effects.HasRestriction(evenBlocker, CombatRestriction.CannotBlock)
            .Should().BeTrue();

        // Void Winnower leaves — the predicate's IsActive gate goes false.
        _zones.MoveCard(winnower, ZoneType.Battlefield, ZoneType.Graveyard);

        effects.HasRestriction(evenBlocker, CombatRestriction.CannotBlock)
            .Should().BeFalse();
    }

    [Fact]
    public void ShapeOnly_DoesNotRegisterBlockRestriction()
    {
        var effects = new ContinuousEffectsService();
        var winnower = VoidWinnowerFactory.Create(_alice);
        PutOnBattlefield(winnower, _alice);

        var evenBlocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        evenBlocker.SetOwner(_bob);
        evenBlocker.SetController(_bob);

        effects.HasRestriction(evenBlocker, CombatRestriction.CannotBlock)
            .Should().BeFalse();
    }
}
