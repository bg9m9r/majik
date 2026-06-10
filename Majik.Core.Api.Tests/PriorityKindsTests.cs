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

    private GameContext Ctx(StepStateType phase = StepStateType.Upkeep) =>
        new(_alice, new[] { _alice, _bob }, _alice, turnNumber: 1, phase, _stack);

    [Fact]
    public void LandInHand_SorceryWindow_DropAvailable_OffersPlayLand()
    {
        var land = new Land("Forest");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Hand.AddCard(land); land.SetZone(ZoneType.Hand);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.PreCombatMain, _stack, landPlayAvailable: true);

        PriorityKinds.Build(ctx).Should().Contain(typeof(PlayLandCommand));
    }

    [Fact]
    public void LandInHand_SorceryWindow_DropSpent_DoesNotOfferPlayLand()
    {
        // Regression: PriorityKinds used to over-include PlayLandCommand on the
        // mere presence of a land in hand, ignoring the per-turn drop cap. The
        // auto-pass gate + the random fuzz harness trust ExpectedKinds, so they
        // proposed an illegal land every priority window once the drop was
        // spent — the loop rejected each one, flooding stderr ("rejected
        // PlayLand ... already played 1 land this turn") and (pre-fix) spinning.
        // Gating on ctx.LandPlayAvailable (the live LandDropTracker truth) stops
        // the over-include at the source.
        var land = new Land("Forest");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Hand.AddCard(land); land.SetZone(ZoneType.Hand);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.PreCombatMain, _stack, landPlayAvailable: false);

        PriorityKinds.Build(ctx).Should().NotContain(typeof(PlayLandCommand));
    }

    [Fact]
    public void EmptyBoard_EmptyHand_OpponentTurn_KindsIsPassOnly()
    {
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _bob,
            turnNumber: 1, StepStateType.Upkeep, _stack);

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
            turnNumber: 2, StepStateType.Upkeep, _stack);

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

    private Planeswalker GristOnBattlefield(int loyalty, params int[] loyaltyChanges)
    {
        var grist = new Planeswalker("Grist, the Hunger Tide", "{1}{B}{G}", loyalty);
        grist.SetOwner(_alice);
        grist.SetController(_alice);
        foreach (var change in loyaltyChanges)
        {
            grist.AddAbility(new LoyaltyAbility(grist, change, () => { }));
        }
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);
        return grist;
    }

    [Fact]
    public void LoyaltyAbility_SorceryWindow_Payable_OffersLoyaltyKind()
    {
        // CR 606.3 — own main phase, empty stack, a payable not-yet-used
        // loyalty ability → loyalty-activation kind must be advertised.
        GristOnBattlefield(3, +1, -2, -5);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.PreCombatMain, _stack);

        PriorityKinds.Build(ctx).Should().Contain(typeof(ActivateLoyaltyAbilityCommand));
    }

    [Fact]
    public void LoyaltyAbility_AllCostsTooHigh_DoesNotOfferLoyaltyKind()
    {
        // CR 606.5 — a minus ability can't reduce loyalty below 0. With loyalty
        // 1 and only −2 / −5 abilities, NONE is payable → kind excluded.
        GristOnBattlefield(1, -2, -5);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.PreCombatMain, _stack);

        PriorityKinds.Build(ctx).Should().NotContain(typeof(ActivateLoyaltyAbilityCommand));
    }

    [Fact]
    public void LoyaltyAbility_AlreadyActivatedThisTurn_DoesNotOfferLoyaltyKind()
    {
        // CR 606.3 — only one loyalty ability per planeswalker per turn.
        var grist = GristOnBattlefield(3, +1, -2);
        grist.LoyaltyAbilityActivatedThisTurn = true;

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.PreCombatMain, _stack);

        PriorityKinds.Build(ctx).Should().NotContain(typeof(ActivateLoyaltyAbilityCommand));
    }

    [Fact]
    public void LoyaltyAbility_NonEmptyStack_DoesNotOfferLoyaltyKind()
    {
        // CR 606.3 — sorcery speed requires an empty stack.
        GristOnBattlefield(3, +1, -2);
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        _stack.Push(new Majik.Core.Spells.Spell(bolt, _alice));

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.PreCombatMain, _stack);

        PriorityKinds.Build(ctx).Should().NotContain(typeof(ActivateLoyaltyAbilityCommand));
    }

    [Fact]
    public void LoyaltyAbility_OpponentTurn_DoesNotOfferLoyaltyKind()
    {
        // CR 606.3 — loyalty abilities are sorcery-speed: active player only.
        // (An instant-speed window on the opponent's turn must not offer it.)
        GristOnBattlefield(3, +1, -2);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _bob,
            turnNumber: 2, StepStateType.PreCombatMain, _stack);

        PriorityKinds.Build(ctx).Should().NotContain(typeof(ActivateLoyaltyAbilityCommand));
    }

    [Fact]
    public void LoyaltyAbility_NonMainPhase_DoesNotOfferLoyaltyKind()
    {
        // CR 606.3 — sorcery speed is a MAIN phase. Own upkeep must not offer it.
        GristOnBattlefield(3, +1, -2);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, StepStateType.Upkeep, _stack);

        PriorityKinds.Build(ctx).Should().NotContain(typeof(ActivateLoyaltyAbilityCommand));
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
