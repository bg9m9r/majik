using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ThrabenCharmFactory"/>.
///
/// Thraben Charm ({1}{W}) — Instant. Oracle text:
///   "Choose one —
///     • Thraben Charm deals damage equal to twice the number of creatures
///       you control to target creature.
///     • Destroy target enchantment.
///     • Exile any number of target players' graveyards."
///
/// CR 700.2d — modal "Choose one —" spell with 3 modes.
/// MinTargets=0 per mode so unchosen modes don't gate the cast (CR 601.2c).
/// </summary>
[Trait("Color", "W")]
public class ThrabenCharmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ─── Identity + dispatcher ────────────────────────────────────────────────

    [Fact]
    public void ThrabenCharm_Create_HasInstantShape_White()
    {
        var card = ThrabenCharmFactory.Create(_alice);

        card.Name.Should().Be("Thraben Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{W} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThrabenCharm_PrintedManaCost_IsExact()
    {
        ThrabenCharmFactory.CardName.Should().Be("Thraben Charm");
        ThrabenCharmFactory.PrintedManaCost.Should().Be("{1}{W}");
    }
    [Fact]
    public void ThrabenCharm_BuildDefinition_HasThreeModes_ThreeTargetRequests()
    {
        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        def.Modes.Should().HaveCount(3);
        def.Modes[ThrabenCharmFactory.ModeDynamicDamage].Should().Contain("damage");
        def.Modes[ThrabenCharmFactory.ModeDestroyEnchantment].Should().Contain("enchantment");
        def.Modes[ThrabenCharmFactory.ModeExileGraveyard].Should().Contain("graveyard");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[ThrabenCharmFactory.ModeDynamicDamage].MinTargets.Should().Be(0);
        def.TargetRequests[ThrabenCharmFactory.ModeDestroyEnchantment].MinTargets.Should().Be(0);
        def.TargetRequests[ThrabenCharmFactory.ModeExileGraveyard].MinTargets.Should().Be(0);
    }

    // ─── Mode 0: dynamic damage to target creature ────────────────────────────

    [Fact]
    public void ThrabenCharm_Mode0_WithNCreatures_DealsDoubleNDamageToTargetCreature()
    {
        // Alice controls 3 creatures on her battlefield.
        SeedBattlefield<Creature>("Cat 1", "{W}", _alice, (n, c) => new Creature(n, c, 1, 1));
        SeedBattlefield<Creature>("Cat 2", "{W}", _alice, (n, c) => new Creature(n, c, 1, 1));
        SeedBattlefield<Creature>("Cat 3", "{W}", _alice, (n, c) => new Creature(n, c, 1, 1));

        // Bob's creature is the target.
        var bobCreature = new Creature("Dragon", "{5}{R}{R}", 5, 5);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        bobCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobCreature);

        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobCreature }, // mode 0 target
            Array.Empty<object>(),        // mode 1 (unused)
            Array.Empty<object>(),        // mode 2 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeDynamicDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Alice controls 3 creatures → 3 × 2 = 6 damage.
        bobCreature.Damage.Should().Be(6, because: "twice the number of creatures Alice controls (3) = 6");
    }

    [Fact]
    public void ThrabenCharm_Mode0_WithZeroCreatures_DealsZeroDamage_NoOp()
    {
        // Alice controls no creatures.
        var bobCreature = new Creature("Goblin", "{R}", 1, 1);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        bobCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobCreature);

        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobCreature },
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeDynamicDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        // 0 creatures → 0 × 2 = 0 — creature takes no damage.
        bobCreature.Damage.Should().Be(0, because: "0 creatures × 2 = 0 damage");
    }

    // ─── Mode 1: destroy target enchantment ──────────────────────────────────

    [Fact]
    public void ThrabenCharm_Mode1_DestroysTargetEnchantment()
    {
        // Bob controls an enchantment on the battlefield.
        var enchantment = new Enchantment("Banishing Light", "{2}{W}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),        // mode 0 (unused)
            new object[] { enchantment }, // mode 1 target
            Array.Empty<object>(),        // mode 2 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeDestroyEnchantment,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the target enchantment (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(enchantment);
    }

    [Fact]
    public void ThrabenCharm_Mode1_NonEnchantmentTarget_IsNoOp()
    {
        // Targeting a creature with mode 1 should be a no-op (CR 608.2b guard).
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { creature }, // illegal target type — guarded at resolution
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeDestroyEnchantment,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow();

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "non-enchantment targets are no-op for mode 1");
    }

    // ─── Mode 2: exile target player's graveyard ─────────────────────────────

    [Fact]
    public void ThrabenCharm_Mode2_ExilesTargetPlayersGraveyard()
    {
        // Bob has 3 cards in his graveyard.
        var card1 = SeedGraveyard<Instant>("Lightning Bolt", "{R}", _bob);
        var card2 = SeedGraveyard<Sorcery>("Thoughtseize", "{B}", _bob);
        var card3 = SeedGraveyard<Instant>("Path to Exile", "{W}", _bob);

        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),   // mode 0 (unused)
            Array.Empty<object>(),   // mode 1 (unused)
            new object[] { _bob },   // mode 2 target — exile Bob's graveyard
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeExileGraveyard,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            because: "mode 2 exiles all cards from the target player's graveyard");
        _bob.Zones.Exile.GetCards().Should().HaveCount(3,
            because: "all 3 graveyard cards are moved to exile");
    }

    [Fact]
    public void ThrabenCharm_Mode2_EmptyGraveyard_IsCleanNoOp()
    {
        // Bob's graveyard is empty.
        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { _bob },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeExileGraveyard,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ThrabenCharm_Mode2_AliceGraveyard_Untouched_WhenBobTargeted()
    {
        // Alice has cards in her graveyard; they should NOT be exiled.
        var aliceCard = SeedGraveyard<Instant>("Brainstorm", "{U}", _alice);

        // Bob has one card in his graveyard.
        var bobCard = SeedGraveyard<Instant>("Lightning Bolt", "{R}", _bob);

        var def = ThrabenCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { _bob },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ThrabenCharmFactory.ModeExileGraveyard,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCard,
            because: "Alice's graveyard is untouched when Bob is targeted");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static T SeedGraveyard<T>(string name, string cost, Player owner)
        where T : Card
    {
        T card = typeof(T) == typeof(Instant) ? (T)(Card)new Instant(name, cost)
               : typeof(T) == typeof(Sorcery) ? (T)(Card)new Sorcery(name, cost)
               : throw new ArgumentException($"Unsupported card type {typeof(T).Name}");
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static void SeedBattlefield<T>(
        string name, string cost, Player owner,
        Func<string, string, T> ctor)
        where T : Card
    {
        var card = ctor(name, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
    }
}
