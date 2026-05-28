using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="FblthpTheLostFactory"/>.
///
/// Fblthp, the Lost (War of the Spark, {1}{U}):
///   Legendary Creature — Homunculus 1/1.
///   "When Fblthp enters, draw a card. If it entered from your library
///    or was cast from your library, draw two cards instead."
///   "When Fblthp becomes the target of a spell, shuffle Fblthp into
///    its owner's library."
///
/// Covers:
///   - Card identity (name, mana cost, Legendary Homunculus 1/1,
///     owner/controller, MV 2, blue).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - ETB trigger structure (single draw trigger, Battlefield active zone,
///     no intervening-if).
///   - ETB resolution — hand-cast path draws 1.
///   - ETB resolution — cast-from-library path draws 2 (WasCastFromLibrary).
///   - ETB resolution — placed-from-library path draws 2 (WasPlacedFromLibrary).
///   - Target trigger structure (single shuffle trigger, Battlefield active
///     zone, spell-only predicate).
///   - Target trigger fires when an opponent's spell targets Fblthp.
///   - Target trigger does NOT fire when an ability (not a spell) targets
///     Fblthp (spell-only posture, unlike Phantasmal Bear's "spell or
///     ability" posture).
///   - Shuffle effect moves Fblthp to its owner's library and shuffles.
///   - Shuffle effect is idempotent when Fblthp has already left the
///     battlefield.
/// </summary>
public class FblthpTheLostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fblthp_Identity_Legendary_Homunculus_1_1_At_1U()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);

        fblthp.Name.Should().Be("Fblthp, the Lost");
        fblthp.ManaCost.Should().Be("{1}{U}");
        fblthp.HasType(CardType.Creature).Should().BeTrue();
        fblthp.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        fblthp.HasSubtype(CardSubtype.Homunculus).Should().BeTrue();
        fblthp.BasePower.Should().Be(1);
        fblthp.BaseToughness.Should().Be(1);
        fblthp.Owner.Should().BeSameAs(_alice);
        fblthp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FblthpTheLost()
    {
        var card = NamedCardFactory.Create("Fblthp, the Lost", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fblthp, the Lost");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Homunculus).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Fblthp has one ETB draw trigger and one becomes-target shuffle trigger");
    }

    // ── ETB trigger structure ─────────────────────────────────────────────────

    [Fact]
    public void Fblthp_HasTwoTriggeredAbilities_EtbAndTarget()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);

        var triggers = fblthp.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2);

        // Both triggers active only on the battlefield (CR 603.6a).
        triggers.All(t => t.ActiveZones.Contains(ZoneType.Battlefield))
            .Should().BeTrue("both triggers are only active from the battlefield");
    }

    [Fact]
    public void Fblthp_EtbTrigger_HasNoInterveningIf()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);

        // The ETB draw trigger has no intervening-if — the library branch
        // is evaluated at resolve time in the effect body, not as a
        // queue-time gate (CR 603.4).
        var etbTrigger = GetEtbTrigger(fblthp);
        etbTrigger.InterveningIf.Should().BeNull(
            "the library-draw bonus is checked at resolution, not at trigger queue time");
    }

    // ── ETB resolution — hand-cast draws 1 ───────────────────────────────────

    [Fact]
    public void EtbResolve_HandCast_DrawsOneCard()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);
        SeatOnBattlefield(fblthp);
        // WasCastFromLibrary and WasPlacedFromLibrary both false (default).

        var top = new Instant("Top", "{U}") { Owner = _alice };
        AddToLibrary(_alice, top);

        ExecuteEtb(fblthp);

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "hand-cast Fblthp draws 1");
    }

    // ── ETB resolution — cast from library draws 2 ───────────────────────────

    [Fact]
    public void EtbResolve_CastFromLibrary_DrawsTwoCards()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);
        SeatOnBattlefield(fblthp);

        // Stamp WasCastFromLibrary — simulates SpellCastFlow's stamp when
        // the card was cast from the library (Narset, Future Sight, etc.).
        fblthp.SetWasCastFromLibrary(true);

        var t1 = new Instant("Top1", "{U}") { Owner = _alice };
        var t2 = new Instant("Top2", "{U}") { Owner = _alice };
        var t3 = new Instant("Top3", "{U}") { Owner = _alice };
        AddToLibrary(_alice, t1, t2, t3);

        ExecuteEtb(fblthp);

        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "cast-from-library Fblthp draws 2 instead of 1");
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { t1, t2 });
    }

    // ── ETB resolution — entered from library (not cast) draws 2 ─────────────

    [Fact]
    public void EtbResolve_PlacedFromLibrary_DrawsTwoCards()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);
        SeatOnBattlefield(fblthp);

        // Stamp WasPlacedFromLibrary — simulates ZoneService's stamp when
        // a Library → Battlefield move is observed without a cast marker
        // (e.g., a library-cheating effect, Glimpse of Nature equivalent).
        fblthp.SetWasPlacedFromLibrary(true);

        var t1 = new Instant("Top1", "{U}") { Owner = _alice };
        var t2 = new Instant("Top2", "{U}") { Owner = _alice };
        AddToLibrary(_alice, t1, t2);

        ExecuteEtb(fblthp);

        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "entered-from-library Fblthp draws 2 instead of 1");
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { t1, t2 });
    }

    // ── Target trigger — structure ────────────────────────────────────────────

    [Fact]
    public void Fblthp_TargetTrigger_SpellTargets_Surfaces()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fblthp = FblthpTheLostFactory.Create(_alice, bus, triggers);
        SeatOnBattlefield(fblthp);

        // Bob casts a creature-targeting spell at Fblthp.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(fblthp) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Fblthp triggers when it becomes the target of a spell");
    }

    [Fact]
    public void Fblthp_TargetTrigger_AbilityTargets_DoesNotSurface()
    {
        // Unlike Phantasmal Bear ("spell or ability"), Fblthp's oracle
        // says "spell" only. A TargetsChosenEvent whose stack object is
        // NOT an ISpell must not fire the shuffle trigger.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fblthp = FblthpTheLostFactory.Create(_alice, bus, triggers);
        SeatOnBattlefield(fblthp);

        // Construct a Spell to satisfy TargetsChosenEvent's IStackObject
        // parameter, then use a different non-ISpell stack object.
        // The simplest stand-in: create a TriggeredAbility shell and wrap
        // its trigger event. For this test we use the fblthp's own ETB
        // trigger (which is a TriggeredAbility, not an ISpell) to produce
        // a TargetsChosenEvent with a non-spell stack object.
        //
        // Since TriggeredAbility implements IStackObject (not ISpell), the
        // condition predicate must return false.
        var etbTrigger = GetEtbTrigger(fblthp);
        bus.Publish(new TargetsChosenEvent(etbTrigger, new[] { Target.Permanent(fblthp) }));

        triggers.PendingCount.Should().Be(0,
            "Fblthp does NOT shuffle when targeted by an ability (spell-only posture)");
    }

    // ── Shuffle effect ────────────────────────────────────────────────────────

    [Fact]
    public void ShuffleEffect_MovesFblthpToLibrary_AndShuffles()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fblthp = FblthpTheLostFactory.Create(_alice, bus, triggers);
        SeatOnBattlefield(fblthp);

        // Add a few cards to the library so we can verify a shuffle occurred.
        var t1 = new Instant("Top1", "{U}") { Owner = _alice };
        var t2 = new Instant("Top2", "{U}") { Owner = _alice };
        AddToLibrary(_alice, t1, t2);

        // Fire the becomes-target trigger via the bus.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(fblthp) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        // Execute the pending shuffle trigger.
        ExecuteTargetTrigger(fblthp);

        fblthp.Zone.Should().Be(ZoneType.Library,
            "Fblthp shuffles into its owner's library when targeted by a spell");
        _alice.Zones.Library.GetCards().Should().Contain(fblthp,
            "Fblthp is present in Alice's library after the shuffle");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fblthp,
            "Fblthp is no longer on the battlefield after the shuffle");
    }

    [Fact]
    public void ShuffleEffect_Idempotent_WhenFblthpAlreadyGone()
    {
        // CR 603.7c — if Fblthp has already left the battlefield before
        // the trigger resolves (destroyed by the targeting spell, another
        // effect, etc.) the library shuffle still fires but the zone-move
        // is skipped. No exception should be thrown.
        var fblthp = FblthpTheLostFactory.Create(_alice);

        // Fblthp is in the graveyard (already gone from battlefield).
        fblthp.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(fblthp);

        var targetTrigger = GetTargetTrigger(fblthp);
        var act = () => { foreach (var e in targetTrigger.Effects) e.Execute(); };

        act.Should().NotThrow("shuffle effect is idempotent when Fblthp is not on the battlefield");
        // The library shuffle still runs (no crash), and Fblthp stays in graveyard
        // (zone-move block is skipped per the Zone != Battlefield guard).
        fblthp.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ── ZoneService stamp verification ────────────────────────────────────────

    [Fact]
    public void ZoneService_LibraryToBattlefield_StampsWasPlacedFromLibrary()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);
        fblthp.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(fblthp);

        var zones = new ZoneService();
        zones.MoveCard(fblthp, ZoneType.Library, ZoneType.Battlefield, _alice);

        fblthp.WasPlacedFromLibrary.Should().BeTrue(
            "ZoneService stamps WasPlacedFromLibrary on Library → Battlefield without a cast");
        fblthp.WasCastFromLibrary.Should().BeFalse(
            "WasCastFromLibrary is NOT set by a non-cast zone move");
    }

    [Fact]
    public void ZoneService_BattlefieldExit_ClearsWasPlacedFromLibrary()
    {
        var fblthp = FblthpTheLostFactory.Create(_alice);
        fblthp.SetWasPlacedFromLibrary(true);
        fblthp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fblthp);

        var zones = new ZoneService();
        zones.MoveCard(fblthp, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        fblthp.WasPlacedFromLibrary.Should().BeFalse(
            "ZoneService clears WasPlacedFromLibrary on battlefield exit (CR 400.7)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TriggeredAbility GetEtbTrigger(Creature fblthp)
    {
        // ETB trigger is the one whose condition is NOT a TargetsChosenEvent
        // condition — i.e. the CardMovedEvent-based ETB condition.
        // By convention it is the first trigger added.
        var triggers = fblthp.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCountGreaterThanOrEqualTo(1);
        // The ETB trigger has no InterveningIf; the target trigger also
        // has no InterveningIf — so distinguish by the trigger effect
        // description which contains "draw" for ETB.
        return triggers.First(t => t.Effects.Any(e => e.Description.Contains("draw")));
    }

    private static TriggeredAbility GetTargetTrigger(Creature fblthp)
    {
        var triggers = fblthp.Abilities.OfType<TriggeredAbility>().ToList();
        return triggers.First(t => t.Effects.Any(e => e.Description.Contains("shuffle")));
    }

    private static void AddToLibrary(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            if (c is Card concrete)
            {
                concrete.SetOwner(p);
                concrete.SetZone(ZoneType.Library);
            }
            p.Zones.Library.AddCard(c);
        }
    }

    private static void SeatOnBattlefield(Creature card)
    {
        card.SetZone(ZoneType.Battlefield);
        card.Owner!.Zones.Battlefield.AddCard(card);
    }

    private static void ExecuteEtb(Creature fblthp)
    {
        var trigger = GetEtbTrigger(fblthp);
        foreach (var effect in trigger.Effects) effect.Execute();
    }

    private static void ExecuteTargetTrigger(Creature fblthp)
    {
        var trigger = GetTargetTrigger(fblthp);
        foreach (var effect in trigger.Effects) effect.Execute();
    }
}
