using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Pashalik Mons (Mercadian Masques, {3}{R}{R}, Legendary
/// Creature — Goblin Warrior 3/3).
///
/// Oracle text (Scryfall, verified):
///   "Whenever Pashalik Mons or another Goblin you control dies, Pashalik
///    Mons deals 1 damage to any target.
///    {3}{R}, Sacrifice a Goblin: Create two 1/1 red Goblin creature
///    tokens."
///
/// Covers:
/// - Card identity (Legendary supertype + Goblin + Warrior subtypes, 3/3,
///   mana cost).
/// - NamedCardFactory dispatch.
/// - Dies-trigger shape (any-target, exactly 1) and predicate:
///     fires on a Goblin YOU control moving Battlefield → Graveyard,
///     ignores non-Goblins, opponent Goblins, and non-death moves.
/// - Dies-trigger resolution deals 1 damage to the chosen target; no-op
///   when no target chosen (defensive shape guard).
/// - Activated ability shape — {3}{R} mana cost + Sacrifice-a-Goblin cost.
/// - Activated ability resolution creates exactly two 1/1 red Goblin
///   tokens.
/// </summary>
public class PashalikMonsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeGoblin(Player owner, string name = "Mogg Fanatic")
    {
        var c = new Creature(name, "{R}", 1, 1, subtypes: new[] { CardSubtype.Goblin });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PashalikMons_Identity()
    {
        var c = PashalikMonsFactory.Create(_alice);

        c.Name.Should().Be("Pashalik Mons");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Pashalik Mons is legendary");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(PashalikMonsFactory.Power);
        c.BaseToughness.Should().Be(PashalikMonsFactory.Toughness);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PashalikMons_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Pashalik Mons", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Pashalik Mons");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Dies-trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PashalikMons_DiesTrigger_HasSingleAnyTargetRequest()
    {
        var c = PashalikMonsFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Dies-trigger predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void PashalikMons_GoblinYouControlDies_TriggerFires()
    {
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var gob = new Creature("Mogg Fanatic", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        gob.SetOwner(_alice);
        gob.SetController(_alice);

        var moveEvent = new CardMovedEvent(gob, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeTrue();
    }

    [Fact]
    public void PashalikMons_SelfDies_TriggerFires()
    {
        // CR 603.6c — Pashalik Mons' own death triggers its ability (the
        // printed "Pashalik Mons or another Goblin" wording collapses to
        // "a Goblin you control" because Mons is itself a Goblin).
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var moveEvent = new CardMovedEvent(mons, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeTrue("Mons' own death is a Goblin you control dying");
    }

    [Fact]
    public void PashalikMons_NonGoblinDies_DoesNotFire()
    {
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var moveEvent = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "the trigger only fires for Goblins");
    }

    [Fact]
    public void PashalikMons_OpponentGoblinDies_DoesNotFire()
    {
        // CR 109.5 — "Goblin you control"; an opponent's Goblin dying
        // does not satisfy the trigger.
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var bobGob = new Creature("Bob's Goblin", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        bobGob.SetOwner(_bob);
        bobGob.SetController(_bob);

        var moveEvent = new CardMovedEvent(bobGob, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "CR 109.5 — only Goblins YOU control trigger the ability");
    }

    [Fact]
    public void PashalikMons_GoblinLeavesToExile_DoesNotFire()
    {
        // "Dies" = Battlefield → Graveyard (CR 700.4); a Goblin being
        // exiled does not count.
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var gob = new Creature("Mogg Fanatic", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        gob.SetOwner(_alice);
        gob.SetController(_alice);

        var moveEvent = new CardMovedEvent(gob, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Dies-trigger resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void PashalikMons_OnResolution_DealsOneDamageToChosenTarget()
    {
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in trigger.Effects) e.Execute();

        target.Damage.Should().Be(1, "Pashalik Mons deals 1 damage to any target");
    }

    [Fact]
    public void PashalikMons_OnResolution_NoTargetChosen_IsNoOp()
    {
        var mons = PashalikMonsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var trigger = mons.Abilities.OfType<TriggeredAbility>().Single();
        // No SetChosenTargets call.

        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PashalikMons_ActivatedAbility_HasManaAndSacrificeCost()
    {
        var mons = PashalikMonsFactory.Create(_alice);

        var ability = mons.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Cost.Equals(Majik.Core.ValueObjects.ManaCost.Parse("{3}{R}")),
            "the activated ability costs {3}{R} plus a sacrifice");
        ability.Costs.OfType<SacrificeAGoblinCost>().Should().ContainSingle(
            "the activated ability requires sacrificing a Goblin");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void PashalikMons_ActivatedAbility_CreatesTwoGoblinTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var mons = PashalikMonsFactory.Create(_alice, zones);
        _alice.Zones.Battlefield.AddCard(mons);
        mons.SetZone(ZoneType.Battlefield);

        var ability = mons.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        var spawned = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, mons))
            .ToList();

        spawned.Should().HaveCount(2, "the ability creates two 1/1 red Goblin tokens");
        spawned.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Goblin");
            t.BasePower.Should().Be(PashalikMonsFactory.TokenPower);
            t.BaseToughness.Should().Be(PashalikMonsFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        });
    }
}
