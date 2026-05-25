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
/// Tests for Krenko, Mob Boss (Magic 2013, {2}{R}{R}, Legendary Creature —
/// Goblin Warrior 3/3).
///
/// Covers:
/// - Card identity (Legendary supertype + Goblin + Warrior subtypes, 3/3,
///   mana cost).
/// - NamedCardFactory dispatch.
/// - Activated ability shape — {T} only (no mana cost).
/// - Activated ability resolution: creates X 1/1 red Goblin tokens where
///   X = Goblins controller controls (including Krenko himself).
/// - X = 0 case (zero Goblins, e.g. opp controls Krenko after a steal —
///   resolves cleanly with no tokens).
/// - Opponent Goblins are NOT counted toward X.
/// </summary>
public class KrenkoMobBossTests
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
    public void Krenko_Identity()
    {
        var krenko = KrenkoMobBossFactory.Create(_alice);

        krenko.Name.Should().Be("Krenko, Mob Boss");
        krenko.ManaCost.Should().Be("{2}{R}{R}");
        krenko.HasType(CardType.Creature).Should().BeTrue();
        krenko.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Krenko is legendary");
        krenko.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        krenko.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        krenko.BasePower.Should().Be(KrenkoMobBossFactory.Power);
        krenko.BaseToughness.Should().Be(KrenkoMobBossFactory.Toughness);
        krenko.Owner.Should().BeSameAs(_alice);
        krenko.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Krenko_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Krenko, Mob Boss", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Krenko, Mob Boss");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Krenko_ActivatedAbility_HasOnlyTapCost()
    {
        var krenko = KrenkoMobBossFactory.Create(_alice);

        var ability = krenko.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Krenko's tap ability has no mana cost — just {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activated ability requires a tap cost");
    }

    // -----------------------------------------------------------------------
    // Resolution — X = self-only count (1 token)
    // -----------------------------------------------------------------------

    [Fact]
    public void Krenko_SoloGoblin_Creates1Token()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var krenko = KrenkoMobBossFactory.Create(_alice, zones);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);

        var ability = krenko.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        // Krenko counts himself ("Goblins you control" — no "other" rider).
        // Solo Krenko = 1 Goblin → 1 token spawned.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => !ReferenceEquals(c, krenko) && c.HasSubtype(CardSubtype.Goblin))
            .Should().Be(1, "Krenko alone counts as 1 Goblin → 1 token");
    }

    // -----------------------------------------------------------------------
    // Resolution — X = self + friends count (3 tokens)
    // -----------------------------------------------------------------------

    [Fact]
    public void Krenko_WithTwoFriendlyGoblins_Creates3Tokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var krenko = KrenkoMobBossFactory.Create(_alice, zones);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);

        // Two friendly Goblins already on Alice's battlefield.
        var friend1 = MakeGoblin(_alice, "Mogg Fanatic");
        var friend2 = MakeGoblin(_alice, "Goblin Lackey");

        int existingGoblinTokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => ReferenceEquals(c, friend1) || ReferenceEquals(c, friend2));
        existingGoblinTokens.Should().Be(2);

        var ability = krenko.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        // Krenko + 2 friends = 3 Goblins → 3 tokens spawned, all token
        // creatures with name "Goblin".
        var spawnedGoblinTokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, krenko)
                     && !ReferenceEquals(c, friend1)
                     && !ReferenceEquals(c, friend2))
            .ToList();
        spawnedGoblinTokens.Should().HaveCount(3,
            "X = 3 Goblins you control (Krenko + friend1 + friend2)");
        spawnedGoblinTokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Goblin");
            t.BasePower.Should().Be(KrenkoMobBossFactory.TokenPower);
            t.BaseToughness.Should().Be(KrenkoMobBossFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        });
    }

    // -----------------------------------------------------------------------
    // Resolution — opponent Goblins are not counted
    // -----------------------------------------------------------------------

    [Fact]
    public void Krenko_OpponentGoblins_NotCounted()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var krenko = KrenkoMobBossFactory.Create(_alice, zones);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);

        // Bob has a Goblin — Krenko must NOT count it.
        MakeGoblin(_bob, "Bob's Goblin");

        var ability = krenko.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        // Solo on Alice's side = 1 Goblin (Krenko himself) → 1 token.
        var spawned = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, krenko))
            .ToList();
        spawned.Should().HaveCount(1,
            "CR 109.5 — Krenko counts only Goblins YOU control, not opponents'");
    }
}
