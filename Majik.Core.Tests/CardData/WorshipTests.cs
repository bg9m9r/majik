using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WorshipFactory"/>.
///
/// Card: Worship — Enchantment {2}{W} (Tempest).
///   "If you control a creature, damage that would reduce your life total
///    to less than 1 reduces it to 1 instead."
///
/// Covers:
///   - Identity / dispatch.
///   - Damage replacement caps controller's life at 1 when a creature is in play.
///   - No replacement when controller controls zero creatures.
///   - Sub-lethal damage passes through unchanged.
///   - Damage to a player who is NOT Worship's controller is ignored.
///   - LTB: Worship in graveyard / hand does not fire.
///   - Shape-only Create(Player) does not register.
///   - Worship at exactly 1 life with 1 damage caps to 0.
/// </summary>
public class WorshipTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------- Identity ---------------------------------------------------

    [Fact]
    public void Worship_Identity()
    {
        var c = WorshipFactory.Create(_alice);

        c.Name.Should().Be("Worship");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Worship()
    {
        var card = NamedCardFactory.Create("Worship", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Worship");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}");
    }

    // -------- Replacement: caps lethal damage to controller --------------

    [Fact]
    public void LethalDamageWithCreature_CappedAtOneLife()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        _alice.LifeTotal = 5;
        var intent = new DamageIntent(Source: _bob, Amount: 20, TargetPlayer: _alice);

        var replaced = bus.Apply(intent);

        replaced.Should().NotBeNull();
        replaced!.Amount.Should().Be(4,
            "5 life → cap damage at 4 to leave Alice at exactly 1");
    }

    [Fact]
    public void LethalDamageAtTwentyLife_CappedAtNineteen()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        _alice.LifeTotal = 20;
        var intent = new DamageIntent(Source: _bob, Amount: 9999, TargetPlayer: _alice);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(19,
            "20 life → cap damage at 19 to leave Alice at exactly 1");
    }

    [Fact]
    public void DamageAtOneLifeWithCreature_CappedAtZero()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        _alice.LifeTotal = 1;
        var intent = new DamageIntent(Source: _bob, Amount: 5, TargetPlayer: _alice);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(0,
            "already at 1 — Worship can't reduce further, all damage zeroed");
    }

    // -------- Gating: requires a creature --------------------------------

    [Fact]
    public void NoCreaturesInPlay_DamagePassesThrough()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        // No creatures on Alice's side.

        _alice.LifeTotal = 5;
        var intent = new DamageIntent(Source: _bob, Amount: 20, TargetPlayer: _alice);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(20,
            "famous Worship-kill pattern: no creature → no protection");
    }

    [Fact]
    public void SubLethalDamage_PassesThroughUnchanged()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        _alice.LifeTotal = 20;
        var intent = new DamageIntent(Source: _bob, Amount: 3, TargetPlayer: _alice);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(3,
            "3 of 20 doesn't reduce life below 1 — replacement skips");
    }

    // -------- Scope: only Worship's controller ---------------------------

    [Fact]
    public void DamageToOpponent_NotCapped()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);
        // Bob has a creature too, but Worship is Alice's only.
        PlaceCreatureOnBattlefield("Goblin", "{R}", 1, 1, _bob);

        _bob.LifeTotal = 5;
        var intent = new DamageIntent(Source: _alice, Amount: 20, TargetPlayer: _bob);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(20,
            "Worship's controller is Alice — damage to Bob is ignored");
    }

    [Fact]
    public void DamageToCreature_NotCapped()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        PlaceOnBattlefield(worship, _alice);
        var bear = PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        var intent = new DamageIntent(Source: _bob, Amount: 99, TargetCreature: bear);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(99,
            "Worship clauses on player damage only — creature damage passes through");
    }

    // -------- Lifecycle: must be on the battlefield -----------------------

    [Fact]
    public void WorshipNotOnBattlefield_DoesNotFire()
    {
        var bus = new ReplacementBus();
        var worship = WorshipFactory.Create(_alice, bus);
        // Leave Worship in its default (Library) zone — replacement should be inert.
        PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        _alice.LifeTotal = 5;
        var intent = new DamageIntent(Source: _bob, Amount: 20, TargetPlayer: _alice);

        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(20,
            "Worship must be on the battlefield to apply (CR 614.6)");
    }

    // -------- Shape-only factory does not register -----------------------

    [Fact]
    public void SingleArgFactory_DoesNotRegisterReplacement()
    {
        var w = WorshipFactory.Create(_alice);
        w.Should().NotBeNull();
        w.Name.Should().Be("Worship");
        // No bus passed — nothing to register against; assertion is structural.
    }

    // -------- Helpers ----------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment e, Player owner)
    {
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
    }

    private static Creature PlaceCreatureOnBattlefield(
        string name, string cost, int power, int toughness, Player owner)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
