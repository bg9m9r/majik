using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="KessigMalcontentsFactory"/>.
///
/// Kessig Malcontents (Innistrad: Midnight Hunt, {2}{R}):
///   Creature — Human Warrior 3/1.
///   When this creature enters, it deals damage to target player or
///   planeswalker equal to the number of Humans you control.
///
/// Covers:
///   - Identity (Human Warrior 3/1, {2}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB triggered-ability shape: one 1..1 "target player or
///     planeswalker" request.
///   - Resolution: damage equals the controller's Human count (including
///     Kessig Malcontents itself — CR 205.3 / 608.2g); extra Humans raise
///     the amount; a planeswalker target routes through loyalty removal
///     (CR 306.8); a non-player / non-planeswalker target no-ops
///     (CR 608.2b).
/// </summary>
public class KessigMalcontentsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Human(Player owner, string name = "Some Human")
    {
        var c = new Creature(name, "{1}{W}", 1, 1, subtypes: new[] { CardSubtype.Human });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KessigMalcontents_Identity()
    {
        var km = KessigMalcontentsFactory.Create(_alice);

        km.Name.Should().Be("Kessig Malcontents");
        km.ManaCost.Should().Be("{2}{R}");
        km.HasType(CardType.Creature).Should().BeTrue();
        km.HasSubtype(CardSubtype.Human).Should().BeTrue();
        km.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        km.BasePower.Should().Be(3);
        km.BaseToughness.Should().Be(1);
        km.Owner.Should().BeSameAs(_alice);
        km.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KessigMalcontents_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Kessig Malcontents", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Kessig Malcontents");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB triggered-ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void KessigMalcontents_HasEtbTrigger_OnePlayerOrPlaneswalkerTarget()
    {
        var km = KessigMalcontentsFactory.Create(_alice);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
        trigger.TargetRequests[0].Description.Should()
            .Contain("player or planeswalker");
    }

    // -----------------------------------------------------------------------
    // Resolution — damage equals Human count
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_LoneMalcontents_DealsOne_CountsItself()
    {
        // CR 205.3 — Kessig Malcontents is itself a Human; by the time the
        // ETB ability resolves it is on the battlefield under its
        // controller, so it counts itself: a lone Malcontents deals 1.
        var km = KessigMalcontentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        trigger.Resolve();

        _bob.LifeTotal.Should().Be(19, "1 Human (Malcontents itself) -> 1 damage");
        _bob.LifeLostThisTurn.Should().Be(1);
    }

    [Fact]
    public void Etb_DamageScalesWithHumanCount()
    {
        // Two other Humans already on Alice's battlefield + Malcontents = 3.
        Human(_alice, "Champion");
        Human(_alice, "Soldier");

        var km = KessigMalcontentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        trigger.Resolve();

        _bob.LifeTotal.Should().Be(17, "3 Humans you control -> 3 damage");
        _bob.LifeLostThisTurn.Should().Be(3);
    }

    [Fact]
    public void Etb_OnlyCountsControllersHumans_NotOpponents()
    {
        // CR 109.5 — "you control" is the ability's controller. Bob's Humans
        // do not count toward Alice's Malcontents.
        Human(_bob, "Bob's Human A");
        Human(_bob, "Bob's Human B");

        var km = KessigMalcontentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        trigger.Resolve();

        _bob.LifeTotal.Should().Be(19, "only Alice's 1 Human (Malcontents) counts");
    }

    [Fact]
    public void Etb_DealsToPlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.8 — damage to a planeswalker removes that many loyalty counters.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 5,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        Human(_alice, "Extra Human"); // 2 Humans: this + Malcontents.

        var km = KessigMalcontentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        trigger.Resolve();

        pw.Loyalty.Should().Be(3, "2 Humans -> 2 loyalty counters removed (5 - 2)");
    }

    [Fact]
    public void Etb_NoChosenTarget_NoOps()
    {
        var km = KessigMalcontentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();

        // No targets set — resolution is a clean no-op (CR 608.2b).
        trigger.Resolve();

        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Etb_CreatureTarget_NoOps()
    {
        // CR 608.2b — a creature is not a legal "player or planeswalker"
        // target; if one is somehow resolved (redirect), the effect no-ops.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var km = KessigMalcontentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = km.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        trigger.Resolve();

        bears.Damage.Should().Be(0, "a creature is not a legal target — no damage");
    }
}
