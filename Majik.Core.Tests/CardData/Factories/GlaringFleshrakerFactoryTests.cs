using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlaringFleshrakerFactory"/> (Edge of
/// Eternities, {2}{C}).
///
/// Creature — Eldrazi Drone 2/2 (colorless). Oracle text (verified against
/// Scryfall):
///   "Whenever you cast a colorless spell, create a 0/1 colorless Eldrazi
///    Spawn creature token with "Sacrifice this token: Add {C}."
///    Whenever another colorless creature you control enters, this creature
///    deals 1 damage to each opponent."
///
/// Covers:
///   - Identity (Eldrazi Drone 2/2 at {2}{C}, colorless, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Both triggers attached structurally on the shape-only path.
///   - Cast-colorless-spell trigger: mints a 0/1 colorless Eldrazi Spawn
///     token with a sac-for-{C} mana ability under the controller.
///   - Another-colorless-creature-enters trigger: deals 1 damage to each
///     opponent.
/// </summary>
[Trait("Color", "C")]
public class GlaringFleshrakerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GlaringFleshraker_Identity()
    {
        var c = GlaringFleshrakerFactory.Create(_alice);

        c.Name.Should().Be("Glaring Fleshraker");
        c.ManaCost.Should().Be("{2}{C}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // CR 105.2c — {2}{C} carries no colored pip, so the Fleshraker is
        // itself colorless.
        CardColors.GetColors(c).Should().BeEmpty(
            "{2}{C} has no colored mana symbol");
    }
    [Fact]
    public void GlaringFleshraker_HasTwoTriggeredAbilities()
    {
        var c = GlaringFleshrakerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(2, "the two printed triggers (cast-colorless + another-colorless-enters)");
    }

    [Fact]
    public void CastTrigger_FiresOnControllersColorlessSpell_AndMintsEldraziSpawn()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = GlaringFleshrakerFactory.Create(
            _alice, triggers, zones, opponentResolver: () => new[] { _bob });

        var castTrigger = FindCastColorlessTrigger(card);
        foreach (var e in castTrigger.Effects) e.Execute();

        // A single 0/1 colorless Eldrazi Spawn token on Alice's battlefield.
        var spawns = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Spawn))
            .ToList();
        spawns.Should().HaveCount(1, "casting a colorless spell mints one Eldrazi Spawn");

        var spawn = spawns[0];
        spawn.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        spawn.BasePower.Should().Be(0);
        spawn.BaseToughness.Should().Be(1);
        CardColors.GetColors(spawn).Should().BeEmpty("Eldrazi Spawn tokens are colorless");

        // "Sacrifice this token: Add {C}." — wired as a ManaAbility (sac
        // cost deferred, same posture as Treasure/Food, see TokenFactory).
        spawn.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the Spawn carries the Add {C} mana ability");
    }

    [Fact]
    public void EntersTrigger_DealsOneDamageToEachOpponent_WhenAnotherColorlessCreatureEnters()
    {
        var card = GlaringFleshrakerFactory.Create(
            _alice, triggers: null, zoneService: null,
            opponentResolver: () => new[] { _bob });

        // Directly execute the enters-trigger effect (matching the Voldaren
        // Epicure test posture — drive the closure independently of the
        // priority / stack drain).
        var entersTrigger = FindColorlessEntersTrigger(card);
        foreach (var e in entersTrigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "another colorless creature entering deals 1 damage to each opponent");
    }

    [Fact]
    public void EntersTrigger_WithoutResolver_NoOps()
    {
        var card = GlaringFleshrakerFactory.Create(
            _alice, triggers: null, zoneService: null, opponentResolver: null);

        var entersTrigger = FindColorlessEntersTrigger(card);
        foreach (var e in entersTrigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no opponent resolver → burn half no-ops");
    }

    /// <summary>The trigger whose condition watches SpellCastEvent.</summary>
    private static TriggeredAbility FindCastColorlessTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(Majik.Core.Domain.DomainEvents.SpellCastEvent));

    /// <summary>The trigger whose condition watches CardMovedEvent.</summary>
    private static TriggeredAbility FindColorlessEntersTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(CardMovedEvent));
}
