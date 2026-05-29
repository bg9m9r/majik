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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GrimLavamancerFactory"/>.
///
/// Grim Lavamancer (Torment, {R}):
///   Creature — Human Wizard 1/1.
///   "{R}, {T}, Exile two cards from your graveyard: This creature deals
///    2 damage to any target."
///
/// Covers:
///   - Identity (Human Wizard 1/1, {R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Activated ability shape: {R} mana cost + tap cost + one any-target
///     request (CR 602).
///   - Resolution: 2 damage to player / creature / planeswalker target
///     (CR 306.7 loyalty route); exactly two graveyard cards exiled
///     (CR 601-style exile cost paid in the resolve closure).
///   - Guard: with fewer than two graveyard cards, the body is a no-op
///     (the cost can't be paid).
/// </summary>
public class GrimLavamancerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Fuel(Player owner, string name)
    {
        var c = new Creature(name, "{1}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GrimLavamancer_Identity()
    {
        var gl = GrimLavamancerFactory.Create(_alice);

        gl.Name.Should().Be("Grim Lavamancer");
        gl.ManaCost.Should().Be("{R}");
        gl.HasType(CardType.Creature).Should().BeTrue();
        gl.HasSubtype(CardSubtype.Human).Should().BeTrue();
        gl.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        gl.BasePower.Should().Be(1);
        gl.BaseToughness.Should().Be(1);
        gl.Owner.Should().BeSameAs(_alice);
        gl.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GrimLavamancer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Grim Lavamancer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Grim Lavamancer");
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GrimLavamancer_ActivatedAbility_HasManaAndTap_OneAnyTarget()
    {
        var gl = GrimLavamancerFactory.Create(_alice);

        var ability = gl.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the ping ability has a {R} mana cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the ability has a {T} cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Sacrifice,
                "Grim Lavamancer does not sacrifice itself.");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Ping_DealsTwoToPlayer_AndExilesTwoGraveyardCards()
    {
        var gl = GrimLavamancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gl);
        gl.SetZone(ZoneType.Battlefield);

        var f1 = Fuel(_alice, "Fuel A");
        var f2 = Fuel(_alice, "Fuel B");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);

        var ability = gl.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(18, "2 damage to Bob");

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty("both fuel cards exiled");
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { f1, f2 });
        f1.Zone.Should().Be(ZoneType.Exile);
        f2.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Activate_Ping_DealsTwoToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var gl = GrimLavamancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gl);
        gl.SetZone(ZoneType.Battlefield);
        Fuel(_alice, "Fuel A");
        Fuel(_alice, "Fuel B");

        var ability = gl.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.Damage.Should().Be(2, "2 marked damage on the bears");
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

        var gl = GrimLavamancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gl);
        gl.SetZone(ZoneType.Battlefield);
        Fuel(_alice, "Fuel A");
        Fuel(_alice, "Fuel B");

        var ability = gl.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        ability.Resolve();

        pw.Loyalty.Should().Be(2, "2 loyalty counters removed (4 - 2)");
    }

    [Fact]
    public void Activate_Ping_NoOp_WhenFewerThanTwoGraveyardCards()
    {
        // The exile-two cost can't be paid with only one card in the
        // graveyard — the resolve body is a no-op (no damage, no exile).
        var gl = GrimLavamancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gl);
        gl.SetZone(ZoneType.Battlefield);
        Fuel(_alice, "Lonely Fuel");

        var ability = gl.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(20, "no damage when the cost can't be paid");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1, "the lone card is not exiled");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
