using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="IfnirDeadlandsFactory"/>.
///
/// Ifnir Deadlands — Land — Desert (Amonkhet). The black twin of
/// <see cref="RamunapRuinsFactory"/>.
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add {B}.
///    {2}{B}{B}, {T}, Sacrifice a Desert: Put two -1/-1 counters on target
///    creature an opponent controls. Activate only as a sorcery."
///
/// Covers:
/// - Identity (Land, Desert subtype, non-Basic, non-Legendary, name,
///   owner/controller) + dispatcher routing through
///   <see cref="NamedCardFactory"/>.
/// - {T}: Add {C} — colourless mana ability (CR 605.1) from the JSON
///   definition; {C} stored as generic.
/// - {T}, Pay 1 life: Add {B} — second mana ability producing {B}; activation
///   loses 1 life and taps the land; gated on life &gt; 1 (CR 119.4).
/// - {2}{B}{B}, {T}, Sacrifice a Desert: put two -1/-1 counters on target
///   creature an opponent controls — sorcery-speed activated ability with
///   mana + tap + sacrifice costs; resolution stamps two -1/-1 counters on the
///   chosen creature (CR 122) and sacrifices this land.
/// </summary>
[Trait("Color", "C")]
public class IfnirDeadlandsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void IfnirDeadlands_IsLand_Desert_WithCorrectName()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("printed type is Land — Desert");
        land.Name.Should().Be("Ifnir Deadlands");
    }

    [Fact]
    public void IfnirDeadlands_OwnerAndControllerAreSet()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IfnirDeadlands_IsNotBasic_AndNotLegendary()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void IfnirDeadlands_RoutesThroughDispatcher()
    {
        var land = (Land)NamedCardFactory.Create("Ifnir Deadlands", _alice);
        land.Name.Should().Be("Ifnir Deadlands");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {C} and {T}, Pay 1 life: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void IfnirDeadlands_HasTwoManaAbilities_ColourlessAndBlack()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "{T}: Add {C} and {T}, Pay 1 life: Add {B}");

        // {C} is stored as generic (no dedicated colourless bucket).
        manaAbilities.Should().Contain(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.Black == 0,
            "{T}: Add {C} produces one colourless mana (modeled as generic)");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1,
            "{T}, Pay 1 life: Add {B} produces one black mana");
    }

    [Fact]
    public void IfnirDeadlands_BlackManaAbility_Activation_LosesOneLifeAndTaps()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        var blackMana = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Black == 1);

        blackMana.Activate();

        _alice.LifeTotal.Should().Be(19, "tapping for {B} costs Pay 1 life (CR 119.4)");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void IfnirDeadlands_ColourlessManaAbility_Activation_DoesNotLoseLife()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        var colourless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.Black == 0);

        colourless.Activate();

        _alice.LifeTotal.Should().Be(20, "{T}: Add {C} has no life cost");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void IfnirDeadlands_BlackManaAbility_CannotActivateAtOneLife()
    {
        var lowLife = new Player("LowLife", 1);
        var land = IfnirDeadlandsFactory.Create(lowLife);
        var blackMana = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Black == 1);

        blackMana.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life with only 1 life remaining");
    }

    // -----------------------------------------------------------------------
    // Sacrifice ability — {2}{B}{B}, {T}, Sacrifice a Desert: two -1/-1 counters
    // -----------------------------------------------------------------------

    [Fact]
    public void IfnirDeadlands_HasExactlyOneActivatedAbility_WithManaTapAndSacrificeCosts()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Black.Should().Be(2, "{2}{B}{B} charges two black");
        manaCost.Generic.Should().Be(2, "{2}{B}{B} charges two generic");
    }

    [Fact]
    public void IfnirDeadlands_SacAbility_IsSorcerySpeed()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.IsSorcerySpeed.Should().BeTrue(
            "CR 117.1a / 307.5 — \"Activate only as a sorcery\"");
    }

    [Fact]
    public void IfnirDeadlands_SacAbility_DeclaresOneTargetRequest()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        var request = ability.TargetRequests.Should().ContainSingle().Subject;
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void IfnirDeadlands_SacAbility_StampsTwoMinusOneCounters_AndSacrificesSelf()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Bob controls a creature — a legal "creature an opponent controls".
        var victim = new Creature("Test Victim", "{2}", 3, 3);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });

        sac.Resolve();

        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(2,
            "CR 122 — two -1/-1 counters are placed on the chosen creature");
        land.Zone.Should().Be(ZoneType.Graveyard,
            "Ifnir Deadlands sacrifices a Desert (itself) as part of resolution");
    }

    [Fact]
    public void IfnirDeadlands_SacAbility_OwnCreatureIsIllegalTarget_NoCounters_ButStillSacrifices()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // The land's controller's OWN creature is not "a creature an opponent
        // controls" (CR 608.2b) — the counter half must no-op.
        var ownCreature = new Creature("Friendly", "{1}", 2, 2);
        ownCreature.SetOwner(_alice);
        ownCreature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownCreature);
        ownCreature.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ownCreature } });

        sac.Resolve();

        ownCreature.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0,
            "CR 608.2b — a creature the controller controls is an illegal target");
        land.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost is already paid, so the land is sacrificed regardless");
    }

    [Fact]
    public void IfnirDeadlands_SacAbility_WithoutTarget_NoOps_ButStillSacrifices()
    {
        var land = IfnirDeadlandsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        // No chosen target — the counter half no-ops, but the sacrifice still
        // happens (same posture as Ramunap Ruins' resolver-less path).
        land.Zone.Should().Be(ZoneType.Graveyard);
    }
}
