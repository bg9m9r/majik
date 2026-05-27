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
/// Tests for <see cref="MoggFanaticFactory"/>.
///
/// Mogg Fanatic (Tempest, {R}):
///   Creature — Goblin 1/1.
///   Sacrifice Mogg Fanatic: It deals 1 damage to any target.
///
/// Covers:
///   - Identity (Goblin 1/1, {R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - No Haste keyword marker (distinct from Fanatical Firebrand).
///   - Activated ability shape: Sacrifice + one any-target request,
///     NO tap cost.
///   - Resolution: 1 damage to player / creature target; planeswalker
///     target routes through loyalty removal (CR 306.7); Fanatic
///     sacrificed.
/// </summary>
public class MoggFanaticTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggFanatic_Identity()
    {
        var mf = MoggFanaticFactory.Create(_alice);

        mf.Name.Should().Be("Mogg Fanatic");
        mf.ManaCost.Should().Be("{R}");
        mf.HasType(CardType.Creature).Should().BeTrue();
        mf.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        mf.HasSubtype(CardSubtype.Warrior).Should().BeFalse(
            "Mogg Fanatic is 'Creature — Goblin' only, no Warrior subtype.");
        mf.BasePower.Should().Be(1);
        mf.BaseToughness.Should().Be(1);
        mf.Owner.Should().BeSameAs(_alice);
        mf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MoggFanatic_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mogg Fanatic", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Mogg Fanatic");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void MoggFanatic_DoesNotHaveHasteKeyword()
    {
        var mf = MoggFanaticFactory.Create(_alice);

        mf.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().NotContain("Haste",
                "Mogg Fanatic's printed text has no Haste rider " +
                "(distinguishes from Fanatical Firebrand).");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggFanatic_ActivatedAbility_HasSacrifice_NoTap_OneAnyTarget()
    {
        var mf = MoggFanaticFactory.Create(_alice);

        var ability = mf.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the ping ability sacrifices Mogg Fanatic");
        ability.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "Mogg Fanatic's printed cost is sac-only — no {T}.");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Ping_DealsOneToPlayerTarget_AndSacrificesFanatic()
    {
        var mf = MoggFanaticFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mf);
        mf.SetZone(ZoneType.Battlefield);

        var ability = mf.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(19, "1 damage to Bob");
        _bob.LifeLostThisTurn.Should().Be(1);

        _alice.Zones.Graveyard.GetCards().Should().Contain(mf);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mf);
        mf.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Ping_DealsOneToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var mf = MoggFanaticFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mf);
        mf.SetZone(ZoneType.Battlefield);

        var ability = mf.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.Damage.Should().Be(1, "1 marked damage on the bears");
        _alice.Zones.Graveyard.GetCards().Should().Contain(mf);
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

        var mf = MoggFanaticFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mf);
        mf.SetZone(ZoneType.Battlefield);

        var ability = mf.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        ability.Resolve();

        pw.Loyalty.Should().Be(3, "1 loyalty counter removed (4 - 1)");
    }
}
