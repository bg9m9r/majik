using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CastleEmberethFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "This land enters tapped unless you control a Mountain.
///    {T}: Add {R}.
///    {1}{R}{R}, {T}: Creatures you control get +1/+0 until end of turn."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Castle
/// Embereth is NOT itself a Mountain.
///
/// Mirrors <see cref="CastleArdenvaleFactoryTests"/> / <see cref="CastleLocthwainFactoryTests"/>
/// (same ELD Castle cycle) — only the gating subtype (Mountain), produced
/// colour ({R}), and the second activated ability (team +1/+0 pump) differ.
/// </summary>
[Trait("Color", "C")]
public class CastleEmberethFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: place Castle Embereth on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var castle = CastleEmberethFactory.Create(_alice);
        castle.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castle);
        return castle;
    }

    // -----------------------------------------------------------------------
    // Helper: add a basic Mountain to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Land AddMountain(Player controller)
    {
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = controller, Controller = controller };
        mountain.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(mountain);
        return mountain;
    }

    // -----------------------------------------------------------------------
    // Helper: add a vanilla creature (with a live effects service) to a
    // player's battlefield so pump can be observed.
    // -----------------------------------------------------------------------
    private static Creature AddCreature(Player controller, int power, int toughness)
    {
        var creature = new Creature(
            name: "Bear",
            manaCost: "{1}{G}",
            power: power,
            toughness: toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Bear })
        { Owner = controller, Controller = controller };
        creature.SetZone(ZoneType.Battlefield);
        creature.ActiveEffects = new ContinuousEffectsService();
        controller.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCastleEmbereth()
    {
        var castle = CastleEmberethFactory.Create(_alice);
        castle.Name.Should().Be("Castle Embereth");
        castle.HasType(CardType.Land).Should().BeTrue();
        castle.Owner.Should().BeSameAs(_alice);
        castle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary_NotMountain()
    {
        var castle = CastleEmberethFactory.Create(_alice);
        castle.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Castle Embereth is nonbasic");
        castle.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Castle Embereth is not legendary");
        castle.HasSubtype(CardSubtype.Mountain).Should().BeFalse(
            "Castle Embereth has no Mountain subtype and cannot satisfy its own ETB predicate");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var castle = CastleEmberethFactory.Create(_alice);
        castle.Abilities.Should().HaveCount(2,
            "one {T}: Add {R} mana ability + one {1}{R}{R},{T} activated ability");
        castle.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        castle.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneRed()
    {
        var castle = PlaceOnBattlefield();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Red.Should().Be(1, "the mana ability produces {R}");
        produced.Generic.Should().Be(0);
        castle.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var castle = PlaceOnBattlefield();
        castle.Tap();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var castle = CastleEmberethFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Embereth enters tapped when controller has no Mountain");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasAMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        AddMountain(alice);

        var castle = CastleEmberethFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Castle Embereth enters untapped when controller has a Mountain");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddMountain(bob);

        var castle = CastleEmberethFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the 'you control' predicate checks the controller's battlefield, not the opponent's");
    }

    [Fact]
    public void PredicateExcludesSelf_CastleIsNotAMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var castle = CastleEmberethFactory.Create(alice, replacements: bus);
        alice.Zones.Battlefield.AddCard(castle);
        castle.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Embereth has no Mountain subtype; its presence on battlefield doesn't satisfy the predicate");
    }
    // -----------------------------------------------------------------------
    // Activated ability: {1}{R}{R}, {T} — cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasExpectedCosts()
    {
        var castle = CastleEmberethFactory.Create(_alice);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2,
            "costs are {1}{R}{R} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {1}{R}{R} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Activated ability: team +1/+0 until end of turn (CR 613.1c Layer 7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_PumpsAllCreaturesYouControl_PlusOnePlusZero()
    {
        var castle = PlaceOnBattlefield();
        var bear = AddCreature(_alice, power: 2, toughness: 2);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the effect directly (unit-test shortcut — costs verified
        // separately above; here we exercise the resolve body).
        ability.Effects.Single().Execute();

        bear.Power.Should().Be(3, "+1/+0 raises 2 power to 3");
        bear.Toughness.Should().Be(2, "+1/+0 leaves toughness unchanged");
    }

    [Fact]
    public void Activate_DoesNotPumpOpponentCreatures()
    {
        var bob = new Player("Bob", 20);
        var castle = PlaceOnBattlefield();
        var enemyBear = AddCreature(bob, power: 2, toughness: 2);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        ability.Effects.Single().Execute();

        enemyBear.Power.Should().Be(2, "only creatures you control are pumped (CR 608.2)");
        enemyBear.Toughness.Should().Be(2);
    }
}
