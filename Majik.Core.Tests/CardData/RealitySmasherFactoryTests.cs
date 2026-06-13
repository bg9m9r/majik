using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RealitySmasherFactory"/>
/// (Oath of the Gatewatch, {4}{C}).
///
/// Creature — Eldrazi 5/5. Oracle text:
///   "Trample, haste
///    Whenever this creature becomes the target of a spell an opponent
///    controls, counter that spell unless its controller discards a card."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {4}{C}, 5/5, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample + Haste + Ward keyword markers attached.
///   - <see cref="RealitySmasherFactory.BuildWardEffect"/> exposes a
///     bound <see cref="Majik.Core.Keywords.WardEffect"/> with mana-zero
///     cost (non-mana discard rider).
///   - The Ward—Discard trigger is a real <see cref="TriggeredAbility"/>:
///     it fires off <see cref="TargetsChosenEvent"/> when an OPPONENT's
///     spell targets Reality Smasher, and on resolution counters the spell
///     unless its controller discards a card.
/// </summary>
public class RealitySmasherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RealitySmasher_Identity()
    {
        var smasher = RealitySmasherFactory.Create(_alice);

        smasher.Name.Should().Be("Reality Smasher");
        smasher.ManaCost.Should().Be("{4}{C}");
        smasher.HasType(CardType.Creature).Should().BeTrue();
        smasher.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        smasher.BasePower.Should().Be(5);
        smasher.BaseToughness.Should().Be(5);
        smasher.Owner.Should().BeSameAs(_alice);
        smasher.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RealitySmasher_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Reality Smasher", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Reality Smasher");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(5);
    }

    [Fact]
    public void RealitySmasher_HasTrampleHasteAndWardMarkers()
    {
        var smasher = RealitySmasherFactory.Create(_alice);
        var keywords = smasher.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Trample",
            "CR 702.19 — Trample marker");
        keywords.Should().Contain(k => k.Keyword == "Haste",
            "CR 702.10 — Haste marker");
        keywords.Should().Contain(k => k.Keyword == "Ward",
            "CR 702.21 — Ward marker (pairs with the real ward trigger)");
    }

    [Fact]
    public void RealitySmasher_BuildWardEffect_ExposesManaZeroCost()
    {
        // Printed Ward cost is non-mana ("discard a card") — the helper's
        // mana portion is zero; the real payment is the DiscardACardCost the
        // WardEffect charges on Resolve.
        var smasher = RealitySmasherFactory.Create(_alice);
        var ward = RealitySmasherFactory.BuildWardEffect(smasher);

        ward.Source.Should().BeSameAs(smasher);
        ward.Cost.TotalValue.Should().Be(0,
            "printed cost is non-mana — mana portion is zero");
        RealitySmasherFactory.WardDiscardCost.Should().Be("Discard a card");
    }

    [Fact]
    public void RealitySmasher_Ward_OpponentTargets_DiscardsOrSpellCountered()
    {
        // CR 702.21c — Ward—Discard a card. The bound WardEffect charges a
        // real DiscardACardCost on Resolve against an opponent.
        var bob = new Player("Bob", 20);
        var smasher = RealitySmasherFactory.Create(_alice);
        smasher.SetController(_alice);
        var ward = RealitySmasherFactory.BuildWardEffect(smasher);

        // Opponent with a card discards it → not countered.
        var spare = new Creature("Spare", "{1}", 1, 1) { Owner = bob, Controller = bob };
        bob.Zones.Hand.AddCard(spare);

        var countered = ward.Resolve(bob);
        countered.Should().BeFalse("Bob discards a card to satisfy the ward");
        bob.Zones.Graveyard.GetCards().Should().Contain(spare);

        // Opponent with empty hand cannot pay → countered.
        var countered2 = ward.Resolve(bob);
        countered2.Should().BeTrue("Bob's hand is now empty — the ward counters his spell");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Ward—Discard a card as a real ITriggeredAbility (CR 702.21e).
    // ──────────────────────────────────────────────────────────────────────

    private static Creature NewCreature(Player controller, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void RealitySmasher_NamedDispatch_CarriesWardTrigger()
    {
        // The prod build path (NamedCardFactory) must produce a card with a
        // real ITriggeredAbility — this is what the Class B trigger-wiring
        // audit asserts. Before this fix the ward was a structural-only Ward
        // keyword marker with no resident triggered ability.
        var card = NamedCardFactory.Create("Reality Smasher", _alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Ward—Discard a card is a triggered ability (CR 702.21e)");
    }

    [Fact]
    public void RealitySmasher_OpponentSpellTargetsIt_Triggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var smasher = RealitySmasherFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(smasher);
        smasher.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        // Bob (opponent) casts Lightning Bolt targeting Reality Smasher.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(smasher) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting Reality Smasher triggers the ward");
    }

    [Fact]
    public void RealitySmasher_OpponentSpellTargetsSomethingElse_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var smasher = RealitySmasherFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(smasher);
        smasher.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        var bear = NewCreature(_alice);

        // Bob targets a DIFFERENT permanent Alice controls — Reality Smasher's
        // ward only triggers when Reality Smasher ITSELF becomes the target.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the ward fires only when Reality Smasher itself is targeted");
    }

    [Fact]
    public void RealitySmasher_YourOwnSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var smasher = RealitySmasherFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(smasher);
        smasher.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        // Alice targets her own Reality Smasher — "an opponent controls" gate.
        var spellCard = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(spellCard, _alice, new[] { Target.Permanent(smasher) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the ward only fires for a spell an OPPONENT controls (CR 702.21e)");
    }

    [Fact]
    public void RealitySmasher_ControllerDiscards_SpellNotCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var smasher = RealitySmasherFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(smasher);
        smasher.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(smasher) });
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        stack.Push(spell);

        // Bob has a card to discard → he pays the ward → spell NOT countered.
        var spare = new Creature("Spare", "{1}", 1, 1) { Owner = _bob, Controller = _bob };
        _bob.Zones.Hand.AddCard(spare);

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        var trigger = smasher.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stack.GetAll().Should().Contain(spell, "Bob discarded a card to satisfy the ward");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Stack);
        _bob.Zones.Graveyard.GetCards().Should().Contain(spare, "the ward discard cost was paid");
    }

    [Fact]
    public void RealitySmasher_ControllerCannotDiscard_SpellCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var smasher = RealitySmasherFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(smasher);
        smasher.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(smasher) });
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        stack.Push(spell);

        // Bob's hand is empty → he cannot discard → spell countered (CR 701.5b).
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        var trigger = smasher.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stack.GetAll().Should().NotContain(spell, "Bob can't discard, so his spell is countered");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard,
            "a countered spell goes to its owner's graveyard (CR 701.5b)");
    }

    private readonly Player _bob = new("Bob", 20);
}
