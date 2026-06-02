using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 700.2d — modal "Choose one —" spell. Origin of Metalbending,
/// Instant — Lesson, {1}{G}, two modes:
///   Mode 0: Destroy target artifact or enchantment.
///   Mode 1: Put a +1/+1 counter on target creature you control;
///           it gains indestructible until end of turn.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring RipApartTests / BantCharmTests.
/// </summary>
[Trait("Color", "G")]
public class OriginOfMetalbendingTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_GreenOneOne()
    {
        var card = OriginOfMetalbendingFactory.Create(_alice);

        card.Name.Should().Be("Origin of Metalbending");
        card.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{G} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_ExposesTwoModes_WithPerModeIntents()
    {
        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(2);
        def.Modes[OriginOfMetalbendingFactory.ModeDestroy].Should().Contain("Destroy");
        def.Modes[OriginOfMetalbendingFactory.ModeCounter].Should().Contain("+1/+1 counter");

        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.ModeIntentsOrEmpty[OriginOfMetalbendingFactory.ModeDestroy].Should().Be(BotIntent.Removal);
        def.ModeIntentsOrEmpty[OriginOfMetalbendingFactory.ModeCounter].Should().Be(BotIntent.Buff);

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[OriginOfMetalbendingFactory.ModeDestroy].MinTargets.Should().Be(0);
        def.TargetRequests[OriginOfMetalbendingFactory.ModeCounter].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy target artifact or enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_Destroy_MovesArtifactToGraveyard()
    {
        var bobBauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _bob, Controller = _bob };
        bobBauble.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBauble);

        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBauble },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: OriginOfMetalbendingFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobBauble.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 destroys the target artifact");
    }

    [Fact]
    public void Mode0_Destroy_MovesEnchantmentToGraveyard()
    {
        var bobAura = new Enchantment("Pacifism", "{1}{W}") { Owner = _bob, Controller = _bob };
        bobAura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobAura);

        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobAura },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: OriginOfMetalbendingFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobAura.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 also destroys an enchantment target");
    }

    [Fact]
    public void Mode0_Destroy_IgnoresNonArtifactNonEnchantmentTarget()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBear }, // a creature — CR 608.2b gate
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: OriginOfMetalbendingFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            because: "the destroy mode no-ops on a creature target");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — +1/+1 counter on target creature you control + indestructible
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_Counter_PlacesPlusOnePlusOneAndGrantsIndestructible()
    {
        var ce = new ContinuousEffectsService();
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        aliceBear.ActiveEffects = ce;
        aliceBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBear);

        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o, ce);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { aliceBear },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: OriginOfMetalbendingFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        aliceBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            because: "mode 1 puts a +1/+1 counter on the target creature");

        // CR 613.1f / 702.12 — the grant lives in the layer system; recompute
        // characteristics and confirm Indestructible is now present.
        var chars = ce.Compute(aliceBear);
        chars.Keywords.Should().Contain("Indestructible",
            because: "mode 1 grants indestructible until end of turn");
    }

    [Fact]
    public void Mode1_Counter_IgnoresCreatureNotControlledByCaster()
    {
        // Bob's creature — Alice can't target it with "creature you control".
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.ActiveEffects = new ContinuousEffectsService();
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobBear },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: OriginOfMetalbendingFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            because: "mode 1's 'creature you control' re-check fails for an opponent's creature (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        var def = OriginOfMetalbendingFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: OriginOfMetalbendingFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                OriginOfMetalbendingFactory.ModeDestroy,
                OriginOfMetalbendingFactory.ModeCounter, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(OriginOfMetalbendingFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
