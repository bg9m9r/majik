using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EscapeTunnelFactory"/> — Murders at Karlov Manor land:
///   "{T}, Sacrifice this land: Search your library for a basic land card, put
///    it onto the battlefield tapped, then shuffle.
///    {T}, Sacrifice this land: Target creature with power 2 or less can't be
///    blocked this turn."
///
/// Covers only the card's unique behaviour (its two activated abilities):
/// - the sac-to-fetch-basic-tapped ability (same shape as Terramorphic
///   Expanse / Evolving Wilds), and
/// - the targeted "can't be blocked this turn" grant gated on power 2 or less
///   (CR 608.2b resolution-time legality guard), modelled like Rogue's Passage.
///
/// NamedCardFactory dispatch + type well-formedness are asserted globally by
/// <c>CardFactoryContractTests</c>, so they are intentionally not duplicated.
/// </summary>
[Trait("Color", "C")]
public class EscapeTunnelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void HasTwoTapSacrificeActivatedAbilities()
    {
        var land = EscapeTunnelFactory.Create(_alice);

        var abilities = land.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(2, "the fetch ability + the unblockable ability");

        foreach (var ability in abilities)
        {
            ability.Costs.OfType<AdditionalCost>()
                .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap);
        }

        // Exactly one of the two abilities is the 1..1 targeted unblockable.
        var targeted = abilities.Where(a => a.TargetRequests.Count == 1).ToList();
        targeted.Should().ContainSingle();
        targeted[0].TargetRequests[0].MinTargets.Should().Be(1);
        targeted[0].TargetRequests[0].MaxTargets.Should().Be(1);
        targeted[0].TargetRequests[0].Description.Should().Contain("power 2 or less");
    }

    [Fact]
    public void Fetch_TutorsBasicLandTapped_NoLifePaid_AndSacrifices()
    {
        // Stage a basic + a nonbasic dual-typed land in library; activation
        // must pick the basic and leave the dual alone (CR 205.4a).
        var basicForest = new Land(
            "Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(basicForest);
        _alice.Zones.Library.AddCard(stomping);
        basicForest.SetZone(ZoneType.Library);
        stomping.SetZone(ZoneType.Library);

        var tunnel = EscapeTunnelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(tunnel);
        tunnel.SetZone(ZoneType.Battlefield);

        // The fetch ability is the one with no target requests.
        var fetch = tunnel.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);
        foreach (var e in fetch.Effects) e.Execute();

        // Basic forest fetched to battlefield tapped; dual stays in library.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.IsTapped.Should().BeTrue();
        _alice.Zones.Library.GetCards().Should().Contain(stomping);
        _alice.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Tunnel self-sacrificed; no life payment.
        _alice.Zones.Graveyard.GetCards().Should().Contain(tunnel);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(tunnel);
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Unblockable_AgainstPower2Creature_GrantsCantBeBlockedUntilEot()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var tunnel = EscapeTunnelFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(tunnel);
        tunnel.SetZone(ZoneType.Battlefield);

        var ability = tunnel.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();

        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue("a power-2 creature is a legal target");

        // The tunnel self-sacrificed as the cost.
        _alice.Zones.Graveyard.GetCards().Should().Contain(tunnel);

        effects.ExpireEndOfTurn();
        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked)
            .Should().BeFalse("the grant is only \"this turn\" (CR 514.2 EOT expiry)");
    }

    [Fact]
    public void Unblockable_AgainstPower3Creature_IsNoOp()
    {
        // CR 608.2b — "power 2 or less" is part of the target restriction; a
        // power-3 creature is an illegal target → the effect does nothing.
        var ogre = new Creature("Hill Giant", "{3}{R}", 3, 3);
        ogre.SetOwner(_alice);
        ogre.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ogre);
        ogre.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var tunnel = EscapeTunnelFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(tunnel);
        tunnel.SetZone(ZoneType.Battlefield);

        var ability = tunnel.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ogre },
        });

        foreach (var e in ability.Effects) e.Execute();

        effects.HasRestriction(ogre, CombatRestriction.CannotBeBlocked)
            .Should().BeFalse("power 3 exceeds the \"power 2 or less\" restriction");
    }

    [Fact]
    public void Unblockable_IllegalTarget_NoRestrictionRegistered()
    {
        // A Player is not a Creature → CR 608.2b no-op (must not throw).
        var effects = new ContinuousEffectsService();
        var tunnel = EscapeTunnelFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(tunnel);
        tunnel.SetZone(ZoneType.Battlefield);

        var ability = tunnel.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        var resolve = () => { foreach (var e in ability.Effects) e.Execute(); };
        resolve.Should().NotThrow();

        var dummy = new Creature("Dummy", "{G}", 1, 1);
        effects.HasRestriction(dummy, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }
}
