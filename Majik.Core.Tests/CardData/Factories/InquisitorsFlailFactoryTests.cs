using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InquisitorsFlailFactory"/>.
///
/// Card: Inquisitor's Flail — Artifact — Equipment {2} (Innistrad).
///   "If equipped creature would deal combat damage, it deals double that
///    damage instead."
///   "If a source would deal combat damage to equipped creature, it deals
///    double that damage instead."
///   "Equip {2}."
///
/// Covers:
///   - Identity (name, type, subtypes, mana cost, owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Equip {2} cost shape.
///   - Single-arg shape-only path: no replacements registered.
///   - Source-side combat-damage doubling (equipped creature attacks).
///   - Target-side combat-damage doubling (equipped creature blocked /
///     attacked).
///   - Non-combat damage is NOT doubled (ping ability, Lightning Bolt-
///     style targeted spell).
///   - Detach (Unattach) suspends doubling without explicit
///     deregistration.
/// </summary>
public class InquisitorsFlailFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity + dispatch
    // ---------------------------------------------------------------------

    [Fact]
    public void InquisitorsFlail_Identity()
    {
        var c = InquisitorsFlailFactory.Create(_alice);

        c.Name.Should().Be("Inquisitor's Flail");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Inquisitor's Flail is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InquisitorsFlail_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Inquisitor's Flail", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Inquisitor's Flail");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    [Fact]
    public void InquisitorsFlail_EquipAbility_HasGenericTwoCost()
    {
        var c = InquisitorsFlailFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void InquisitorsFlail_SingleArgPath_RegistersNoReplacements()
    {
        // Shape-only path: no ReplacementBus, no damage-doubling
        // replacements — only the structural shape + Equip {2}.
        var bus = new ReplacementBus();
        var c = InquisitorsFlailFactory.Create(_alice);

        c.Abilities.OfType<EquipActivatedAbility>().Should().HaveCount(1);
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Flail has no printed triggered ability");
        c.Abilities.OfType<StaticAbility>().Should().BeEmpty(
            "the damage-doubling effects live on the ReplacementBus, not as StaticAbility");

        // A bus that never saw the Flail should not double anything.
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var intent = new DamageIntent(bear, 3, TargetPlayer: _bob) { IsCombatDamage = true };
        bus.Apply(intent)!.Amount.Should().Be(3);
    }

    // ---------------------------------------------------------------------
    // Source-side combat-damage doubling
    // ---------------------------------------------------------------------

    [Fact]
    public void EquippedCreature_CombatDamage_ToPlayer_IsDoubled()
    {
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        flail.AttachTo(bear);

        var intent = new DamageIntent(bear, 3, TargetPlayer: _bob) { IsCombatDamage = true };

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(6,
            "equipped creature's 3 combat damage is doubled to 6 (source-side)");
    }

    [Fact]
    public void EquippedCreature_CombatDamage_ToCreature_IsDoubled()
    {
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        flail.AttachTo(bear);

        var enemy = NewCreatureOnBattlefield(_bob, "Hill Giant", "{3}{R}", 3, 3);
        var intent = new DamageIntent(bear, 2, TargetCreature: enemy) { IsCombatDamage = true };

        var result = bus.Apply(intent);

        result!.Amount.Should().Be(4);
    }

    [Fact]
    public void EquippedCreature_NonCombatDamage_IsNotDoubled()
    {
        // Pinger ability of equipped creature — e.g. {T}: deals 1 damage.
        // IsCombatDamage = false on the intent so Flail's source-side
        // gate does not fire.
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);
        var pinger = NewCreatureOnBattlefield(_alice, "Prodigal Sorcerer", "{2}{U}", 1, 1);
        flail.AttachTo(pinger);

        var intent = new DamageIntent(pinger, 1, TargetPlayer: _bob);
        // IsCombatDamage defaults to false.

        var result = bus.Apply(intent);

        result!.Amount.Should().Be(1,
            "ping damage is non-combat — Inquisitor's Flail only doubles combat damage");
    }

    // ---------------------------------------------------------------------
    // Target-side combat-damage doubling
    // ---------------------------------------------------------------------

    [Fact]
    public void EquippedCreature_TakesCombatDamage_IsDoubled()
    {
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        flail.AttachTo(bear);

        var attacker = NewCreatureOnBattlefield(_bob, "Hill Giant", "{3}{R}", 3, 3);
        var intent = new DamageIntent(attacker, 2, TargetCreature: bear) { IsCombatDamage = true };

        var result = bus.Apply(intent);

        result!.Amount.Should().Be(4,
            "incoming combat damage to equipped creature doubles 2 -> 4 (target-side)");
    }

    [Fact]
    public void EquippedCreature_TakesNonCombatDamage_IsNotDoubled()
    {
        // Lightning Bolt-style targeted damage: source = player (caster),
        // IsCombatDamage = false. Flail's target-side gate does not fire.
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        flail.AttachTo(bear);

        var intent = new DamageIntent(_bob, 3, TargetCreature: bear);
        // IsCombatDamage defaults to false — non-combat spell damage.

        var result = bus.Apply(intent);

        result!.Amount.Should().Be(3,
            "Lightning Bolt's targeted damage is non-combat — no doubling");
    }

    // ---------------------------------------------------------------------
    // Attachment lifecycle
    // ---------------------------------------------------------------------

    [Fact]
    public void Unattach_DisablesDoubling()
    {
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        flail.AttachTo(bear);

        // Sanity: doubles while attached.
        bus.Apply(new DamageIntent(bear, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(6);

        flail.Unattach();

        bus.Apply(new DamageIntent(bear, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(3,
                "with no AttachedTo bearer, Flail's gates short-circuit");
    }

    [Fact]
    public void Unequipped_DoesNotDoubleAnyone()
    {
        // Flail is on the battlefield but attached to nothing — neither
        // gate should fire for any creature.
        var bus = new ReplacementBus();
        var flail = FlailOnBattlefield(_alice, bus);

        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var enemy = NewCreatureOnBattlefield(_bob, "Hill Giant", "{3}{R}", 3, 3);

        // Source-side: bear attacks bob — no doubling.
        bus.Apply(new DamageIntent(bear, 2, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(2);

        // Target-side: enemy hits bear — no doubling.
        bus.Apply(new DamageIntent(enemy, 3, TargetCreature: bear) { IsCombatDamage = true })!
            .Amount.Should().Be(3);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Artifact FlailOnBattlefield(Player owner, ReplacementBus bus)
    {
        var flail = InquisitorsFlailFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(flail);
        flail.SetZone(ZoneType.Battlefield);
        return flail;
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
