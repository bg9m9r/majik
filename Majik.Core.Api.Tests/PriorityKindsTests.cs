using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Regression coverage for the
/// <see cref="Majik.Core.Api.PriorityKinds"/> narrowing — specifically the
/// empty-action / dead-window detection that drives the engine's
/// server-side auto-pass gate (PriorityLoop, Slice 5a).
///
/// <para>The user-reported regression: a summoning-sick Delighted Halfling
/// on the battlefield made every priority window non-dead because the old
/// code advertised the <c>ActivateManaAbilityCommand</c> kind on the mere
/// PRESENCE of a mana ability — without consulting CR 302.6's gate that
/// blocks {T} activations on summoning-sick non-haste creatures. The fix
/// gates on <see cref="IManaAbility.CanActivate"/>, which already wraps
/// the rule.</para>
/// </summary>
public class PriorityKindsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityKindsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    private GameContext Ctx(PhaseStateType phase = PhaseStateType.Upkeep) =>
        new(_alice, new[] { _alice, _bob }, _alice, turnNumber: 1, phase, _stack);

    [Fact]
    public void EmptyBoard_EmptyHand_OpponentTurn_KindsIsPassOnly()
    {
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _bob,
            turnNumber: 1, PhaseStateType.Upkeep, _stack);

        var kinds = PriorityKinds.Build(ctx);

        kinds.Should().Equal(typeof(PassPriorityCommand));
        PriorityKinds.IsPassOnly(kinds).Should().BeTrue();
    }

    [Fact]
    public void SummoningSickCreatureWithManaAbility_NoMana_NoCastable_IsPassOnly()
    {
        // The Delighted Halfling case. Sick creature with a {T} mana ability,
        // empty hand, opponent's upkeep. Engine MUST narrow to PASS-ONLY so
        // PriorityLoop's auto-pass gate fires and the user is not prompted.
        var halfling = new Creature("Delighted Halfling", "{G}", 1, 2);
        halfling.SetOwner(_alice);
        halfling.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(halfling);
        halfling.SetZone(ZoneType.Battlefield);
        // Sick by default for a freshly-entered creature — but the test is
        // explicit to document the precondition.
        halfling.HasSummoningSickness = true;
        halfling.AddAbility(new ManaAbility(halfling, _alice, ManaCost.Parse("G")));

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _bob,
            turnNumber: 2, PhaseStateType.Upkeep, _stack);

        var kinds = PriorityKinds.Build(ctx);

        kinds.Should().Equal(typeof(PassPriorityCommand));
        PriorityKinds.IsPassOnly(kinds).Should().BeTrue();
    }

    [Fact]
    public void UnsickCreatureWithManaAbility_AdvertisesManaAbilityKind()
    {
        // Same shape but the creature has shed summoning sickness. The
        // kind MUST appear so the player can still tap for mana.
        var halfling = new Creature("Delighted Halfling", "{G}", 1, 2);
        halfling.SetOwner(_alice);
        halfling.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(halfling);
        halfling.SetZone(ZoneType.Battlefield);
        halfling.HasSummoningSickness = false;
        halfling.AddAbility(new ManaAbility(halfling, _alice, ManaCost.Parse("G")));

        var kinds = PriorityKinds.Build(Ctx());

        kinds.Should().Contain(typeof(ActivateManaAbilityCommand));
        PriorityKinds.IsPassOnly(kinds).Should().BeFalse();
    }

    [Fact]
    public void TappedManaSource_DoesNotAdvertiseManaAbilityKind()
    {
        // Already-tapped land. CanActivate() returns false → kind must
        // NOT appear → dead window detection fires.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);
        forest.AddAbility(new ManaAbility(forest, _alice, ManaCost.Parse("G")));
        forest.Tap();

        var kinds = PriorityKinds.Build(Ctx());

        kinds.Should().NotContain(typeof(ActivateManaAbilityCommand));
        PriorityKinds.IsPassOnly(kinds).Should().BeTrue();
    }

    [Fact]
    public void UntappedLandWithManaAbility_AdvertisesManaAbilityKind()
    {
        // Lands are never summoning-sick (CR 302.6 only applies to
        // creatures). Untapped Forest must surface the mana kind.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);
        forest.AddAbility(new ManaAbility(forest, _alice, ManaCost.Parse("G")));

        var kinds = PriorityKinds.Build(Ctx());

        kinds.Should().Contain(typeof(ActivateManaAbilityCommand));
    }
}
