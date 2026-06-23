using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CathedralOfWarFactory"/> (Gatecrash).
///
/// Oracle text:
///   "This land enters tapped.
///    Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    {T}: Add {C}."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, Land type, {T}: Add {C} mana ability).
/// - Enters-tapped (CR 614.1c) via the registered EntersTappedReplacement.
/// - Exalted keyword marker (CR 702.90).
/// - Exalted trigger pumps the solo attacker +1/+1 EOT (CR 702.90b).
/// - Two controlled attackers — no pump (CR 702.90b "attacks alone").
/// - Single-arg dispatcher path is a no-op pump (no attackers source).
/// </summary>
[Trait("Color", "C")]
public class CathedralOfWarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetExaltedTrigger(Land l) =>
        l.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void CathedralOfWar_Identity()
    {
        var land = CathedralOfWarFactory.Create(_alice);

        land.Name.Should().Be("Cathedral of War");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        // {T}: Add {C} — the single colourless mana ability.
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "Cathedral of War has exactly one mana ability: {T}: Add {C}.");
        // ManaCost.ToString() omits braces — "{C}" -> "C".
        manaAbilities[0].ManaGenerated?.ToString().Should().Be("C",
            "Cathedral of War taps for one colourless mana.");
    }

    // ── Enters tapped (CR 614.1c) ────────────────────────────────────────

    [Fact]
    public void CathedralOfWar_EntersTapped()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var land = CathedralOfWarFactory.Create(
            _alice, replacements: rep, attackingCreaturesSource: null);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        land.IsTapped.Should().BeTrue(
            "CR 614.1c — \"This land enters tapped.\"");
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Exalted keyword marker ────────────────────────────────────────────

    [Fact]
    public void CathedralOfWar_HasExaltedKeywordMarker()
    {
        var land = CathedralOfWarFactory.Create(_alice);

        var exalted = land.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => k.Keyword == "Exalted");

        exalted.Should().NotBeNull("Exalted keyword marker must be present (CR 702.90).");
    }

    // ── Exalted trigger — single attacker ────────────────────────────────

    [Fact]
    public void CathedralOfWar_Exalted_SoloAttacker_GetsPumped()
    {
        // CR 702.90b — attacker attacks alone; should get +1/+1 EOT.
        var svc = new ContinuousEffectsService();

        var attacker = MakeCreature(_alice, "Grizzly Bears");
        attacker.ActiveEffects = svc;

        var attackers = new List<Creature> { attacker };

        var land = CathedralOfWarFactory.Create(
            _alice,
            replacements: null,
            attackingCreaturesSource: () => attackers);
        land.SetZone(ZoneType.Battlefield);

        var trigger = GetExaltedTrigger(land);
        trigger.IsTriggered(new CreatureAttacksEvent(attacker, _bob)).Should().BeTrue(
            "the exalted trigger fires whenever any creature Alice controls attacks.");

        foreach (var e in trigger.Effects) e.Execute();

        attacker.GetPower().Should().Be(2 + 1,
            "Exalted gives the solo attacker +1/+1 until end of turn.");
        attacker.GetToughness().Should().Be(2 + 1);
    }

    // ── Exalted trigger — multiple attackers ─────────────────────────────

    [Fact]
    public void CathedralOfWar_Exalted_TwoAttackers_NoPump()
    {
        // CR 702.90b — "attacks alone" requires no other controlled attackers.
        var svc = new ContinuousEffectsService();

        var attacker1 = MakeCreature(_alice, "Bear Alpha");
        var attacker2 = MakeCreature(_alice, "Bear Beta");
        attacker1.ActiveEffects = svc;
        attacker2.ActiveEffects = svc;

        var attackers = new List<Creature> { attacker1, attacker2 };

        var land = CathedralOfWarFactory.Create(
            _alice,
            replacements: null,
            attackingCreaturesSource: () => attackers);
        land.SetZone(ZoneType.Battlefield);

        var trigger = GetExaltedTrigger(land);
        trigger.IsTriggered(new CreatureAttacksEvent(attacker1, _bob)).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        attacker1.GetPower().Should().Be(2,
            "two attackers means the creature didn't attack alone — no pump.");
        attacker1.GetToughness().Should().Be(2);
        attacker2.GetPower().Should().Be(2);
        attacker2.GetToughness().Should().Be(2);
    }

    // ── Single-arg dispatcher path ────────────────────────────────────────

    [Fact]
    public void CathedralOfWar_SingleArgPath_NoOpPumpBody()
    {
        // The single-arg path doesn't wire an attackers source — the pump
        // body short-circuits and attackers remain at base P/T.
        var svc = new ContinuousEffectsService();

        var attacker = MakeCreature(_alice, "Grizzly Bears");
        attacker.ActiveEffects = svc;

        var land = CathedralOfWarFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var trigger = GetExaltedTrigger(land);
        foreach (var e in trigger.Effects) e.Execute();

        attacker.GetPower().Should().Be(2,
            "no attackers source — pump body is a no-op (shape-only path).");
        attacker.GetToughness().Should().Be(2);
    }
}
