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
/// Unit tests for <see cref="SwelteringSunsFactory"/>.
///
/// Card: Sweltering Suns — Sorcery {1}{R}{R} (Amonkhet).
///   "Sweltering Suns deals 3 damage to each creature.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// Covers (mirrors <see cref="AngerOfTheGodsFactory"/> for the sweep half
/// + <see cref="OnslaughtCyclingLandFactory"/> for the cycling half):
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve dishes 3 damage to every creature on both battlefields.
///   - Cycling activated ability shape: ManaCostCost({3}) + DiscardSelfCost
///     via the shared CyclingFactory primitive, plus the Cycling keyword
///     marker.
///   - End-to-end cycle: pays {3}, discards self, draws one card,
///     publishes CardCycledEvent when a bus is supplied.
/// </summary>
public class SwelteringSunsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SwelteringSuns_Identity()
    {
        var c = SwelteringSunsFactory.Create(_alice);

        c.Name.Should().Be("Sweltering Suns");
        c.ManaCost.Should().Be("{1}{R}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SwelteringSuns()
    {
        var card = NamedCardFactory.Create("Sweltering Suns", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sweltering Suns");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep (CR 109.5 — "each creature")
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsThreeDamage_ToEveryCreature_AcrossBothPlayers()
    {
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceGiant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var bobBig = NewCreatureOnBattlefield(_bob, "Wall of Doubt", "{2}{U}", 0, 5);

        var effects = SwelteringSunsFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        aliceBear.Damage.Should().Be(3);
        aliceGiant.Damage.Should().Be(3);
        bobBig.Damage.Should().Be(3, "opponent creatures are also damaged");

        aliceBear.IsDead().Should().BeTrue("3 damage on a 2/2 is lethal");
        aliceGiant.IsDead().Should().BeTrue("3 damage on a 3/3 is lethal");
        bobBig.IsDead().Should().BeFalse("3 damage on a 0/5 is survivable");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void SwelteringSuns_HasCyclingKeywordMarker()
    {
        var card = SwelteringSunsFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void SwelteringSuns_HasCyclingActivatedAbility_WithGenericThreeAndDiscardSelfCosts()
    {
        var card = SwelteringSunsFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "Cycling {3} charges 3 generic mana");
        manaCost.Red.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {3}, discards self, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void SwelteringSuns_Cycling_EndToEnd_PaysThreeDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Goblin Guide", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var suns = SwelteringSunsFactory.Create(_alice, bus);
        _alice.Zones.Hand.AddCard(suns);
        suns.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        var cycling = suns.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        suns.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(suns);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
