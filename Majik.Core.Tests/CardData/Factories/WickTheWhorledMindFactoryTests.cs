using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WickTheWhorledMindFactory"/> (Duskmourn: House of
/// Horror, {3}{B}).
///
/// Legendary Creature — Rat Warlock 2/4. Oracle text (current Scryfall):
///   "Whenever Wick or another Rat you control enters, create a 1/1 black
///    Snail creature token if you don't control a Snail. Otherwise, put a
///    +1/+1 counter on a Snail you control.
///    {U}{B}{R}, Sacrifice a Snail: Wick deals damage equal to the
///    sacrificed creature's power to each opponent. Then draw cards equal to
///    the sacrificed creature's power."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (cost / P-T / Legendary / Rat Warlock subtypes).
/// - Subtype-gated ETB-of-self-or-another-Rat trigger (CR 603.6e —
///   includeSelf + subtype "Rat" + youControlOnly).
/// - ETB effect branch A (CR 111 token creation): no Snail controlled →
///   create a 1/1 black Snail token.
/// - ETB effect branch B (CR 122.1c counter): a Snail already controlled →
///   put a +1/+1 counter on a controlled Snail (no extra token).
/// - Activated ability ({U}{B}{R}, Sacrifice a Snail): the sacrificed
///   creature's power becomes the damage dealt to each opponent AND the
///   number of cards drawn (CR 117 / 601.16 / 119 / 120).
/// </summary>
[Trait("Color", "B")]
public class WickTheWhorledMindFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Creature Snail(Player owner, int power = 1, int toughness = 1)
    {
        var s = new Creature("Snail Buddy", "{1}{U}", power, toughness,
            subtypes: new[] { CardSubtype.Snail });
        PutOnBattlefield(owner, s);
        return s;
    }

    // =========================================================================
    // Identity
    // =========================================================================

    [Fact]
    public void Wick_Identity()
    {
        var c = WickTheWhorledMindFactory.Create(_alice);

        c.Name.Should().Be("Wick, the Whorled Mind");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Wick is a Legendary Creature");
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // =========================================================================
    // ETB trigger condition (CR 603.6e)
    // =========================================================================

    [Fact]
    public void Wick_SelfEnters_TriggerMatches()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        wick.SetController(_alice);

        var trigger = wick.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(wick, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(ev, trigger).Should().BeTrue(
            "'Wick or another Rat you control' includes Wick's own entry (includeSelf)");
    }

    [Fact]
    public void Wick_AnotherRatYouControlEnters_TriggerMatches()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        wick.SetZone(ZoneType.Battlefield);
        wick.SetController(_alice);

        var rat = new Creature("Pack Rat", "{1}{B}", 1, 1,
            subtypes: new[] { CardSubtype.Rat });
        rat.SetOwner(_alice);
        rat.SetController(_alice);

        var trigger = wick.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(rat, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(ev, trigger).Should().BeTrue(
            "another Rat you control entering fires the subtype-gated trigger");
    }

    [Fact]
    public void Wick_NonRatEnters_DoesNotTrigger()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        wick.SetZone(ZoneType.Battlefield);
        wick.SetController(_alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = wick.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "a non-Rat creature entering does not fire the trigger");
    }

    [Fact]
    public void Wick_OpponentRatEnters_DoesNotTrigger()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        wick.SetZone(ZoneType.Battlefield);
        wick.SetController(_alice);

        var oppRat = new Creature("Bob's Rat", "{1}{B}", 1, 1,
            subtypes: new[] { CardSubtype.Rat });
        oppRat.SetOwner(_bob);
        oppRat.SetController(_bob);

        var trigger = wick.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(oppRat, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "'a Rat YOU control' excludes an opponent's Rat (CR 109.5)");
    }

    // =========================================================================
    // ETB effect — branch A: no Snail controlled → create a Snail token
    // =========================================================================

    [Fact]
    public void Wick_Etb_NoSnailControlled_CreatesOneBlackSnailToken()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        PutOnBattlefield(_alice, wick);

        var trigger = wick.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Resolve();

        var snails = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Snail))
            .ToList();

        snails.Should().HaveCount(1, "no Snail controlled → create exactly one Snail token");
        var token = snails[0];
        token.IsToken.Should().BeTrue("the created Snail is a token (CR 111)");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasType(CardType.Creature).Should().BeTrue();
    }

    // =========================================================================
    // ETB effect — branch B: a Snail controlled → +1/+1 counter, no new token
    // =========================================================================

    [Fact]
    public void Wick_Etb_SnailControlled_PutsCounterOnSnail_NoNewToken()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        PutOnBattlefield(_alice, wick);

        var existingSnail = Snail(_alice);

        var trigger = wick.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Resolve();

        var snails = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Snail))
            .ToList();

        snails.Should().HaveCount(1, "a Snail is already controlled → no new token is created");
        existingSnail.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Otherwise, put a +1/+1 counter on a Snail you control (CR 122.1c)");
    }

    // =========================================================================
    // Activated ability — {U}{B}{R}, Sacrifice a Snail:
    //   damage to each opponent = sacrificed power; draw = sacrificed power.
    // =========================================================================

    [Fact]
    public void Wick_ActivatedAbility_HasManaAndSacrificeSnailCost()
    {
        var wick = WickTheWhorledMindFactory.Create(_alice);
        PutOnBattlefield(_alice, wick);

        var ability = wick.Abilities.OfType<ActivatedAbility>().Single();

        var manaCost = ability.Costs.OfType<ManaCostCost>().Should().ContainSingle().Subject;
        manaCost.Cost.Blue.Should().Be(1, "the activation cost includes {U}");
        manaCost.Cost.Black.Should().Be(1, "the activation cost includes {B}");
        manaCost.Cost.Red.Should().Be(1, "the activation cost includes {R}");
        manaCost.Cost.TotalValue.Should().Be(3, "{U}{B}{R} has mana value 3");
        ability.Costs.OfType<SacrificeFilteredCost>().Should().ContainSingle(
            "the cost is 'Sacrifice a Snail'");
    }

    [Fact]
    public async System.Threading.Tasks.Task Wick_ActivatedAbility_DamageEachOpponentAndDrawEqualSacrificedPower()
    {
        var game = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob, _carol },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack());

        var wick = WickTheWhorledMindFactory.Create(_alice);
        PutOnBattlefield(_alice, wick);

        // Give Alice some cards to draw.
        for (int i = 0; i < 5; i++)
        {
            var card = new Creature("Filler", "{1}", 1, 1);
            card.SetOwner(_alice);
            _alice.Zones.Library.AddCard(card);
        }
        int libraryBefore = _alice.Zones.Library.GetCards().Count();

        // A 3/3 Snail to sacrifice.
        var snail = Snail(_alice, power: 3, toughness: 3);

        var ability = wick.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs.OfType<SacrificeFilteredCost>().Single();

        // Pay the sacrifice cost so the sacrificed Snail (power 3) is recorded.
        sacCost.CanPay(_alice).Should().BeTrue("Alice controls a Snail to sacrifice");
        sacCost.Pay(_alice);

        // Resolve the ability against the multiplayer game context.
        await ability.ResolveAsync(agent: null, game: game);

        _bob.LifeTotal.Should().Be(17, "3 damage to each opponent (sacrificed Snail's power)");
        _carol.LifeTotal.Should().Be(17, "3 damage to each opponent (sacrificed Snail's power)");
        _alice.LifeTotal.Should().Be(20, "the controller is not an opponent (CR 102.1)");

        (libraryBefore - _alice.Zones.Library.GetCards().Count()).Should().Be(3,
            "draw cards equal to the sacrificed creature's power (3)");
    }
}
