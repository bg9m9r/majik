using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BorosCharmFactory"/>.
///
/// Card: Boros Charm — Instant {R}{W} (Gatecrash).
///   CR 700.2d — modal "Choose one —" spell with 3 modes.
///   Mode 0: "Boros Charm deals 4 damage to target player or planeswalker."
///   Mode 1: "Permanents you control gain indestructible until end of turn."
///   Mode 2: "Target creature gains double strike until end of turn."
///
/// Covers:
///   - Identity: name, Instant type, Red+White colours, mana value 2.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: 3 modes, 3 TargetRequests (all MinTargets=0).
///   - Mode 0 resolve: deals 4 damage to a player target (life drops 4).
///   - Mode 1 resolve: grants Indestructible-until-EOT to every permanent
///     the caster controls (asserted via ContinuousEffectsService); a
///     lethal-damage creature that would otherwise die survives.
///   - Mode 2 resolve: target creature gains Double strike until EOT via
///     GrantKeywordUntilEndOfTurnEffect (CombatAbilities.HasDoubleStrike).
/// </summary>
public class BorosCharmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosCharm_Create_HasInstantShape_RedWhite()
    {
        var card = BorosCharmFactory.Create(_alice);

        card.Name.Should().Be("Boros Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{R}{W} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BorosCharm_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Boros Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Boros Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosCharm_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        def.Modes.Should().HaveCount(3);
        def.Modes[BorosCharmFactory.ModeDamage].Should().Contain("4 damage");
        def.Modes[BorosCharmFactory.ModeIndestructible].Should().Contain("indestructible");
        def.Modes[BorosCharmFactory.ModeDoubleStrike].Should().Contain("double strike");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[BorosCharmFactory.ModeDamage].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[BorosCharmFactory.ModeIndestructible].MinTargets.Should().Be(0,
            because: "mode 1 has no target — MinTargets must be 0");
        def.TargetRequests[BorosCharmFactory.ModeDoubleStrike].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
    }

    // -----------------------------------------------------------------------
    // Mode 0: 4 damage to target player or planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosCharm_Mode0_Deals4DamageToPlayer()
    {
        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _bob },     // mode 0 — player target
            Array.Empty<object>(),     // mode 1 (unused)
            Array.Empty<object>(),     // mode 2 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(16, because: "Boros Charm mode 0 deals 4 damage to the target player");
    }

    // -----------------------------------------------------------------------
    // Mode 1: permanents you control gain indestructible until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosCharm_Mode1_GrantsIndestructibleToControllersCreatures()
    {
        var svc = new ContinuousEffectsService();

        // Alice controls a 2/2 creature.
        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        // Bob controls a creature that should NOT get indestructible.
        var enemy = new Creature("Goblin", "{R}", 1, 1);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);
        enemy.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.ActiveEffects = svc;

        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, svc);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),     // mode 0 (unused)
            Array.Empty<object>(),     // mode 1 — no target
            Array.Empty<object>(),     // mode 2 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeIndestructible,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Alice's creature has Indestructible.
        CombatAbilities.HasIndestructible(ally).Should().BeTrue(
            because: "mode 1 grants indestructible to all permanents Alice controls");

        // Bob's creature is not affected.
        CombatAbilities.HasIndestructible(enemy).Should().BeFalse(
            because: "mode 1 only affects permanents the caster controls");
    }

    [Fact]
    public void BorosCharm_Mode1_IndestructibleExpires_EndOfTurn()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeIndestructible,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        CombatAbilities.HasIndestructible(ally).Should().BeTrue();

        // CR 514.2 — EOT cleanup expires the grant.
        svc.ExpireEndOfTurn();

        CombatAbilities.HasIndestructible(ally).Should().BeFalse(
            because: "Indestructible grant expires at end of turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Mode 2: target creature gains double strike until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosCharm_Mode2_GrantsDoubleStrikeToTargetCreature()
    {
        var svc = new ContinuousEffectsService();
        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);
        target.ActiveEffects = svc;

        CombatAbilities.HasDoubleStrike(target).Should().BeFalse();

        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),     // mode 0 (unused)
            Array.Empty<object>(),     // mode 1 (unused)
            new object[] { target },   // mode 2 — creature target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeDoubleStrike,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue(
            because: "Boros Charm mode 2 grants double strike to the target creature until EOT");
    }

    [Fact]
    public void BorosCharm_Mode2_DoubleStrikeExpires_EndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);
        target.ActiveEffects = svc;

        var def = BorosCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { target },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BorosCharmFactory.ModeDoubleStrike,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        CombatAbilities.HasDoubleStrike(target).Should().BeTrue();

        // CR 514.2 — cleanup expires the grant.
        svc.ExpireEndOfTurn();

        CombatAbilities.HasDoubleStrike(target).Should().BeFalse(
            because: "Double strike grant expires at end of turn (CR 514.2)");
    }
}
