using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HydroidKrasisFactory"/> (Ravnica Allegiance,
/// {X}{G}{U}). Creature — Jellyfish Hydra Beast 0/0.
///
/// Oracle (verified against Scryfall):
///   "When you cast this spell, you gain half X life and draw half X cards.
///    Round down each time.
///    Flying, trample
///    This creature enters with X +1/+1 counters on it."
///
/// Hydroid Krasis is the canonical "variable-X SPELL cast through the live
/// dispatcher" payoff that the cast-pipeline X-fold deferral unblocks: its CAST
/// trigger (CR 601.2i) reads the chosen X to scale the life-gain + draw, so the
/// dispatcher must have folded X into the mana payment + stamped it on
/// <see cref="Card.PendingCastX"/> for the card to do anything. These tests pin
/// the card body; the dispatcher-payment fold itself is covered by
/// <c>TurnDriverCastXPaymentTests</c> / <c>GameFacadeCastXPaymentTests</c>.
///
/// Covers:
/// - Identity (Creature — Jellyfish Hydra Beast, {X}{G}{U}, 0/0, GU colours).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="Card.ManaCostValue.HasX"/> reports true.
/// - Flying + Trample keyword markers (CR 702.9 / 702.19).
/// - Cast trigger shape (over <see cref="SpellCastEvent"/>, matches self only).
/// - Cast trigger effect: floor(X / 2) life gained + floor(X / 2) cards drawn,
///   reading X off <see cref="Card.PendingCastX"/> (round DOWN each).
/// - The cast effect does NOT clear PendingCastX (the EntersWithCountersBinder
///   still needs the same X for the ETB counters).
/// - "Enters with X +1/+1 counters" is owned by the generic
///   <see cref="EntersWithCountersBinder"/> (NOT a self-managed ETB trigger).
/// </summary>
[Trait("Color", "M")]
public class HydroidKrasisFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void HydroidKrasis_Identity()
    {
        var c = HydroidKrasisFactory.Create(_alice);

        c.Name.Should().Be("Hydroid Krasis");
        c.ManaCost.Should().Be("{X}{G}{U}");
        c.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Jellyfish).Should().BeTrue();
        c.HasSubtype(CardSubtype.Hydra).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(0);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HydroidKrasis_Dispatch_ViaNamedFactory()
    {
        var c = NamedCardFactory.Create("Hydroid Krasis", _alice);
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Hydroid Krasis");
    }

    // ── Flying + Trample ────────────────────────────────────────────────

    [Fact]
    public void HydroidKrasis_HasFlyingAndTrample()
    {
        var c = HydroidKrasisFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Flying")
            .Should().HaveCount(1, "CR 702.9 — Flying keyword marker.");
        c.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Trample")
            .Should().HaveCount(1, "CR 702.19 — Trample keyword marker.");
        CombatAbilities.HasFlying(c).Should().BeTrue("Hydroid Krasis prints Flying.");
        CombatAbilities.HasTrample(c).Should().BeTrue("Hydroid Krasis prints Trample.");
    }

    // ── Cast trigger shape ──────────────────────────────────────────────

    [Fact]
    public void HydroidKrasis_HasCastTrigger_OverSpellCastEvent()
    {
        var c = HydroidKrasisFactory.Create(_alice);

        var cast = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);
        cast.Source.Should().BeSameAs(c);
        cast.Controller.Should().BeSameAs(_alice);
        cast.ActiveZones.Should().Contain(ZoneType.Stack,
            "a 'When you cast this spell' trigger is live while the card is a " +
            "spell on the stack (CR 601.2i)");
    }

    [Fact]
    public void CastTrigger_Matches_OnlyThisSpellBeingCast()
    {
        var c = HydroidKrasisFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);
        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;

        var selfSpell = new Majik.Core.Spells.Spell(c, _alice);
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue(
            "casting THIS spell fires the ability (CR 601.2i).");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse(
            "casting a different spell does not fire this ability.");
    }

    // ── Cast trigger effect: half X (round down) life + draw ────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(7, 3)]
    public void CastTrigger_GainsHalfXLife_AndDrawsHalfXCards_RoundedDown(int x, int expectedHalf)
    {
        var c = HydroidKrasisFactory.Create(_alice);
        // The spell is on the stack when "When you cast this spell" resolves.
        _alice.Zones.Hand.AddCard(c); // start anywhere; PendingCastX drives X
        c.SetZone(ZoneType.Stack);
        c.SetPendingCastX(x);

        // Stock the library so the draws have cards to take.
        for (var i = 0; i < 10; i++)
        {
            var lib = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
            lib.SetOwner(_alice);
            _alice.Zones.Library.AddCard(lib);
            lib.SetZone(ZoneType.Library);
        }

        var lifeBefore = _alice.LifeTotal;
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var cast = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);
        foreach (var e in cast.Effects) e.Execute();

        (_alice.LifeTotal - lifeBefore).Should().Be(expectedHalf,
            $"gain half X (={x}) life, rounded DOWN (CR 119.3 / 107.16)");
        (_alice.Zones.Hand.GetCards().Count() - handBefore).Should().Be(expectedHalf,
            $"draw half X (={x}) cards, rounded DOWN (CR 120 / 107.16)");
    }

    [Fact]
    public void CastTrigger_DoesNotClearPendingCastX_SoBinderCountersStillSeeX()
    {
        // CR 601.2i — the cast trigger resolves BEFORE Hydroid enters. It must
        // leave PendingCastX intact so the EntersWithCountersBinder reads the
        // same X for the +1/+1 counters at battlefield entry.
        var c = HydroidKrasisFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Stack);
        c.SetPendingCastX(4);

        for (var i = 0; i < 4; i++)
        {
            var lib = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
            lib.SetOwner(_alice);
            _alice.Zones.Library.AddCard(lib);
            lib.SetZone(ZoneType.Library);
        }

        var cast = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);
        foreach (var e in cast.Effects) e.Execute();

        c.PendingCastX.Should().Be(4,
            "the cast trigger reads X but does not consume it — the binder " +
            "needs it for the ETB counters");
    }

    // ── ETB +1/+1 counters: owned by the binder (CR 614.1d) ─────────────

    private static CardEntity HydroidEntity() =>
        new EmbeddedCardRepository().GetByName("Hydroid Krasis")!;

    [Fact]
    public void HydroidKrasis_DoesNotSelfManageEntersWithCounters()
    {
        var c = HydroidKrasisFactory.Create(_alice);

        c.SelfManagesEntersWithCounters.Should().BeFalse(
            "the binder owns the ETB-X replacement; self-managing suppresses it " +
            "and yields zero counters on the Approach-B prod route");
        c.Abilities.OfType<TriggeredAbility>()
            .Should().NotContain(t => t.Effects.Any(e => e.Description.Contains("enters with X")),
                "the ETB-X counters clause is not a factory-attached trigger");
    }

    [Fact]
    public void HydroidKrasis_BinderReplacement_EntersWithXEquals4_Counters()
    {
        var bus = new ReplacementBus();
        var c = HydroidKrasisFactory.Create(_alice);

        EntersWithCountersBinder.Bind(c, HydroidEntity(), bus).Should().BeTrue(
            "the binder matches 'enters with X +1/+1 counters on it' and registers " +
            "the variable-X replacement");

        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        c.SetPendingCastX(4);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4,
            "Hydroid Krasis enters WITH X (=4) +1/+1 counters per CR 614.1d");
    }

    [Fact]
    public void HydroidKrasis_BinderReplacement_ZeroX_NoCounters()
    {
        var bus = new ReplacementBus();
        var c = HydroidKrasisFactory.Create(_alice);

        EntersWithCountersBinder.Bind(c, HydroidEntity(), bus).Should().BeTrue();

        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        // No SetPendingCastX → X = 0.

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X = 0 → zero counters (enters as base 0/0, dies to SBA later)");
    }
}
