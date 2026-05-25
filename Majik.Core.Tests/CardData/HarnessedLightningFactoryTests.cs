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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HarnessedLightningFactory"/> (Aether Revolt).
///
/// Oracle:
///   "You get {E}{E}{E} (three energy counters). Choose target creature.
///    You may pay X {E}. Harnessed Lightning deals X damage to that
///    creature."
///
/// Covers:
/// - Identity (Instant, {1}{R}).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="SpellDefinition"/> shape (single 1..1 "target creature",
///   no variable X at cast time — the "may pay X" rider is resolve-time).
/// - Resolve grants the controller three energy unconditionally.
/// - No-agent fallback: pays up to target's remaining toughness, deals
///   that much damage to the creature.
/// - Target gone at resolve (CR 608.2b) — energy still gained, no damage.
/// </summary>
public class HarnessedLightningFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HarnessedLightning_Identity_InstantAtOneRed()
    {
        var card = HarnessedLightningFactory.Create(_alice);

        card.Name.Should().Be("Harnessed Lightning");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HarnessedLightning_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Harnessed Lightning", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Harnessed Lightning");
    }

    // -----------------------------------------------------------------------
    // Spell definition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HarnessedLightning_SpellDefinition_HasSingleCreatureTarget()
    {
        var card = HarnessedLightningFactory.Create(_alice);
        var def = HarnessedLightningFactory.BuildSpellDefinition(
            _alice, card, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse(
            "the X in \"may pay X\" is a resolve-time pick, not a cast-time " +
            "variable cost");
    }

    // -----------------------------------------------------------------------
    // Resolve — energy gain
    // -----------------------------------------------------------------------

    [Fact]
    public void HarnessedLightning_Resolve_GrantsThreeEnergy()
    {
        var alice = new Player("Alice", 20);
        var card = HarnessedLightningFactory.Create(alice);
        // Create a controlled creature target so the damage path runs.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        bear.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(bear);

        var def = HarnessedLightningFactory.BuildSpellDefinition(
            alice, card, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        alice.EnergyCounters.Should().Be(0);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Energy was banked (3 gained), then spent on the X-damage rider
        // — the no-agent fallback picks X = min(energy, lethal_toughness)
        // = min(3, 2) = 2, leaving 3 - 2 = 1 energy remaining.
        alice.EnergyCounters.Should().Be(1,
            "{E}{E}{E} banked, then X=2 spent for 2 damage to a 2/2 (no-agent " +
            "fallback caps at lethal)");
    }

    [Fact]
    public void HarnessedLightning_NoAgent_DealsLethalDamage()
    {
        // No-agent fallback caps X at target's remaining toughness — so a
        // 2/2 with no prior damage gets 2 damage and dies.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var card = HarnessedLightningFactory.Create(alice);
        var def = HarnessedLightningFactory.BuildSpellDefinition(
            alice, card, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Damage.Should().Be(2,
            "no-agent fallback pays X = min(energy=3, toughness=2) = 2");
    }

    [Fact]
    public void HarnessedLightning_TargetGone_GainsEnergyButNoDamage()
    {
        // CR 608.2b — if the chosen creature left the battlefield between
        // cast and resolve the damage rider fizzles. The energy gain
        // already committed before the legality recheck.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard); // already gone

        var card = HarnessedLightningFactory.Create(alice);
        var def = HarnessedLightningFactory.BuildSpellDefinition(
            alice, card, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        alice.EnergyCounters.Should().Be(3,
            "energy gain is unconditional even when the damage step fizzles");
        bear.Damage.Should().Be(0, "no damage when target is no longer legal");
    }

    [Fact]
    public void HarnessedLightning_AlreadyDamaged_NoAgentPicksRemainingToughness()
    {
        // 4/4 with 1 damage already marked → remaining toughness = 3, and
        // controller has 3 fresh energy → X = 3 (kills the creature SBA-side).
        var alice = new Player("Alice", 20);
        var bigBear = new Creature("Big Bears", "{2}{G}", 4, 4);
        bigBear.SetOwner(_bob);
        bigBear.SetController(_bob);
        bigBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bigBear);
        bigBear.TakeDamage(1);

        var card = HarnessedLightningFactory.Create(alice);
        var def = HarnessedLightningFactory.BuildSpellDefinition(
            alice, card, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bigBear },
            },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bigBear.Damage.Should().Be(4,
            "1 prior + 3 fresh = 4 damage on a 4-toughness creature");
        alice.EnergyCounters.Should().Be(0, "all three energy spent on X=3");
    }
}
