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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="JeskaiCharmFactory"/>.
///
/// Card: Jeskai Charm — Instant {U}{R}{W} (Khans of Tarkir).
///   CR 700.2d — modal "Choose one —" spell with 3 modes:
///   Mode 0: "Put target creature on top of its owner's library."
///   Mode 1: "Jeskai Charm deals 4 damage to target opponent or planeswalker."
///   Mode 2: "Creatures you control get +1/+1 and gain lifelink until end of turn."
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring BantCharmTests /
/// BorosCharmFactoryTests.
/// </summary>
[Trait("Color", "M")]
public class JeskaiCharmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_JeskaiColors()
    {
        var card = JeskaiCharmFactory.Create(_alice);

        card.Name.Should().Be("Jeskai Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{U}{R}{W} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_ExposesThreeModes_WithPerModeIntents()
    {
        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        def.Modes.Should().HaveCount(3);
        def.Modes[JeskaiCharmFactory.ModeTopLibrary].Should().Contain("top");
        def.Modes[JeskaiCharmFactory.ModeDamage].Should().Contain("4 damage");
        def.Modes[JeskaiCharmFactory.ModePumpLifelink].Should().Contain("lifelink");

        def.ModeIntentsOrEmpty.Should().HaveCount(3);
        def.ModeIntentsOrEmpty[JeskaiCharmFactory.ModeTopLibrary].Should().Be(BotIntent.Bounce);
        def.ModeIntentsOrEmpty[JeskaiCharmFactory.ModeDamage].Should().Be(BotIntent.Burn);
        def.ModeIntentsOrEmpty[JeskaiCharmFactory.ModePumpLifelink].Should().Be(BotIntent.CombatTrick);

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[JeskaiCharmFactory.ModeTopLibrary].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[JeskaiCharmFactory.ModeDamage].MinTargets.Should().Be(0);
        def.TargetRequests[JeskaiCharmFactory.ModePumpLifelink].MinTargets.Should().Be(0,
            because: "mode 2 has no target — MinTargets must be 0");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — put target creature on top of its owner's library
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_TopLibrary_MovesCreatureToTopOfOwnersLibrary()
    {
        // Bob owns + controls the creature; its existing library has one card
        // so we can confirm the creature lands on the TOP (before it).
        var existing = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(existing);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBear },
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeTopLibrary,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobBear);

        var library = _bob.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(2);
        // Index 0 is the top; the bear must be on TOP (first).
        library[0].Should().BeSameAs(bobBear,
            because: "the creature is placed on top of its owner's library");
        library[1].Should().BeSameAs(existing, because: "the pre-existing card is now second");
    }

    [Fact]
    public void Mode0_TopLibrary_UsesOwnersLibrary_NotControllers()
    {
        // Alice controls a creature that Bob owns. It must return to BOB's
        // library (owner), not Alice's (controller). CR 109.5.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _alice };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBear },
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeTopLibrary,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Library.GetCards().Should().Contain(bobBear,
            because: "'its owner's library' = Bob's, even though Alice controlled it");
        _alice.Zones.Library.GetCards().Should().NotContain(bobBear);
    }

    [Fact]
    public void Mode0_TopLibrary_IgnoresNonCreatureTarget()
    {
        var bobBauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _bob, Controller = _bob };
        bobBauble.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBauble);

        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBauble }, // not a creature — CR 608.2b gate
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeTopLibrary,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBauble.Zone.Should().Be(ZoneType.Battlefield,
            because: "the top-of-library mode no-ops on a non-creature target");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — 4 damage to target opponent or planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_Deals4DamageToOpponent()
    {
        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _bob },     // opponent
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(16, because: "mode 1 deals 4 damage to the target opponent");
    }

    [Fact]
    public void Mode1_DoesNotTargetCaster()
    {
        // The caster is not an "opponent" (CR 102.1) — handing the caster as
        // the target must no-op (defence in depth past the candidate filter).
        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _alice },   // the caster — not a legal "opponent"
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.LifeTotal.Should().Be(20,
            because: "the caster is not an opponent and takes no damage");
    }

    [Fact]
    public void Mode1_DamagesPlaneswalkerLoyalty()
    {
        var pw = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3) { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { pw },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        pw.Loyalty.Should().Be(0, because: "4 damage removes 4 loyalty from a 3-loyalty planeswalker (floored)");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — creatures you control get +1/+1 and gain lifelink until EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_PumpAndLifelink_AffectsControllersCreaturesOnly()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var enemy = new Creature("Goblin", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        enemy.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.ActiveEffects = svc;

        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModePumpLifelink,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Alice's creature: +1/+1 and lifelink.
        var (allyPower, allyTough) = svc.ComputePowerToughness(ally);
        allyPower.Should().Be(3, because: "mode 2 gives the caster's creatures +1/+1");
        allyTough.Should().Be(3);
        CombatAbilities.HasLifelink(ally).Should().BeTrue(
            because: "mode 2 grants lifelink to the caster's creatures");

        // Bob's creature: unaffected.
        var (enemyPower, enemyTough) = svc.ComputePowerToughness(enemy);
        enemyPower.Should().Be(1, because: "mode 2 only affects creatures the caster controls");
        enemyTough.Should().Be(1);
        CombatAbilities.HasLifelink(enemy).Should().BeFalse();
    }

    [Fact]
    public void Mode2_PumpAndLifelink_ExpiresEndOfTurn()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModePumpLifelink,
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

        var (pumpedPower, _) = svc.ComputePowerToughness(ally);
        pumpedPower.Should().Be(3);
        CombatAbilities.HasLifelink(ally).Should().BeTrue();

        // CR 514.2 — EOT cleanup expires both grants.
        svc.ExpireEndOfTurn();

        var (basePower, baseTough) = svc.ComputePowerToughness(ally);
        basePower.Should().Be(2, because: "the +1/+1 grant expires at end of turn (CR 514.2)");
        baseTough.Should().Be(2);
        CombatAbilities.HasLifelink(ally).Should().BeFalse(
            because: "the lifelink grant expires at end of turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        var def = JeskaiCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: JeskaiCharmFactory.ModeTopLibrary,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                JeskaiCharmFactory.ModeTopLibrary,
                JeskaiCharmFactory.ModeDamage, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(JeskaiCharmFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
