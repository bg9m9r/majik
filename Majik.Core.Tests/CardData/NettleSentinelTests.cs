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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="NettleSentinelFactory"/> (Eventide, {G},
/// Creature — Elf Warrior 2/2).
///
/// Covers:
/// - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch hands back the correct shape.
/// - "Doesn't untap during your untap step" registers a per-permanent skip
///   in <see cref="UntapStepRestrictions"/> when an IEventBus is wired
///   (CR 502.1).
/// - The doesn't-untap registration is removed when the card leaves the
///   battlefield.
/// - Untap-on-green-spell trigger fires on a SpellCastEvent for a green
///   spell cast by the controller (CR 603.1 / CR 105).
/// - Trigger does NOT fire for a non-green spell.
/// - Trigger does NOT fire for an opponent's green spell.
/// - Resolve effect untaps Nettle Sentinel; idempotent on an already
///   untapped Sentinel (CR 701.20).
/// </summary>
public class NettleSentinelTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NettleSentinelTests()
    {
        UntapStepRestrictions.Clear();
    }

    public void Dispose() => UntapStepRestrictions.Clear();

    private static Majik.Core.Spells.Spell NewSpell(Player controller, Card cardShape)
    {
        cardShape.SetOwner(controller);
        return new Majik.Core.Spells.Spell(cardShape, controller);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NettleSentinel_Identity_ElfWarrior_2_2_AtCostG()
    {
        var c = NettleSentinelFactory.Create(_alice);

        c.Name.Should().Be("Nettle Sentinel");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NettleSentinel_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Nettle Sentinel", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Nettle Sentinel");
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.ManaCost.Should().Be("{G}");
    }

    // -----------------------------------------------------------------------
    // Doesn't-untap static (CR 502.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void NettleSentinel_DoesNotUntap_RegistersSkipWhenOnBattlefieldWithEventBus()
    {
        var bus = new EventBus();
        var c = NettleSentinelFactory.Create(_alice, triggers: null, eventBus: bus);

        // ETB: zone change → battlefield triggers the lifecycle sync.
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));

        UntapStepRestrictions.ShouldSkipUntap(c, _alice).Should().BeTrue(
            "CR 502.1 — Nettle Sentinel's printed clause registers a per-permanent untap skip");
    }

    [Fact]
    public void NettleSentinel_DoesNotUntap_RemovesSkipWhenLeavingBattlefield()
    {
        var bus = new EventBus();
        var c = NettleSentinelFactory.Create(_alice, triggers: null, eventBus: bus);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));
        UntapStepRestrictions.ShouldSkipUntap(c, _alice).Should().BeTrue();

        // Move to graveyard → lifecycle should remove the skip.
        _alice.Zones.Battlefield.RemoveCard(c);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(c, _alice).Should().BeFalse(
            "leaving the battlefield lifts the registered untap skip");
    }

    [Fact]
    public void NettleSentinel_DoesNotUntap_ShapeOnlyConstructor_HasNoEventBus_DoesNotRegister()
    {
        // Single-arg path — no event bus, so the lifecycle binder is NOT
        // attached. Shape tests should not see a stray registration.
        var c = NettleSentinelFactory.Create(_alice);

        UntapStepRestrictions.ShouldSkipUntap(c, _alice).Should().BeFalse(
            "the shape-only ctor doesn't attach the doesn't-untap lifecycle binder");
    }

    // -----------------------------------------------------------------------
    // Untap-on-green-spell-cast trigger (CR 603.1 / CR 105)
    // -----------------------------------------------------------------------

    private Creature PutOnBattlefield(Player owner)
    {
        var c = NettleSentinelFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void NettleSentinel_Trigger_FiresOnGreenSpellCastByController()
    {
        var c = PutOnBattlefield(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var greenSpellCard = new Creature("Llanowar Elves", "G", 1, 1);
        var spell = NewSpell(_alice, greenSpellCard);
        var evt = new SpellCastEvent(spell);

        trigger.IsTriggered(evt).Should().BeTrue(
            "CR 105 — Llanowar Elves's printed pip is {G} so the spell is green; controller-cast → trigger fires");
    }

    [Fact]
    public void NettleSentinel_Trigger_DoesNotFireForNonGreenSpell()
    {
        var c = PutOnBattlefield(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var redSpellCard = new Instant("Lightning Bolt", "R");
        var spell = NewSpell(_alice, redSpellCard);
        var evt = new SpellCastEvent(spell);

        trigger.IsTriggered(evt).Should().BeFalse(
            "Lightning Bolt's colour set is {R} (CR 105) — no Green pip, no trigger");
    }

    [Fact]
    public void NettleSentinel_Trigger_DoesNotFireForOpponentGreenSpell()
    {
        var c = PutOnBattlefield(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var greenSpellCard = new Creature("Bob's Llanowar Elves", "G", 1, 1);
        var spell = NewSpell(_bob, greenSpellCard);
        var evt = new SpellCastEvent(spell);

        trigger.IsTriggered(evt).Should().BeFalse(
            "the trigger fires only on YOUR green spells (CR 603.1 controller scope)");
    }

    [Fact]
    public void NettleSentinel_Trigger_DoesNotFireForColorlessSpell()
    {
        var c = PutOnBattlefield(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // A colourless artifact has no Green pip — trigger should not fire.
        var colorlessCard = new Artifact("Bonesplitter", "1");
        var spell = NewSpell(_alice, colorlessCard);
        var evt = new SpellCastEvent(spell);

        trigger.IsTriggered(evt).Should().BeFalse(
            "colourless spells (no Green pip) don't satisfy the green-spell predicate");
    }

    [Fact]
    public void NettleSentinel_Resolve_UntapsSelf()
    {
        var c = PutOnBattlefield(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        c.Tap();
        c.IsTapped.Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        c.IsTapped.Should().BeFalse(
            "CR 701.20 — the resolved effect untaps Nettle Sentinel");
    }

    [Fact]
    public void NettleSentinel_Resolve_OnAlreadyUntapped_IsNoOp()
    {
        var c = PutOnBattlefield(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        c.IsTapped.Should().BeFalse();

        var act = () => { foreach (var effect in trigger.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "untapping an already-untapped permanent is a no-op (CR 701.20)");
        c.IsTapped.Should().BeFalse();
    }
}
