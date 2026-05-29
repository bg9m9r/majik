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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Merrow Reejerey (Lorwyn, {2}{U}, Creature — Merfolk Soldier 2/2).
///
/// Oracle text:
///   "Other Merfolk creatures you control get +1/+1.
///    Whenever you cast a Merfolk spell, you may tap or untap target permanent."
///
/// Covers:
///   - Identity (name, type, mana cost, Merfolk + Soldier subtypes, 2/2,
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - Anthem lord: buffs the controller's OTHER Merfolk +1/+1 (allPlayers:
///     false — "you control"), does NOT self-buff ("Other"), does NOT buff
///     an opponent's Merfolk, and does NOT buff non-Merfolk.
///   - LTB lifts the anthem bonus.
///   - Cast trigger fires when the controller casts a Merfolk spell;
///     does NOT fire on a non-Merfolk spell; does NOT fire when an opponent
///     casts a Merfolk spell ("you cast").
///   - The tap-or-untap effect flips the chosen target's tapped state at
///     resolution (Pestermite-style deterministic "useful flip").
/// </summary>
public class MerrowReejereyTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewMerfolkSpell(Player controller)
    {
        var merfolk = new Creature("Silvergill Adept", "1U", 2, 1,
            subtypes: new[] { CardSubtype.Merfolk }) { Owner = controller };
        return new Majik.Core.Spells.Spell(merfolk, controller);
    }

    private static Majik.Core.Spells.Spell NewNonMerfolkSpell(Player controller)
    {
        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear }) { Owner = controller };
        return new Majik.Core.Spells.Spell(bear, controller);
    }

    // ─── Identity / dispatch ─────────────────────────────────────────────────

    [Fact]
    public void MerrowReejerey_Identity()
    {
        var card = MerrowReejereyFactory.Create(_alice);

        card.Name.Should().Be("Merrow Reejerey");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MerrowReejerey_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Merrow Reejerey", _alice);
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Merrow Reejerey");
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // ─── Anthem lord (+1/+1 to OTHER Merfolk you control) ────────────────────

    [Fact]
    public void MerrowReejerey_BuffsOwnMerfolk_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = MakeMerfolk(_alice, svc);
        var lord = MerrowReejereyFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        aliceMerfolk.GetPower().Should().Be(2,
            "Merrow Reejerey gives +1/+1 to the controller's other Merfolk (1 base + 1).");
        aliceMerfolk.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MerrowReejerey_DoesNotSelfBuff()
    {
        var svc = new ContinuousEffectsService();

        var lord = MerrowReejereyFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        lord.GetPower().Should().Be(2, "Merrow Reejerey says 'Other' — no self-buff.");
        lord.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MerrowReejerey_IsNotSymmetric_DoesNotBuffOpponentMerfolk()
    {
        // "Other Merfolk creatures you control" — scoped to controller only.
        var svc = new ContinuousEffectsService();

        var bobMerfolk = MakeMerfolk(_bob, svc);
        var lord = MerrowReejereyFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        bobMerfolk.GetPower().Should().Be(1,
            "Merrow Reejerey does not buff an opponent's Merfolk ('you control').");
        bobMerfolk.GetToughness().Should().Be(1);
    }

    [Fact]
    public void MerrowReejerey_DoesNotBuff_NonMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2, subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var lord = MerrowReejereyFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "Merrow Reejerey only buffs Merfolk.");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MerrowReejerey_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = MakeMerfolk(_alice, svc);
        var lord = MerrowReejereyFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        aliceMerfolk.GetPower().Should().Be(2);

        lord.SetZone(ZoneType.Graveyard);

        aliceMerfolk.GetPower().Should().Be(1, "bonus lifts when Merrow Reejerey leaves the battlefield.");
        aliceMerfolk.GetToughness().Should().Be(1);
    }

    // ─── Cast trigger ("Whenever you cast a Merfolk spell ...") ──────────────

    [Fact]
    public void MerrowReejerey_CastMerfolkSpell_FiresTrigger_AndUntapsTappedTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var lord = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lord);
        lord.SetZone(ZoneType.Battlefield);

        // A tapped permanent to flip.
        var land = new Land("Island", subtypes: new[] { CardSubtype.Island })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        land.Tap();
        land.IsTapped.Should().BeTrue();

        bus.Publish(new SpellCastEvent(NewMerfolkSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "casting a Merfolk spell fires Merrow Reejerey's trigger exactly once (CR 603.1).");

        var ability = (TriggeredAbility)lord.Abilities.OfType<TriggeredAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.SetChosenTargets(new[] { new object[] { land } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        land.IsTapped.Should().BeFalse(
            "the tap-or-untap effect untaps the chosen tapped target (deterministic useful flip).");
    }

    [Fact]
    public void MerrowReejerey_CastMerfolkSpell_TapsUntappedTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var lord = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lord);
        lord.SetZone(ZoneType.Battlefield);

        var land = new Land("Island", subtypes: new[] { CardSubtype.Island })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        land.IsTapped.Should().BeFalse();

        bus.Publish(new SpellCastEvent(NewMerfolkSpell(_alice)));

        var ability = (TriggeredAbility)lord.Abilities.OfType<TriggeredAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.SetChosenTargets(new[] { new object[] { land } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        land.IsTapped.Should().BeTrue(
            "the tap-or-untap effect taps the chosen untapped target (deterministic useful flip).");
    }

    [Fact]
    public void MerrowReejerey_CastNonMerfolkSpell_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var lord = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lord);
        lord.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewNonMerfolkSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "a non-Merfolk spell does not match the 'Merfolk spell' predicate.");
    }

    [Fact]
    public void MerrowReejerey_OpponentCastsMerfolk_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var lord = MerrowReejereyFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lord);
        lord.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewMerfolkSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts the trigger to Merrow Reejerey's controller.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Creature MakeMerfolk(Player controller, ContinuousEffectsService svc)
        => new Creature("Lullmage Mentor", "U", 1, 1, subtypes: new[] { CardSubtype.Merfolk })
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
}
