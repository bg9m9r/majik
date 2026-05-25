using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MerrowReejereyFactory"/> (Lorwyn, {1}{U},
/// Creature — Merfolk Rogue 2/2).
///
/// Oracle text:
///   "Whenever you cast a Merfolk spell, choose one —
///     • Tap target permanent.
///     • Untap target permanent."
///
/// Covers:
///   - Identity (name, type, Merfolk + Rogue subtypes, 2/2, {1}{U},
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - Cast trigger fires when controller casts a Merfolk spell.
///   - Cast trigger does NOT fire when controller casts a non-Merfolk
///     spell (e.g. a vanilla Bear).
///   - Cast trigger does NOT fire when an opponent casts a Merfolk
///     spell ("whenever YOU cast").
///   - Resolution: deterministic "useful flip" — untaps tapped target,
///     taps untapped target (modal collapsed; see factory xmldoc).
///   - CR 608.2b — off-battlefield target at resolution is a clean no-op.
///   - Boost stacking with Lord of Atlantis + Master of the Pearl Trident
///     (Reejerey gets +1/+1 from each Merfolk lord).
/// </summary>
public class MerrowReejereyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ─── Identity / dispatch ─────────────────────────────────────────────────

    [Fact]
    public void Reejerey_Identity()
    {
        var r = MerrowReejereyFactory.Create(_alice);

        r.Name.Should().Be("Merrow Reejerey");
        r.ManaCost.Should().Be("{1}{U}");
        r.HasType(CardType.Creature).Should().BeTrue();
        r.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        r.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        r.BasePower.Should().Be(2);
        r.BaseToughness.Should().Be(2);
        r.Owner.Should().BeSameAs(_alice);
        r.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Reejerey_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Merrow Reejerey", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Merrow Reejerey");
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void Reejerey_CastTrigger_Shape_TargetPermanent_1to1()
    {
        var r = MerrowReejereyFactory.Create(_alice);

        var trigger = r.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().HaveCount(1);

        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1, "modal trigger requires a permanent target (no \"may\")");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("permanent");

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ─── Cast trigger event filter ───────────────────────────────────────────

    [Fact]
    public void Reejerey_FiresOn_ControllerCastsMerfolkSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var r = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(r);
        r.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewMerfolkSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "casting a Merfolk spell by the controller fires Reejerey's trigger once (CR 603.1)");
    }

    [Fact]
    public void Reejerey_DoesNotFire_OnControllerCastsNonMerfolkSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var r = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(r);
        r.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewBearSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "vanilla Bear is not a Merfolk spell — Reejerey's trigger should not fire");
    }

    [Fact]
    public void Reejerey_DoesNotFire_OnOpponentCastsMerfolkSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var r = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(r);
        r.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewMerfolkSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts the trigger to Reejerey's controller (CR 603.1)");
    }

    // ─── Resolution: tap/untap target permanent ──────────────────────────────

    [Fact]
    public void Reejerey_Resolve_UntapsTappedTargetPermanent()
    {
        var r = MerrowReejereyFactory.Create(_alice);

        var target = NewBattlefieldCreature("Grizzly Bears", _bob);
        target.Tap();

        var trigger = r.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in trigger.Effects) e.Execute();

        target.IsTapped.Should().BeFalse(
            "Reejerey's deterministic \"useful flip\" untaps a tapped target");
    }

    [Fact]
    public void Reejerey_Resolve_TapsUntappedTargetPermanent()
    {
        var r = MerrowReejereyFactory.Create(_alice);

        var target = NewBattlefieldCreature("Grizzly Bears", _bob);

        var trigger = r.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in trigger.Effects) e.Execute();

        target.IsTapped.Should().BeTrue("\"useful flip\" taps an untapped target");
    }

    [Fact]
    public void Reejerey_Resolve_TargetLeftBattlefield_NoOp()
    {
        var r = MerrowReejereyFactory.Create(_alice);

        // Target's gone to the graveyard before resolve — CR 608.2b
        // illegal-on-resolution → clean no-op.
        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var trigger = r.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in trigger.Effects) e.Execute();

        target.IsTapped.Should().BeFalse(
            "off-battlefield target at resolution is a clean no-op");
    }

    [Fact]
    public void Reejerey_EndToEnd_TriggerResolves_TapsBobsPermanent()
    {
        // Full event-bus → trigger queue → stack → resolve, then assert
        // the chosen target's tapped state flipped.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var r = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(r);
        r.SetZone(ZoneType.Battlefield);

        var target = NewBattlefieldCreature("Grizzly Bears", _bob);

        bus.Publish(new SpellCastEvent(NewMerfolkSpell(_alice)));
        triggers.PendingCount.Should().Be(1);

        // Wire the chosen target onto the pending trigger before it goes
        // to stack. We look it up directly off the card's ability — the
        // PutPendingTriggersOnStack helper consumes pending triggers in
        // FIFO order, so the queued ability is Reejerey's.
        var queued = r.Abilities.OfType<TriggeredAbility>().Single();
        queued.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        target.IsTapped.Should().BeTrue(
            "end-to-end: Merfolk cast fires trigger → resolves → taps the untapped target");
    }

    // ─── Boost stacking with the two lords ───────────────────────────────────

    [Fact]
    public void Reejerey_GetsBoostedBy_LordOfAtlantis_And_MasterOfThePearlTrident()
    {
        // Reejerey is a Merfolk, so both Merfolk lords' "Other Merfolk
        // +1/+1" effects apply. Base 2/2 → +1/+1 from Lord → +1/+1 from
        // Master → 4/4 with Islandwalk.
        var svc = new ContinuousEffectsService();

        var reejerey = MerrowReejereyFactory.Create(_alice);
        reejerey.Zone = ZoneType.Battlefield;
        reejerey.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(reejerey);

        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(lord);

        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(master);

        reejerey.GetPower().Should().Be(4,
            "base 2 + 1 (Lord of Atlantis, \"Other Merfolk\") + 1 (Master, \"Other Merfolk you control\")");
        reejerey.GetToughness().Should().Be(4);

        var chars = svc.Compute(reejerey);
        chars.Keywords.Should().Contain("Islandwalk",
            "both lords grant Islandwalk; Reejerey picks it up because it's another Merfolk");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Majik.Core.Spells.Spell NewMerfolkSpell(Player controller)
    {
        var merfolk = new Creature("Silvergill Adept", "{1}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard })
        {
            Owner = controller,
        };
        return new Majik.Core.Spells.Spell(merfolk, controller);
    }

    private static Majik.Core.Spells.Spell NewBearSpell(Player controller)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = controller,
        };
        return new Majik.Core.Spells.Spell(bear, controller);
    }

    private static Creature NewBattlefieldCreature(string name, Player controller)
    {
        var c = new Creature(name, "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
