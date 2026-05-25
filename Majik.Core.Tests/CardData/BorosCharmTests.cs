using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Boros Charm, Gatecrash, {R}{W}, choose-one modal with three modes
/// (4 damage to player / mass indestructible / pump + double strike).
/// </summary>
public class BorosCharmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Create_HasInstantShape_BorosColors()
    {
        var c = BorosCharmFactory.Create(_alice);

        c.Name.Should().Be("Boros Charm");
        c.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.White);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBorosCharmShape()
    {
        var dispatched = NamedCardFactory.Create("Boros Charm", _alice);
        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Boros Charm");
    }

    [Fact]
    public void BuildDefinition_ExposesThreeModes_WithPerModeIntents()
    {
        var def = BorosCharmFactory.BuildDefinition(_alice, o => o);
        def.Modes.Should().HaveCount(3);
        def.ModeIntentsOrEmpty.Should().HaveCount(3);
        def.ModeIntentsOrEmpty[BorosCharmFactory.ModeDealDamage]
            .Should().Be(BotIntent.Burn);
        def.ModeIntentsOrEmpty[BorosCharmFactory.ModeIndestructible]
            .Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — 4 damage to target player
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_Deals4DamageToTargetPlayer()
    {
        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, chosenMode: BorosCharmFactory.ModeDealDamage);

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeDealDamage,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { _bob }, // mode 0 — player slot
                System.Array.Empty<object>(), // mode 2 — creature slot (unused)
            },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        _bob.LifeTotal.Should().Be(16, "Boros Charm mode 0 deals 4 damage");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Permanents you control gain indestructible until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_GrantsIndestructibleToAllControlledPermanents()
    {
        var continuous = new ContinuousEffectsService();

        var creature = new Creature("Goblin", "{R}", 1, 1)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(creature);

        var artifact = new Artifact("Mox", "{0}")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(artifact);

        var bobCreature = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _bob.Zones.Battlefield.AddCard(bobCreature);

        var def = BorosCharmFactory.BuildDefinition(
            _alice, o => o, continuous, chosenMode: BorosCharmFactory.ModeIndestructible);

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeIndestructible,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                System.Array.Empty<object>(),
                System.Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        CombatAbilities.HasIndestructible(creature).Should().BeTrue(
            "Alice's creature is granted Indestructible");
        artifact.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Indestructible", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Alice's non-creature permanent also gets the marker");
        CombatAbilities.HasIndestructible(bobCreature).Should().BeFalse(
            "Bob's creature is NOT granted — Boros Charm scopes to 'permanents you control'");

        // CR 514.2 — Indestructible expires at end of turn.
        continuous.ExpireEndOfTurn();

        CombatAbilities.HasIndestructible(creature).Should().BeFalse(
            "creature loses Indestructible after cleanup");
        artifact.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Indestructible", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("non-creature loses Indestructible after cleanup");
    }

    [Fact]
    public void Mode1_NoContinuousEffectsService_NoOps()
    {
        var creature = new Creature("Goblin", "{R}", 1, 1)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(creature);

        // Shape-only path — no service supplied; mode 1 silently no-ops.
        var def = BorosCharmFactory.BuildDefinition(
            _alice, o => o, continuousEffects: null, chosenMode: BorosCharmFactory.ModeIndestructible);

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeIndestructible,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                System.Array.Empty<object>(),
                System.Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        creature.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Indestructible", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("no service → no grant");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Target creature gets +1/+0 and gains double strike EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_PumpsAndGrantsDoubleStrike_ToTargetCreature()
    {
        var continuous = new ContinuousEffectsService();
        var attacker = new Creature("Boros Reckoner", "{R}{W}", 3, 3)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(attacker);

        var def = BorosCharmFactory.BuildDefinition(
            _alice, o => o, continuous, chosenMode: BorosCharmFactory.ModePumpDoubleStrike);

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModePumpDoubleStrike,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                System.Array.Empty<object>(),
                new object[] { attacker }, // mode 2 — creature slot at index 1
            },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        attacker.GetPower().Should().Be(4, "+1/+0 pump");
        attacker.GetToughness().Should().Be(3, "toughness unchanged");
        CombatAbilities.HasDoubleStrike(attacker).Should().BeTrue(
            "gains Double strike EOT");

        // CR 514.2 — both effects expire at EOT.
        continuous.ExpireEndOfTurn();
        attacker.GetPower().Should().Be(3);
        CombatAbilities.HasDoubleStrike(attacker).Should().BeFalse();
    }

    [Fact]
    public void DefaultMode_IsDealDamage_WhenNoneSupplied()
    {
        var def = BorosCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { _bob },
                System.Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        _bob.LifeTotal.Should().Be(16, "default mode (0) deals 4 damage");
    }
}
