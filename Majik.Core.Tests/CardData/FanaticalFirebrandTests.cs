using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="FanaticalFirebrandFactory"/>.
///
/// Fanatical Firebrand (Dominaria, {R}):
///   Creature — Goblin Pirate 1/1.
///   Haste.
///   {T}, Sacrifice this creature: It deals 1 damage to any target.
///
/// Covers:
///   - Identity (Goblin Pirate 1/1, {R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Haste keyword marker.
///   - Activated ability shape: {T} + Sacrifice + one any-target request.
///   - Resolution: 1 damage to player / creature target; planeswalker target
///     routes through loyalty removal (CR 306.7); Firebrand sacrificed.
/// </summary>
public class FanaticalFirebrandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FanaticalFirebrand_Identity()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);

        fb.Name.Should().Be("Fanatical Firebrand");
        fb.ManaCost.Should().Be("{R}");
        fb.HasType(CardType.Creature).Should().BeTrue();
        fb.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        fb.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        fb.BasePower.Should().Be(1);
        fb.BaseToughness.Should().Be(1);
        fb.Owner.Should().BeSameAs(_alice);
        fb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FanaticalFirebrand_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Fanatical Firebrand", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fanatical Firebrand");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void FanaticalFirebrand_HasHasteKeywordMarker()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);

        fb.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Haste",
                "CR 702.10 — Haste marker for CombatAbilities.HasHaste");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FanaticalFirebrand_ActivatedAbility_HasTap_AndSacrifice_AndOneAnyTarget()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the ping ability costs {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the ping ability sacrifices Firebrand");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Ping_DealsOneToPlayerTarget_AndSacrificesFirebrand()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(19, "1 damage to Bob");
        _bob.LifeLostThisTurn.Should().Be(1);

        _alice.Zones.Graveyard.GetCards().Should().Contain(fb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fb);
        fb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Ping_DealsOneToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.Damage.Should().Be(1, "1 marked damage on the bears");
        _alice.Zones.Graveyard.GetCards().Should().Contain(fb);
    }

    // -----------------------------------------------------------------------
    // Re-source-safe (fanatical-firebrand-sac-self-pinger-rebind) — the whole
    // {T}, Sacrifice this creature: ping ability re-homes onto a new bearer via
    // ActivatedAbility.RebindTo (Agatha's Soul Cauldron group-grant; CR 707.2 /
    // 613.1f). The Mogg-Fanatic-class sac-self-pinger family.
    // -----------------------------------------------------------------------

    [Fact]
    public void FanaticalFirebrand_PingAbility_IsRebindSafe()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);
        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();

        ability.RebindSafe.Should().BeTrue(
            "the sac-self-pinger reads its source off ResolutionContext.Source "
            + "and its costs re-home, so Agatha's group-grant may RebindTo it");
    }

    [Fact]
    public void RebindTo_PingAbility_SacrificesNewBearer_AndDealsDamageFromIt_NotOriginal()
    {
        // Arrange — Firebrand (the printed source) plus a DIFFERENT creature
        // (the counter-bearer Agatha re-homes the ability to).
        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var bearer = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bearer.SetOwner(_alice);
        bearer.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);
        // CR 302.6 — the bearer has been under control since before this turn,
        // so the {T} tap cost is legal (mirrors a real Agatha-grant scenario).
        bearer.ClearSummoningSickness();

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();

        // Act — re-home the ENTIRE ability onto the bearer (CR 707.2 / 613.1f).
        var rebound = ability.RebindTo(bearer, _alice);

        // STAGE 1 — pay the re-homed source-capturing costs (tap + sacrifice):
        // they must hit the bearer, not the original Firebrand.
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        rebound.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        rebound.Resolve();

        // Sacrifice re-homed: the BEARER went to the graveyard; Firebrand stays.
        bearer.Zone.Should().Be(ZoneType.Graveyard, "the rebound sac hits the bearer");
        fb.Zone.Should().Be(ZoneType.Battlefield, "the original Firebrand is untouched");

        // Damage source re-homed: 1 damage dealt to Bob from the bearer's copy.
        _bob.LifeTotal.Should().Be(19, "the rebound ability still deals 1 to any target");
    }

    [Fact]
    public void Activate_Ping_PlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.7 — damage to a planeswalker removes loyalty counters.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        ability.Resolve();

        pw.Loyalty.Should().Be(3, "1 loyalty counter removed (4 - 1)");
    }
}
