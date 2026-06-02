using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BloodsoakedChampionFactory"/> (Khans of
/// Tarkir, {B}).
///
/// Creature — Human Warrior 2/1. Oracle text (verified against Scryfall):
///   "This creature can't block.
///    Raid — {1}{B}: Return this card from your graveyard to the
///    battlefield. Activate only if you attacked this turn."
///
/// Covers:
///   - Identity (Human Warrior 2/1 at {B}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Shape-only Create overload does NOT register the combat restriction;
///     with a <see cref="ContinuousEffectsService"/> the CannotBlock
///     restriction is registered, scoped, and non-expiring.
///   - Raid activated ability shape: {1}{B} mana cost.
///   - Raid gate (canActivateCheck) closed until the owner attacks; opens
///     after a CreatureAttacksEvent for the owner; resets on the owner's
///     next turn.
///   - Activation resolves: returns the Champion from graveyard to
///     battlefield; no-ops if the Champion isn't in the graveyard.
/// </summary>
[Trait("Color", "B")]
public class BloodsoakedChampionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BloodsoakedChampion_Identity()
    {
        var c = BloodsoakedChampionFactory.Create(_alice);

        c.Name.Should().Be("Bloodsoaked Champion");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BloodsoakedChampion_DispatchesViaNamedFactory()
    {
        var c = NamedCardFactory.Create("Bloodsoaked Champion", _alice);

        c.Should().BeOfType<Creature>();
        ((Creature)c).Name.Should().Be("Bloodsoaked Champion");
    }

    // -----------------------------------------------------------------------
    // Can't block — CR 509.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodsoakedChampion_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = BloodsoakedChampionFactory.Create(_alice);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    [Fact]
    public void BloodsoakedChampion_WithEffectsService_RegistersCannotBlockRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects, zoneService: null, eventBus: null, attackedThisTurn: null);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "CR 509.1c — Bloodsoaked Champion's static 'can't block' rider is " +
            "registered as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void BloodsoakedChampion_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects, zoneService: null, eventBus: null, attackedThisTurn: null);

        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "the can't-block is a permanent static — it does NOT expire at end of turn");
    }

    // -----------------------------------------------------------------------
    // Raid activated ability shape — CR 113.6 / 117.1a
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodsoakedChampion_RaidAbility_HasManaCost()
    {
        var c = BloodsoakedChampionFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the Raid return is paid with a single mana cost");
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Generic.Should().Be(1, "the Raid return costs {1}{B}");
        manaCost.Cost.Black.Should().Be(1, "the Raid return costs {1}{B}");
        manaCost.Cost.TotalValue.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Raid gate — "Activate only if you attacked this turn."
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodsoakedChampion_RaidGate_ClosedBeforeAttack()
    {
        var bus = new EventBus();
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects: null, zoneService: null, eventBus: bus, attackedThisTurn: null);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.CanActivateNow().Should().BeFalse(
            "Raid — the gate is closed until the owner attacks this turn");
    }

    [Fact]
    public void BloodsoakedChampion_RaidGate_OpensAfterOwnerAttacks()
    {
        var bus = new EventBus();
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects: null, zoneService: null, eventBus: bus, attackedThisTurn: null);

        var attacker = new Creature("Some Attacker", "{1}", 2, 2);
        attacker.SetOwner(_alice);
        attacker.SetController(_alice);

        bus.Publish(new CreatureAttacksEvent(attacker, _bob));

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.CanActivateNow().Should().BeTrue(
            "Raid — the owner attacked this turn, so the return is activatable");
    }

    [Fact]
    public void BloodsoakedChampion_RaidGate_IgnoresOpponentAttacks()
    {
        var bus = new EventBus();
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects: null, zoneService: null, eventBus: bus, attackedThisTurn: null);

        var opponentAttacker = new Creature("Bob's Attacker", "{1}", 2, 2);
        opponentAttacker.SetOwner(_bob);
        opponentAttacker.SetController(_bob);

        bus.Publish(new CreatureAttacksEvent(opponentAttacker, _alice));

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.CanActivateNow().Should().BeFalse(
            "Raid — only the controller's own attacks open the gate");
    }

    [Fact]
    public void BloodsoakedChampion_RaidGate_ResetsOnOwnerTurnStart()
    {
        var bus = new EventBus();
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects: null, zoneService: null, eventBus: bus, attackedThisTurn: null);

        var attacker = new Creature("Some Attacker", "{1}", 2, 2);
        attacker.SetOwner(_alice);
        attacker.SetController(_alice);
        bus.Publish(new CreatureAttacksEvent(attacker, _bob));

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.CanActivateNow().Should().BeTrue();

        // New turn for the owner — "this turn" resets.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 3));

        ability.CanActivateNow().Should().BeFalse(
            "Raid — the attacked-this-turn fact resets at the start of the owner's turn");
    }

    [Fact]
    public void BloodsoakedChampion_RaidGate_UsesFallbackPredicateWithoutBus()
    {
        var attacked = new[] { false };
        var c = BloodsoakedChampionFactory.Create(
            _alice, effects: null, zoneService: null, eventBus: null,
            attackedThisTurn: () => attacked[0]);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.CanActivateNow().Should().BeFalse();

        attacked[0] = true;
        ability.CanActivateNow().Should().BeTrue(
            "without a bus, the caller-supplied attacked-this-turn predicate gates the Raid");
    }

    // -----------------------------------------------------------------------
    // Raid resolution — returns self from graveyard to battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodsoakedChampion_RaidResolution_ReturnsFromGraveyardToBattlefield()
    {
        var c = BloodsoakedChampionFactory.Create(_alice);

        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        c.Zone.Should().Be(ZoneType.Battlefield,
            "Raid — the Champion returns from graveyard to the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(c);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void BloodsoakedChampion_RaidResolution_NotInGraveyard_IsNoOp()
    {
        var c = BloodsoakedChampionFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in ability.Effects) e.Execute(); };

        act.Should().NotThrow();
        c.Zone.Should().Be(ZoneType.Battlefield,
            "resolution re-checks the graveyard zone — an off-zone activation is a no-op");
    }
}
