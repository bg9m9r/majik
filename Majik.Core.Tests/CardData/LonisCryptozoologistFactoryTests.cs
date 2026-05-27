using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Lonis, Cryptozoologist (Streets of New Capenna Commander,
/// {G}{U}, Legendary Creature — Snake Elf Scout 2/2).
///
/// Covers (v1 — investigate trigger only; activated ability deferred):
///   - Card identity (name, types/supertype/subtypes, P/T, mana cost,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch returns the same shape.
///   - One triggered ability on the card.
///   - Non-token creature ETB under controller → one Clue token created.
///   - Token creature ETB → no investigate (oracle: "nontoken").
///   - Lonis's own ETB → no self-investigate ("another" gate).
///   - Opponent's creature ETB → no investigate (controller gate).
///   - Non-creature ETB (artifact, enchantment) → no investigate.
/// </summary>
public class LonisCryptozoologistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Lonis_Identity_LegendarySnakeElfScout22AtGU()
    {
        var l = LonisCryptozoologistFactory.Create(_alice);

        l.Name.Should().Be("Lonis, Cryptozoologist");
        l.ManaCost.Should().Be("{G}{U}");
        l.HasType(CardType.Creature).Should().BeTrue();
        l.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        l.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        l.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        l.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        l.BasePower.Should().Be(2);
        l.BaseToughness.Should().Be(2);
        l.Owner.Should().BeSameAs(_alice);
        l.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Lonis()
    {
        var card = NamedCardFactory.Create("Lonis, Cryptozoologist", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lonis, Cryptozoologist");
        card.ManaCost.Should().Be("{G}{U}");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Investigate trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void NonTokenCreatureEntersUnderController_CreatesClue()
    {
        var (zones, stack, triggers) = BuildEngine();

        var lonis = LonisCryptozoologistFactory.Create(_alice, triggers, zones);
        lonis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lonis);

        // Cast a regular (non-token) creature → ETB triggers investigate.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "nontoken creature ETB triggers Lonis");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(1);
        clues[0].IsToken.Should().BeTrue();
    }

    [Fact]
    public void CreatureTokenEnters_NoInvestigate()
    {
        var (zones, _, triggers) = BuildEngine();

        var lonis = LonisCryptozoologistFactory.Create(_alice, triggers, zones);
        lonis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lonis);

        // 1/1 creature token → fails "nontoken" gate.
        var spec = new TokenFactory.TokenSpec(
            Name: "Soldier",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Soldier },
            Colors: new[] { ManaColor.White });
        TokenFactory.CreateOnBattlefield(spec, _alice, zones);

        triggers.PendingCount.Should().Be(0, "creature tokens don't trigger Lonis (\"nontoken\" gate)");
    }

    [Fact]
    public void LonisOwnETB_NoSelfInvestigate()
    {
        // "Another" — Lonis's own ETB does NOT trigger her ability
        // (CR 109.2 / CR 603.6e). Simulate this by adding Lonis via
        // ZoneService and watching the ETB event flow.
        var (zones, _, triggers) = BuildEngine();

        var lonis = LonisCryptozoologistFactory.Create(_alice, triggers, zones);
        lonis.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(lonis);
        lonis.SetZone(ZoneType.Hand);

        // Move Lonis to the battlefield — her own ETB publishes a
        // CardMovedEvent but the trigger's "another" gate (Card != lonis)
        // suppresses it.
        zones.MoveCardTo(lonis, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0, "\"another\" excludes Lonis's own ETB");
    }

    [Fact]
    public void OpponentNonTokenCreatureEnters_NoInvestigate()
    {
        var (zones, _, triggers) = BuildEngine();

        var lonis = LonisCryptozoologistFactory.Create(_alice, triggers, zones);
        lonis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lonis);

        var bobBear = new Creature("Bob's Bear", "1G", 2, 2);
        bobBear.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Hand);
        zones.MoveCardTo(bobBear, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0, "opponent's creature ETB doesn't trigger \"you control\"");
    }

    [Fact]
    public void NonCreatureEnters_NoInvestigate()
    {
        var (zones, _, triggers) = BuildEngine();

        var lonis = LonisCryptozoologistFactory.Create(_alice, triggers, zones);
        lonis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lonis);

        // An artifact ETB under Alice — should NOT trigger (creature gate).
        var artifact = new Artifact("Mox Pearl", "0");
        artifact.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(artifact);
        artifact.SetZone(ZoneType.Hand);
        zones.MoveCardTo(artifact, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0, "non-creature permanents don't trigger Lonis");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
