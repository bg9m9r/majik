using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Archmage of Runes (Modern Horizons 3, {3}{U}{U}, Creature —
/// Giant Wizard 3/6).
///
/// Oracle text:
///   "Instant and sorcery spells you cast cost {1} less to cast.
///    Whenever you cast an instant or sorcery spell, draw a card."
///
/// Covers ONLY the card's unique behaviour:
///   - Identity (name, {3}{U}{U}, blue, Giant + Wizard, 3/6) — single assert.
///   - Spell-cost reduction rider (CR 117.7): instant reduced, sorcery reduced,
///     creature untouched, off-battlefield inert.
///   - Instant/sorcery-cast draw trigger (CR 603.1): instant fires, sorcery
///     fires + resolves to a draw, creature does not fire, opponent does not
///     fire.
///
/// Dispatch + well-formedness is covered for every implemented card by
/// CardFactoryContractTests; not re-asserted here.
/// </summary>
[Trait("Color", "U")]
public class ArchmageOfRunesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static void SeedLibrary(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Instant(n, "1") { Owner = p };
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller)
    {
        var instant = new Instant("Opt", "{U}") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller)
    {
        var sorcery = new Sorcery("Divination", "{2}{U}") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller)
    {
        var creature = new Creature("Bear", "{1}{G}", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void ArchmageOfRunes_Identity()
    {
        var c = ArchmageOfRunesFactory.Create(_alice);

        c.Name.Should().Be("Archmage of Runes");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue("Giant is a printed subtype");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Wizard is a printed subtype");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the instant/sorcery cost-reduction rider is attached");
        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the instant/sorcery cast-draw trigger is attached");
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void InstantCast_GenericReducedByOne()
    {
        var archmage = ArchmageOfRunesFactory.Create(_alice);
        PutOnBattlefield(_alice, archmage);

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
        var archmage = ArchmageOfRunesFactory.Create(_alice);
        PutOnBattlefield(_alice, archmage);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Black.Should().Be(1, "coloured pips untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void CreatureCast_NoReduction()
    {
        var archmage = ArchmageOfRunesFactory.Create(_alice);
        PutOnBattlefield(_alice, archmage);

        var creature = new Creature("Test Beast", "{2}{G}", power: 3, toughness: 3);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(2, "creature spell — no Archmage discount");
        effective.Green.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var archmage = ArchmageOfRunesFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(archmage);
        archmage.SetZone(ZoneType.Hand);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(2, "Archmage isn't on the battlefield — no discount");
        effective.Black.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // Instant/sorcery-cast draw trigger (CR 603.1)
    // -------------------------------------------------------------------------

    [Fact]
    public void CastInstant_DrawsOne()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var archmage = ArchmageOfRunesFactory.Create(_alice, bus, triggers);
        PutOnBattlefield(_alice, archmage);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "an instant spell fires Archmage's cast trigger exactly once (CR 603.1)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1", "drew the top card of the library");
    }

    [Fact]
    public void CastSorcery_TriggerGoesPending()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var archmage = ArchmageOfRunesFactory.Create(_alice, bus, triggers);
        PutOnBattlefield(_alice, archmage);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "a sorcery spell fires Archmage's cast trigger (CR 603.1)");
    }

    [Fact]
    public void CastCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var archmage = ArchmageOfRunesFactory.Create(_alice, bus, triggers);
        PutOnBattlefield(_alice, archmage);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "a creature spell is neither instant nor sorcery — no draw");
    }

    [Fact]
    public void OpponentCastsInstant_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var archmage = ArchmageOfRunesFactory.Create(_alice, bus, triggers);
        PutOnBattlefield(_alice, archmage);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "Bob's instant does not fire Alice's Archmage — 'you cast' (CR 603.1)");
    }
}
