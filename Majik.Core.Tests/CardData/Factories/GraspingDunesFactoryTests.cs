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
/// Tests for <see cref="GraspingDunesFactory"/>.
///
/// Grasping Dunes — Land — Desert (Hour of Devastation). The colourless,
/// single-counter cousin of <see cref="IfnirDeadlandsFactory"/>.
/// Oracle text:
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice this land: Put a -1/-1 counter on target creature.
///    Activate only as a sorcery."
///
/// Covers:
/// - Identity (Land, Desert subtype, non-Basic, non-Legendary, name,
///   owner/controller) + dispatcher routing through
///   <see cref="NamedCardFactory"/>.
/// - {T}: Add {C} — colourless mana ability (CR 605.1) from the JSON
///   definition; {C} stored as generic. Exactly one mana ability (no Pay-1-life
///   black mode like Ifnir Deadlands).
/// - {1}, {T}, Sacrifice this land: put a -1/-1 counter on target creature —
///   sorcery-speed activated ability with {1} mana + tap + sacrifice costs;
///   resolution stamps one -1/-1 counter on the chosen creature (CR 122) and
///   sacrifices this land. Targets ANY creature (no opponent restriction).
/// </summary>
[Trait("Color", "C")]
public class GraspingDunesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GraspingDunes_IsLand_Desert_WithCorrectName()
    {
        var land = GraspingDunesFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("printed type is Land — Desert");
        land.Name.Should().Be("Grasping Dunes");
    }

    [Fact]
    public void GraspingDunes_OwnerAndControllerAreSet()
    {
        var land = GraspingDunesFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GraspingDunes_IsNotBasic_AndNotLegendary()
    {
        var land = GraspingDunesFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void GraspingDunes_RoutesThroughDispatcher()
    {
        var land = (Land)NamedCardFactory.Create("Grasping Dunes", _alice);
        land.Name.Should().Be("Grasping Dunes");
    }

    // -----------------------------------------------------------------------
    // Mana ability — {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void GraspingDunes_HasExactlyOneColourlessManaAbility()
    {
        var land = GraspingDunesFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "{T}: Add {C} is the only mana ability");

        // {C} is stored as generic (no dedicated colourless bucket).
        manaAbilities.Single().ManaGenerated.Generic.Should().Be(1,
            "{T}: Add {C} produces one colourless mana (modeled as generic)");
    }

    [Fact]
    public void GraspingDunes_ColourlessManaAbility_Activation_DoesNotLoseLife()
    {
        var land = GraspingDunesFactory.Create(_alice);
        var colourless = land.Abilities.OfType<ManaAbility>().Single();

        colourless.Activate();

        _alice.LifeTotal.Should().Be(20, "{T}: Add {C} has no life cost");
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Sacrifice ability — {1}, {T}, Sacrifice this land: a -1/-1 counter
    // -----------------------------------------------------------------------

    [Fact]
    public void GraspingDunes_HasExactlyOneActivatedAbility_WithManaTapAndSacrificeCosts()
    {
        var land = GraspingDunesFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "{1} charges one generic");
        ability.Costs.OfType<AdditionalCost>().Should().HaveCount(2,
            "{T} (tap) + Sacrifice this land are the two additional costs");
    }

    [Fact]
    public void GraspingDunes_SacAbility_IsSorcerySpeed()
    {
        var land = GraspingDunesFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.IsSorcerySpeed.Should().BeTrue(
            "CR 117.1a / 307.5 — \"Activate only as a sorcery\"");
    }

    [Fact]
    public void GraspingDunes_SacAbility_DeclaresOneTargetRequest()
    {
        var land = GraspingDunesFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        var request = ability.TargetRequests.Should().ContainSingle().Subject;
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void GraspingDunes_SacAbility_StampsOneMinusOneCounter_OnOpponentCreature_AndSacrificesSelf()
    {
        var land = GraspingDunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Test Victim", "{2}", 3, 3);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });

        sac.Resolve();

        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "CR 122 — one -1/-1 counter is placed on the chosen creature");
        land.Zone.Should().Be(ZoneType.Graveyard,
            "Grasping Dunes sacrifices this land as part of resolution");
    }

    [Fact]
    public void GraspingDunes_SacAbility_CanTargetOwnCreature_NoOpponentRestriction()
    {
        var land = GraspingDunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Grasping Dunes says "target creature" with no controller restriction
        // (unlike Ifnir Deadlands' "an opponent controls"), so the controller's
        // OWN creature is a legal target and gets the counter.
        var ownCreature = new Creature("Friendly", "{1}", 2, 2);
        ownCreature.SetOwner(_alice);
        ownCreature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownCreature);
        ownCreature.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ownCreature } });

        sac.Resolve();

        ownCreature.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "\"target creature\" has no opponent restriction — own creatures are legal");
        land.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void GraspingDunes_SacAbility_WithoutTarget_NoOps_ButStillSacrifices()
    {
        var land = GraspingDunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        // No chosen target — the counter half no-ops, but the sacrifice still
        // happens (same posture as Ifnir Deadlands' resolver-less path).
        land.Zone.Should().Be(ZoneType.Graveyard);
    }
}
