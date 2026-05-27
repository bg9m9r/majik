using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MagmaSprayFactory"/> (Amonkhet / Scourge, {R}).
///
/// Card: Magma Spray — Instant {R} (Amonkhet).
///   "Magma Spray deals 2 damage to target creature. If that creature
///    would die this turn, exile it instead."
///
/// Covers:
///   - Identity ({R} Instant, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Spell-definition shape: 1..1 "target creature".
///   - Resolve deals 2 damage to the targeted creature.
///   - Exile rider: replacement registered on the <see cref="ReplacementBus"/>
///     rewrites the targeted creature's battlefield→graveyard move to exile
///     (CR 700.3 — "that creature" is scoped to the single damaged creature).
///   - Exile rider is NOT applied to a creature that was NOT targeted
///     (i.e., the rider is scoped to only the one targeted creature).
/// </summary>
public class MagmaSprayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MagmaSpray_Identity_InstantAtR()
    {
        var spray = MagmaSprayFactory.Create(_alice);

        spray.Name.Should().Be("Magma Spray");
        spray.HasType(CardType.Instant).Should().BeTrue();
        spray.ManaCost.ToString().Should().Be("{R}");
        spray.Owner.Should().BeSameAs(_alice);
        spray.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MagmaSpray_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Magma Spray", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Magma Spray");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
    }

    // -----------------------------------------------------------------------
    // Spell-definition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MagmaSpray_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = MagmaSprayFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolution — damage
    // -----------------------------------------------------------------------

    [Fact]
    public void MagmaSpray_Resolve_DealsTwoDamageToTargetCreature()
    {
        // Use a 2/4 creature so 2 damage is not lethal — verifies damage
        // marker is applied without concern for SBA wipe.
        var target = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 4);

        var def = MagmaSprayFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { target },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        target.Damage.Should().Be(2, "Magma Spray deals 2 damage to target creature");
    }

    // -----------------------------------------------------------------------
    // Exile rider — replacement bus
    // -----------------------------------------------------------------------

    [Fact]
    public void MagmaSpray_Resolve_WithBus_RegistersReplacement_ExilesTargetedCreatureDeath()
    {
        var bus = new ReplacementBus();

        var target = NewCreatureOnBattlefield(_bob, "Goblin Guide", "{R}", 2, 2);

        var def = MagmaSprayFactory.BuildSpellDefinition(resolver: x => x, replacements: bus);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { target },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        // The targeted creature's subsequent battlefield→graveyard move
        // (e.g., SBA-driven death from the 2 damage) must be rewritten to
        // battlefield→exile by the registered replacement (CR 700.3 —
        // "that creature" scopes the rider to the single targeted creature).
        var dyingIntent = new ZoneMoveIntent(
            Card: target,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: _bob);

        var result = bus.Apply(dyingIntent);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "the targeted creature's death is redirected to exile (Magma Spray rider)");
    }

    [Fact]
    public void MagmaSpray_ExileRider_DoesNotApplyToNonTargetedCreature()
    {
        var bus = new ReplacementBus();

        var target = NewCreatureOnBattlefield(_bob, "Goblin Guide", "{R}", 2, 2);
        var bystander = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var def = MagmaSprayFactory.BuildSpellDefinition(resolver: x => x, replacements: bus);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { target },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        // A creature NOT targeted by Magma Spray dies normally to the
        // graveyard — the rider is scoped to "that creature" only.
        var bystanderDying = new ZoneMoveIntent(
            Card: bystander,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: _bob);

        var result = bus.Apply(bystanderDying);
        result!.ToZone.Should().Be(ZoneType.Graveyard,
            "the exile rider is scoped only to the targeted creature — other creatures die normally");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
