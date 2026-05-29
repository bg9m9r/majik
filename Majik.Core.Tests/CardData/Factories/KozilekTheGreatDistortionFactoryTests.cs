using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KozilekTheGreatDistortionFactory"/>.
///
/// Kozilek, the Great Distortion (Oath of the Gatewatch, {8}{C}{C}).
/// Legendary Creature — Eldrazi 12/12. Oracle:
///   "When you cast this spell, if you have fewer than seven cards in hand,
///    draw cards equal to the difference.
///    Menace
///    Discard a card with mana value X: Counter target spell with mana
///    value X."
///
/// Coverage:
/// - Identity (name, types, supertype, subtype, cost, mv, colourless, P/T,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Menace keyword marker (CR 702.111).
/// - Structural cast trigger (CR 603.6a) — SpellCastEvent over Stack, gated
///   to this card.
/// - Cast trigger refill: draws up to seven; no draw when at/above seven;
///   empty-library halts + stamps the loss.
/// - Discard-X-counter-X activated ability: counters a matching-mv spell;
///   no-op on mv mismatch.
/// </summary>
public class KozilekTheGreatDistortionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card MakeCard(Player owner, string name, string cost)
    {
        var c = new Instant(name, cost);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void FillLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Instant($"Lib {i}", "{1}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void Kozilek_Identity()
    {
        var c = KozilekTheGreatDistortionFactory.Create(_alice);

        c.Name.Should().Be("Kozilek, the Great Distortion");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.ManaCost.Should().Be("{8}{C}{C}");
        c.ManaCostValue.TotalValue.Should().Be(10, "{8}{C}{C} — {C} counts as +1 generic (CR 107.4c).");
        c.BasePower.Should().Be(12);
        c.BaseToughness.Should().Be(12);
        CardColors.GetColors(c).Should().BeEmpty("Kozilek is colourless (no coloured mana symbols, CR 105.2c).");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kozilek_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kozilek, the Great Distortion", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kozilek, the Great Distortion");
        ((Creature)c).HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    // ── Menace ──────────────────────────────────────────────────────────

    [Fact]
    public void Kozilek_HasMenace()
    {
        var c = KozilekTheGreatDistortionFactory.Create(_alice);

        CombatAbilities.HasMenace(c).Should().BeTrue(
            "Kozilek prints Menace (CR 702.111).");
    }

    // ── Cast trigger — structural ───────────────────────────────────────

    [Fact]
    public void Card_HasStructuralCastTrigger()
    {
        var card = KozilekTheGreatDistortionFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Kozilek prints one triggered ability — the refill cast trigger.");

        var cast = triggers[0];
        cast.Source.Should().BeSameAs(card);
        cast.Controller.Should().BeSameAs(_alice);
        cast.ActiveZones.Should().Contain(ZoneType.Stack,
            "the cast trigger fires while Kozilek is on the stack (CR 603.6a).");
        cast.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void CastTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = KozilekTheGreatDistortionFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var selfSpell = new Spell(card, _alice);
        var other = MakeCard(_alice, "Other", "{R}");
        var otherSpell = new Spell(other, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ── Cast trigger — refill behaviour ─────────────────────────────────

    [Fact]
    public void CastTrigger_DrawsUpToSeven_WhenBelow()
    {
        FillLibrary(_alice, 20);
        // Alice has 2 cards in hand → should draw 5 to reach seven.
        for (var i = 0; i < 2; i++)
        {
            var h = MakeCard(_alice, $"Hand {i}", "{1}");
            _alice.Zones.Hand.AddCard(h);
        }

        var card = KozilekTheGreatDistortionFactory.Create(_alice);
        card.SetZone(ZoneType.Stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Spell(card, _alice);
        trigger.Condition.Matches(new SpellCastEvent(spell), trigger).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(7,
            "fewer-than-seven refills the hand to seven (draw equal to the difference).");
    }

    [Fact]
    public void CastTrigger_DrawsNothing_WhenAtOrAboveSeven()
    {
        FillLibrary(_alice, 20);
        for (var i = 0; i < 8; i++)
        {
            var h = MakeCard(_alice, $"Hand {i}", "{1}");
            _alice.Zones.Hand.AddCard(h);
        }

        var card = KozilekTheGreatDistortionFactory.Create(_alice);
        card.SetZone(ZoneType.Stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Spell(card, _alice);
        trigger.Condition.Matches(new SpellCastEvent(spell), trigger).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(8,
            "intervening-if 'fewer than seven' fails — no cards drawn (CR 603.4).");
    }

    [Fact]
    public void CastTrigger_EmptyLibrary_StampsLossCondition()
    {
        // No library; empty hand → wants to draw seven but can't.
        var card = KozilekTheGreatDistortionFactory.Create(_alice);
        card.SetZone(ZoneType.Stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Spell(card, _alice);
        trigger.Condition.Matches(new SpellCastEvent(spell), trigger).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the CR 704.5b / 120.3 loss condition.");
    }

    // ── Discard-X-counter-X activated ability ───────────────────────────

    [Fact]
    public void Card_HasDiscardCounterActivatedAbility()
    {
        var card = KozilekTheGreatDistortionFactory.Create(_alice);

        var activated = card.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "Kozilek prints one activated ability — discard-X-counter-X.");

        var ab = activated[0];
        ab.Costs.OfType<DiscardACardCost>().Should().ContainSingle(
            "the discard is the sole activation cost (no mana).");
        ab.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("target spell");
    }

    [Fact]
    public void CounterAbility_CountersTargetSpell_WhenManaValueMatchesX()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Alice discards a mv-3 card → X = 3.
        var discarded = MakeCard(_alice, "Discarded", "{1}{R}{R}"); // mv 3
        _alice.Zones.Hand.AddCard(discarded);

        // Bob's mv-3 spell is on the stack.
        var targetCard = MakeCard(_bob, "Counterspell Target", "{2}{U}"); // mv 3
        var targetSpell = new Spell(targetCard, _bob);
        stack.Push(targetSpell);

        var card = KozilekTheGreatDistortionFactory.Create(_alice, triggers: null, stack);
        card.SetZone(ZoneType.Battlefield);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        // Pay the cost (nominate the mv-3 card) and choose the target spell.
        var cost = ability.Costs.OfType<DiscardACardCost>().Single();
        cost.Target = discarded;
        cost.Pay(_alice);
        ability.SetChosenTargets(new[] { new object[] { targetSpell } });

        foreach (var e in ability.Effects) e.Execute();

        stack.GetAll().Should().NotContain(targetSpell,
            "X = 3 (discarded card mv) matches the target spell's mv 3 → countered (CR 701.5).");
    }

    [Fact]
    public void CounterAbility_NoOp_WhenManaValueMismatch()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Alice discards a mv-1 card → X = 1.
        var discarded = MakeCard(_alice, "Discarded", "{R}"); // mv 1
        _alice.Zones.Hand.AddCard(discarded);

        // Bob's mv-3 spell on the stack — does NOT match X = 1.
        var targetCard = MakeCard(_bob, "Big Spell", "{2}{U}"); // mv 3
        var targetSpell = new Spell(targetCard, _bob);
        stack.Push(targetSpell);

        var card = KozilekTheGreatDistortionFactory.Create(_alice, triggers: null, stack);
        card.SetZone(ZoneType.Battlefield);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        var cost = ability.Costs.OfType<DiscardACardCost>().Single();
        cost.Target = discarded;
        cost.Pay(_alice);
        ability.SetChosenTargets(new[] { new object[] { targetSpell } });

        foreach (var e in ability.Effects) e.Execute();

        stack.GetAll().Should().Contain(targetSpell,
            "X = 1 ≠ target mv 3 → illegal on resolution, no counter (CR 608.2b).");
    }
}
