using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StormscaleScionFactory"/>.
///
/// Stormscale Scion (Tarkir: Dragonstorm, {4}{R}{R}). Creature — Dragon 4/4.
/// Oracle:
///   "Flying
///    Other Dragons you control get +1/+1.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. Copies become tokens.)"
///
/// Coverage:
/// - Identity (name, type, subtype, cost, colour, P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - Flying keyword marker (CR 702.9).
/// - Lord static (CR 613.7c): other controller-Dragons get +1/+1; self and
///   opponent Dragons unaffected; non-Dragons unaffected.
/// - Structural Storm trigger (CR 702.40) — SpellCastEvent over Stack.
/// - Storm as first spell this turn: no token copies.
/// - Storm as Nth spell this turn: N-1 token copies, each a 4/4 flying Dragon.
/// </summary>
public class StormscaleScionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeDragon(Player owner, string name = "Shivan Dragon")
    {
        var c = new Creature(name, "{4}{R}{R}", 5, 5, subtypes: new[] { CardSubtype.Dragon });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonDragon(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void StormscaleScion_Identity()
    {
        var c = StormscaleScionFactory.Create(_alice);

        c.Name.Should().Be("Stormscale Scion");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.ManaCost.Should().Be("{4}{R}{R}");
        c.ManaCostValue.TotalValue.Should().Be(6);
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        CardColors.GetColors(c).Should().Contain(ManaColor.Red);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StormscaleScion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Stormscale Scion", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Stormscale Scion");
        ((Creature)c).HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    // ── Flying ──────────────────────────────────────────────────────────

    [Fact]
    public void StormscaleScion_HasFlying()
    {
        var c = StormscaleScionFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "Stormscale Scion prints Flying (CR 702.9).");
    }

    // ── Lord static ─────────────────────────────────────────────────────

    [Fact]
    public void StormscaleScion_BuffsOtherControllerDragon_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherDragon = MakeDragon(_alice);
        otherDragon.ActiveEffects = svc;

        var scion = StormscaleScionFactory.Create(_alice, svc);
        scion.SetZone(ZoneType.Battlefield);
        scion.ActiveEffects = svc;

        otherDragon.GetPower().Should().Be(6,
            "other Dragons controlled by the Scion's controller get +1/+1 (5 → 6 power).");
        otherDragon.GetToughness().Should().Be(6);
    }

    [Fact]
    public void StormscaleScion_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var scion = StormscaleScionFactory.Create(_alice, svc);
        scion.SetZone(ZoneType.Battlefield);
        scion.ActiveEffects = svc;

        scion.GetPower().Should().Be(4,
            "printed 'Other Dragons' excludes the Scion itself (CR 613.1g).");
        scion.GetToughness().Should().Be(4);
    }

    [Fact]
    public void StormscaleScion_DoesNotBuffOpponentDragon()
    {
        var svc = new ContinuousEffectsService();

        var bobDragon = MakeDragon(_bob);
        bobDragon.ActiveEffects = svc;

        var scion = StormscaleScionFactory.Create(_alice, svc);
        scion.SetZone(ZoneType.Battlefield);
        scion.ActiveEffects = svc;

        bobDragon.GetPower().Should().Be(5,
            "controller-scoped lord — Bob's Dragons are unaffected (allPlayers: false).");
        bobDragon.GetToughness().Should().Be(5);
    }

    [Fact]
    public void StormscaleScion_DoesNotBuffNonDragon()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonDragon(_alice);
        bears.ActiveEffects = svc;

        var scion = StormscaleScionFactory.Create(_alice, svc);
        scion.SetZone(ZoneType.Battlefield);
        scion.ActiveEffects = svc;

        bears.GetPower().Should().Be(2, "the anthem only buffs Dragons.");
        bears.GetToughness().Should().Be(2);
    }

    // ── Storm — structural ──────────────────────────────────────────────

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var card = StormscaleScionFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Stormscale Scion prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(card);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void StormTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = StormscaleScionFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Creature("Other Dragon", "{R}", 1, 1, subtypes: new[] { CardSubtype.Dragon });
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ── Storm — first spell (no copies) ─────────────────────────────────

    [Fact]
    public void Cast_AsFirstSpellThisTurn_NoTokenCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        var card = StormscaleScionFactory.Create(_alice, continuousEffects: null, stack, ts);
        card.SetZone(ZoneType.Stack);

        // CR 700.6 / 702.40a — the spell being cast is counted on
        // announcement (TurnDriver bookkeeping).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(1);

        var spell = new Majik.Core.Spells.Spell(card, _alice);
        var storm = card.Abilities.OfType<TriggeredAbility>().Single();
        storm.Condition.Matches(new SpellCastEvent(spell), storm).Should().BeTrue();

        foreach (var e in storm.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Where(c => c.IsToken)
            .Should().BeEmpty("first spell — no storm copies, so no token Scions.");
    }

    // ── Storm — fourth spell (3 token copies) ───────────────────────────

    [Fact]
    public void Cast_AsFourthSpellThisTurn_ThreeTokenCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice already cast three other spells before Stormscale Scion.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(4);

        var card = StormscaleScionFactory.Create(_alice, continuousEffects: null, stack, ts);
        card.SetZone(ZoneType.Stack);

        var spell = new Majik.Core.Spells.Spell(card, _alice);
        var storm = card.Abilities.OfType<TriggeredAbility>().Single();
        storm.Condition.Matches(new SpellCastEvent(spell), storm).Should().BeTrue();

        foreach (var e in storm.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(3, "3 other spells cast before → 3 token copies (CR 702.40a).");

        foreach (var tok in tokens)
        {
            tok.Name.Should().Be("Stormscale Scion");
            tok.BasePower.Should().Be(4);
            tok.BaseToughness.Should().Be(4);
            tok.IsToken.Should().BeTrue();
            tok.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
            CombatAbilities.HasFlying(tok).Should().BeTrue(
                "the copy is a copy of the Scion, so it has Flying.");
            CardColors.GetColors(tok).Should().Contain(ManaColor.Red);
            tok.Controller.Should().BeSameAs(_alice);
        }
    }

    [Fact]
    public void StormTrigger_NullTurnState_FiresWithoutCopies()
    {
        var stack = new Majik.Core.Stack.Stack();
        var card = StormscaleScionFactory.Create(_alice, continuousEffects: null, stack, turnState: null);
        card.SetZone(ZoneType.Stack);
        var storm = card.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(card, _alice);
        storm.Condition.Matches(new SpellCastEvent(spell), storm).Should().BeTrue();

        var act = () => { foreach (var e in storm.Effects) e.Execute(); };
        act.Should().NotThrow();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Where(c => c.IsToken)
            .Should().BeEmpty();
    }
}
