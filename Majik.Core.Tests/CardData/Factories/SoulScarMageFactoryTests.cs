using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Soul-Scar Mage (Amonkhet, {R}).
///
/// Card: Creature — Human Monk 1/2.
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    If a source you control would deal noncombat damage to a creature
///    an opponent controls, put that many -1/-1 counters on that creature
///    instead."
///
/// Covers:
///   - Identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Prowess wired as a TriggeredAbility when an effects service is
///     supplied; not wired on the single-arg shape-only path.
///   - Damage→-1/-1 counters replacement rewrites noncombat damage from a
///     source the controller controls to an opponent's creature into
///     counter placement + zero damage.
///   - Combat damage (Creature source) is NOT redirected.
///   - Damage to a player is NOT redirected.
///   - Damage to the controller's own creature is NOT redirected.
///   - Damage from an opponent's source is NOT redirected.
///   - Soul-Scar Mage leaving the battlefield disables the replacement.
/// </summary>
[Trait("Color", "R")]
public class SoulScarMageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SoulScarMage_Identity()
    {
        var c = SoulScarMageFactory.Create(_alice);

        c.Name.Should().Be("Soul-Scar Mage");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Prowess
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleArg_ShapeOnly_DoesNotWireProwess()
    {
        var c = SoulScarMageFactory.Create(_alice);

        // Shape-only path: no Prowess trigger attached (mirrors
        // MonasteryMentor's shape-only posture).
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "single-arg dispatcher path is shape-only — no Prowess trigger");
    }

    [Fact]
    public void WithEffectsService_AttachesProwessTrigger()
    {
        var effects = new ContinuousEffectsService();
        var c = SoulScarMageFactory.Create(_alice, effects, replacements: null, triggers: null);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().ContainSingle("Prowess wires when ContinuousEffectsService is supplied");
    }

    // -----------------------------------------------------------------------
    // Damage → -1/-1 counters replacement
    // -----------------------------------------------------------------------

    [Fact]
    public void NoncombatDamage_FromControllerSpell_ToOpponentsCreature_BecomesCounters()
    {
        var bus = new ReplacementBus();
        var mage = SoulScarMageOnBattlefield(_alice, bus);

        var bobBear = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        // Noncombat damage: source = Alice (player; the casting controller
        // shape Filter() uses in DamageSpellFactory).
        var intent = new DamageIntent(_alice, 2, TargetCreature: bobBear);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(0,
            "Soul-Scar Mage zeroes the damage and stamps -1/-1 counters instead");
        bobBear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(2);
        bobBear.Damage.Should().Be(0, "damage was replaced before application");
    }

    [Fact]
    public void CombatDamage_IsNotRedirected()
    {
        var bus = new ReplacementBus();
        var mage = SoulScarMageOnBattlefield(_alice, bus);

        var attacker = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var blocker = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        // Combat damage: source = Creature (per CombatFlow.DealDamageToCreature).
        var intent = new DamageIntent(attacker, 3, TargetCreature: blocker);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(3, "combat damage passes through unchanged");
        blocker.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void DamageToPlayer_IsNotRedirected()
    {
        var bus = new ReplacementBus();
        var mage = SoulScarMageOnBattlefield(_alice, bus);

        // Bolt-to-face: source = Alice (caster), target = Bob (player).
        var intent = new DamageIntent(_alice, 3, TargetPlayer: _bob);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(3,
            "Soul-Scar Mage only redirects damage to creatures opponents control");
    }

    [Fact]
    public void DamageToControllersOwnCreature_IsNotRedirected()
    {
        var bus = new ReplacementBus();
        var mage = SoulScarMageOnBattlefield(_alice, bus);

        // Alice's own bear takes 2 from her own Pyroclasm-style spell.
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var intent = new DamageIntent(_alice, 2, TargetCreature: aliceBear);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(2,
            "the rider only catches damage to creatures an OPPONENT controls");
        aliceBear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void DamageFromOpponentSource_IsNotRedirected()
    {
        var bus = new ReplacementBus();
        var mage = SoulScarMageOnBattlefield(_alice, bus);

        // Bob casts a damage spell at his own bear (or any of Alice's
        // creatures — the source-controller check fails either way). The
        // "source you control" clause is keyed to Soul-Scar Mage's
        // controller (Alice); Bob-sourced damage doesn't satisfy it.
        var bobBear = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        // Bob → Alice's bear (Alice's "creature an opponent controls" from
        // Bob's perspective, but Soul-Scar Mage's controller is Alice — so
        // the source-controller gate fails).
        var intent = new DamageIntent(_bob, 2, TargetCreature: aliceBear);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(2,
            "the rider only catches damage from sources Soul-Scar Mage's controller controls");
        aliceBear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void Replacement_DoesNotApply_WhenSoulScarMageLeavesBattlefield()
    {
        var bus = new ReplacementBus();
        var mage = SoulScarMageOnBattlefield(_alice, bus);

        var bobBear = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        // Sanity — replacement active.
        var first = bus.Apply(new DamageIntent(_alice, 1, TargetCreature: bobBear));
        first!.Amount.Should().Be(0);
        bobBear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);

        // Soul-Scar Mage leaves the battlefield (e.g., dies, gets bounced).
        // The replacement self-gates on Source.Zone == Battlefield, so it
        // no longer applies even though it remains registered on the bus
        // (CR 614.6 — no LTB unregister needed, same pattern as
        // PlagueEngineer's LordStaticEffect).
        _alice.Zones.Battlefield.RemoveCard(mage);
        mage.SetZone(ZoneType.Graveyard);

        var second = bus.Apply(new DamageIntent(_alice, 2, TargetCreature: bobBear));
        second!.Amount.Should().Be(2,
            "with Soul-Scar Mage off the battlefield the rider no longer applies");
        bobBear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "the prior counter sticks; no new counters from the post-LTB intent");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SoulScarMageOnBattlefield(Player owner, ReplacementBus bus)
    {
        var effects = new ContinuousEffectsService();
        var mage = SoulScarMageFactory.Create(owner, effects, bus, triggers: null);
        owner.Zones.Battlefield.AddCard(mage);
        mage.SetZone(ZoneType.Battlefield);
        return mage;
    }

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
