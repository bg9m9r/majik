using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Baral, Chief of Compliance (Aether Revolt, {1}{U}).
///
/// Covers:
///   - Card identity (name, Legendary supertype, Human + Wizard subtypes,
///     1/3, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - One <see cref="SpellCostReductionAbility"/> rider attached.
///   - Spell-cost reduction (CR 117.7):
///       * Instant cast — generic reduced by 1.
///       * Sorcery cast — generic reduced by 1.
///       * Creature cast — no reduction.
///       * Off-battlefield Baral — no reduction.
///       * Stacks with another reducer (a Goblin Electromancer).
///       * Floor-at-zero on a {R} instant; coloured pips untouched.
///       * Opponent-controlled Baral does NOT discount your spells.
///
/// The counter → loot trigger ("whenever a spell or ability you control
/// counters a spell, draw a card, then discard a card") IS implemented — see
/// the factory's class-level remarks and the Counter → loot tests below.
/// </summary>
public class BaralChiefOfComplianceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void Baral_Identity_LegendaryHumanWizard_1_3_AtCost1U()
    {
        var b = BaralChiefOfComplianceFactory.Create(_alice);

        b.Name.Should().Be("Baral, Chief of Compliance");
        b.ManaCost.Should().Be("{1}{U}");
        b.HasType(CardType.Creature).Should().BeTrue();
        b.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Baral is Legendary");
        b.HasSubtype(CardSubtype.Human).Should().BeTrue();
        b.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        b.BasePower.Should().Be(1);
        b.BaseToughness.Should().Be(3);
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);

        b.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the instant/sorcery cost-reduction rider is attached");
    }

    [Fact]
    public void Baral_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Baral, Chief of Compliance", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Baral, Chief of Compliance");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void InstantCast_GenericReducedByOne()
    {
        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);

        var cancel = new Instant("Cancel", "{1}{U}{U}");
        cancel.SetOwner(_alice);
        cancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(cancel, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Blue.Should().Be(2, "coloured pips untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void SorceryCast_GenericReducedByOne()
    {
        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);

        var mindRot = new Sorcery("Mind Rot", "{2}{B}");
        mindRot.SetOwner(_alice);
        mindRot.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(mindRot, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Black.Should().Be(1, "coloured pips untouched");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void CreatureCast_NoReduction()
    {
        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);

        var creature = new Creature("Test Beast", "{2}{G}", power: 3, toughness: 3);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(2, "creature spell — no Baral discount");
        effective.Green.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(baral);
        baral.SetZone(ZoneType.Hand);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(2, "Baral isn't on the battlefield — no discount");
        effective.Black.Should().Be(1);
    }

    [Fact]
    public void BaralPlusElectromancer_ReductionStacks()
    {
        // Baral + Goblin Electromancer = two reducers, each contributing
        // {1} → {2} total reduction. CR 117.7 stacks additively per
        // CostReduction.GetEffectiveCost.
        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        var mancer = GoblinElectromancerFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);
        PutOnBattlefield(_alice, mancer);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(0, "{2} generic reduced by 2 → {0}");
        effective.Black.Should().Be(1, "coloured pip still required");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void RedInstant_FloorsAtZero_ColouredPipUntouched()
    {
        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(bolt, _alice);

        effective.Generic.Should().Be(0, "no generic to reduce — floor-at-zero (CR 117.7c)");
        effective.Red.Should().Be(1, "coloured pip untouched");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsBaral_DoesNotDiscountYourSpells()
    {
        // Bob controls Baral; Alice casts an instant. The rider is scoped
        // to the controller's battlefield, so Alice gets no discount.
        var bobBaral = BaralChiefOfComplianceFactory.Create(_bob);
        PutOnBattlefield(_bob, bobBaral);

        var aliceCancel = new Instant("Cancel", "{1}{U}{U}");
        aliceCancel.SetOwner(_alice);
        aliceCancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceCancel, _alice);

        effective.Generic.Should().Be(1,
            "Bob's Baral doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
        effective.Blue.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // Counter → loot trigger (CR 603.1):
    //   "Whenever a spell or ability you control counters a spell, you may
    //    draw a card. If you do, discard a card."
    // ------------------------------------------------------------------

    [Fact]
    public void Baral_HasCounterLootTrigger()
    {
        var b = BaralChiefOfComplianceFactory.Create(_alice);

        b.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the counter → loot trigger is attached");
    }

    [Fact]
    public async Task Baral_WhenYourSpellCountersAnOpponentSpell_LootsOne()
    {
        // Full counter flow: Alice's Counterspell (resolving via the
        // StackResolver) counters Bob's spell. Alice's Baral sees its
        // controller's counter and loots 1 (draw a card, then discard one).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new Majik.Core.Services.StackResolver(bus);

        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);
        triggers.BindCard(baral);

        // Give Alice a library card to draw + a hand card to discard so the
        // loot can resolve fully.
        var libraryCard = new Instant("Opt", "{U}");
        libraryCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        var handCard = new Sorcery("Divination", "{2}{U}");
        handCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        // Bob's spell on the stack (the target to be countered).
        var bobBolt = new Instant("Lightning Bolt", "{R}");
        bobBolt.SetOwner(_bob);
        bobBolt.SetController(_bob);
        bobBolt.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        // Alice's Counterspell on top of the stack — resolving it counters
        // Bob's spell. We model the Counterspell as a Spell whose resolution
        // calls Fx.Counter; here we use the canonical CounterTargetSpell from
        // the named Counterspell factory shape by resolving an effect.
        var counterCard = new Instant("Counterspell", "{U}{U}");
        counterCard.SetOwner(_alice);
        counterCard.SetController(_alice);
        counterCard.SetZone(ZoneType.Stack);
        var counterSpell = new Majik.Core.Spells.Spell(
            counterCard, _alice,
            effects: new IEffect[]
            {
                new Effect("counter target spell", () => Fx.Counter(stack, bobSpell)),
            });
        stack.Push(counterSpell);

        // Resolve the top object (Alice's Counterspell). During its resolution
        // it counters Bob's spell, which fires Baral's trigger onto the stack.
        await resolver.ResolveTopAsync(stack, agentLookup: _ => null);
        triggers.PutPendingTriggersOnStack(_alice);
        await resolver.ResolveAllAsync(stack, agentLookup: _ => null);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard, "Bob's spell was countered");
        // Loot resolved: Alice drew Opt (now in hand or discarded) and
        // discarded one card. Net hand size is unchanged (1 in, 1 out), and
        // exactly one card sits in her graveyard.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1,
            "the loot discarded exactly one card");
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "the loot drew the only library card");
    }

    [Fact]
    public void Baral_WhenOpponentCountersASpell_DoesNotLoot()
    {
        // Bob's counter (countering controller = Bob) must NOT trigger
        // Alice's Baral. Drive the event directly to isolate the predicate.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);
        triggers.BindCard(baral);

        var some = new Instant("Some Spell", "{U}");
        some.SetOwner(_alice);
        some.SetController(_alice);
        var counteredSpell = new Majik.Core.Spells.Spell(some, _alice);

        // A counter controlled by BOB.
        bus.Publish(new SpellCounteredEvent(counteredSpell, _bob));

        triggers.PendingCount.Should().Be(0,
            "an opponent's counter does not trigger Alice's Baral");
    }

    [Fact]
    public void Baral_WhenYourCounter_TriggerGoesPending()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var baral = BaralChiefOfComplianceFactory.Create(_alice);
        PutOnBattlefield(_alice, baral);
        triggers.BindCard(baral);

        var some = new Instant("Some Spell", "{R}");
        some.SetOwner(_bob);
        some.SetController(_bob);
        var counteredSpell = new Majik.Core.Spells.Spell(some, _bob);

        bus.Publish(new SpellCounteredEvent(counteredSpell, _alice));

        triggers.PendingCount.Should().Be(1,
            "a counter Alice controls triggers her Baral");
    }
}
