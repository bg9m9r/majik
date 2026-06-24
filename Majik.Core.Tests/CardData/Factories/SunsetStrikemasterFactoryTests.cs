using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SunsetStrikemasterFactory"/> — Sunset Strikemaster
/// (Outlaws of Thunder Junction, {1}{R}). Creature — Human Monk 3/1. Oracle
/// text (verified against the embedded Modern seed):
///   "{T}: Add {R}.
///    {2}{R}, {T}, Sacrifice this creature: It deals 6 damage to target
///    creature with flying."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (Human Monk 3/1 at {1}{R}) — the single *_Identity assert.
///   - {T}: Add {R} mana ability — taps and produces one red.
///   - The sac-damage activated ability shape: {2}{R} + {T} + Sacrifice costs,
///     one 1..1 "creature with flying" target request.
///   - Resolution: 6 damage to a flying target creature, Strikemaster sacrificed.
///   - The "creature with flying" filter (CR 702.9) accepts a flyer and rejects
///     a non-flyer (the engine gap this card paid down).
/// </summary>
[Trait("Color", "R")]
public class SunsetStrikemasterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature OnBattlefield(Creature c, Player owner)
    {
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature Flyer(string name, Player owner)
    {
        var c = new Creature(name, "{1}{U}", 1, 1);
        OnBattlefield(c, owner);
        // CR 702.9 — grant Flying as a keyword marker; CombatAbilities.HasFlying
        // (the predicate the creature_with_flying filter consults) reads it.
        c.AddAbility(new KeywordAbility("Flying", c, owner));
        return c;
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SunsetStrikemaster_Identity_HumanMonk_3_1_At1R()
    {
        var c = SunsetStrikemasterFactory.Create(_alice);

        c.Name.Should().Be("Sunset Strikemaster");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── {T}: Add {R} ─────────────────────────────────────────────────────

    [Fact]
    public void SunsetStrikemaster_TapForRed_TapsCreatureAndProducesOneRed()
    {
        var c = SunsetStrikemasterFactory.Create(_alice);
        // CR 302.6 — clear summoning sickness so the test exercises mana
        // production rather than the sickness gate.
        c.ClearSummoningSickness();

        var mana = c.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue("untapped Strikemaster — mana gate open");

        var produced = mana.Activate();
        produced.Red.Should().Be(1);
        produced.Generic.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue("{T} cost tapped the Strikemaster");
    }

    // ── Sac-damage activated ability — shape ─────────────────────────────

    [Fact]
    public void SacDamageAbility_HasManaTapSacrificeCosts_AndOneFlyingTarget()
    {
        var c = SunsetStrikemasterFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(x => x.CostType == AdditionalCostType.Tap,
                "the ability costs {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(x => x.CostType == AdditionalCostType.Sacrifice,
                "the ability sacrifices this creature (CR 701.16)");
        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle("the ability costs {2}{R}");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("flying",
            "the target is restricted to a creature with flying (CR 702.9)");
    }

    // ── Sac-damage activated ability — resolution ────────────────────────

    [Fact]
    public void Activate_Deals6ToFlyingCreature_AndSacrificesStrikemaster()
    {
        var c = SunsetStrikemasterFactory.Create(_alice);
        OnBattlefield(c, _alice);
        // CR 302.6 — under control since before this turn, so the {T} tap cost
        // is legal (the ability also taps as part of its cost).
        c.ClearSummoningSickness();

        var flyer = Flyer("Storm Crow", _bob);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        // CR 601.2f-h — pay the non-mana activation costs (tap + sacrifice) up
        // front, exactly as activation does; the {2}{R} mana cost is paid from
        // the mana pool by the live activator and is not exercised here.
        foreach (var cost in ability.Costs.OfType<AdditionalCost>())
        {
            cost.Pay(_alice);
        }

        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { flyer },
        });

        ability.Resolve();

        flyer.Damage.Should().Be(6, "6 marked damage on the flying target");
        _alice.Zones.Graveyard.GetCards().Should().Contain(c,
            "Sacrifice this creature (CR 701.16) moved the Strikemaster to its graveyard");
        c.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ── creature_with_flying filter (CR 702.9) — the engine gap paid down ─

    [Fact]
    public void FlyingFilter_AcceptsFlyer_RejectsNonFlyer()
    {
        var flyer = Flyer("Storm Crow", _bob);
        var grounded = OnBattlefield(new Creature("Hill Giant", "{3}{R}", 3, 3), _bob);

        TargetFilters.Matches("creature_with_flying", flyer)
            .Should().BeTrue("Storm Crow has Flying (CR 702.9)");
        TargetFilters.Matches("creature_with_flying", grounded)
            .Should().BeFalse("Hill Giant has no Flying — illegal target (CR 608.2b)");
    }
}
