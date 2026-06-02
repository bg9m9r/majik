using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LightOfHopeFactory"/>.
///
/// Card: Light of Hope — Instant {W} (Aether Revolt).
///   CR 700.2d — modal "Choose one —" spell with 3 modes.
///   Mode 0: "You gain 4 life."
///   Mode 1: "Destroy target enchantment."
///   Mode 2: "Put a +1/+1 counter on target creature."
///
/// Covers:
///   - Identity: name, Instant type, White colour, mana value 1.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: 3 modes, 3 TargetRequests (all MinTargets=0).
///   - Mode 0 resolve: caster gains 4 life (CR 119.3).
///   - Mode 1 resolve: destroy target enchantment; non-enchantment no-ops.
///   - Mode 2 resolve: +1/+1 counter on target creature; non-creature no-ops.
/// </summary>
[Trait("Color", "W")]
public class LightOfHopeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LightOfHope_Create_HasInstantShape_White()
    {
        var card = LightOfHopeFactory.Create(_alice);

        card.Name.Should().Be("Light of Hope");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(1, because: "{W} = mana value 1");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LightOfHope_IsRegistered_AsImplemented()
    {
        ImplementedCardNames.Contains("Light of Hope").Should().BeTrue();
        ImplementedCardNames.HasRealFactory("Light of Hope").Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LightOfHope_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = LightOfHopeFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(3);
        def.Modes[LightOfHopeFactory.ModeGainLife].Should().Contain("4 life");
        def.Modes[LightOfHopeFactory.ModeDestroyEnchantment].Should().Contain("enchantment");
        def.Modes[LightOfHopeFactory.ModeCounter].Should().Contain("+1/+1 counter");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[LightOfHopeFactory.ModeGainLife].MinTargets.Should().Be(0,
            because: "mode 0 has no target — MinTargets must be 0");
        def.TargetRequests[LightOfHopeFactory.ModeGainLife].MaxTargets.Should().Be(0);
        def.TargetRequests[LightOfHopeFactory.ModeDestroyEnchantment].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[LightOfHopeFactory.ModeDestroyEnchantment].MaxTargets.Should().Be(1);
        def.TargetRequests[LightOfHopeFactory.ModeCounter].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[LightOfHopeFactory.ModeCounter].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 0: you gain 4 life
    // -----------------------------------------------------------------------

    [Fact]
    public void LightOfHope_Mode0_CasterGains4Life()
    {
        var def = LightOfHopeFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: LightOfHopeFactory.ModeGainLife,
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

        _alice.LifeTotal.Should().Be(24, because: "20 + 4 = 24 (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Mode 1: destroy target enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void LightOfHope_Mode1_DestroysTargetEnchantment()
    {
        var aura = new Enchantment("Pacifism", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        aura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(aura);

        var def = LightOfHopeFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: LightOfHopeFactory.ModeDestroyEnchantment,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                new object[] { aura },   // mode 1 — enchantment target
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        aura.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the target enchantment (CR 701.7)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(aura);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void LightOfHope_Mode1_NonEnchantmentTarget_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = LightOfHopeFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: LightOfHopeFactory.ModeDestroyEnchantment,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                new object[] { bear },   // raw-target a creature — should no-op
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            because: "Light of Hope mode 1 can only destroy an enchantment");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
    }

    // -----------------------------------------------------------------------
    // Mode 2: put a +1/+1 counter on target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void LightOfHope_Mode2_PutsPlusOneCounterOnTargetCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ActiveEffects = svc;

        var def = LightOfHopeFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: LightOfHopeFactory.ModeCounter,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                new object[] { bear },   // mode 2 — creature target
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            because: "mode 2 puts a single +1/+1 counter (CR 122)");
        var computed = svc.Compute(bear);
        computed.Power.Should().Be(3, because: "2/2 + a +1/+1 counter = 3/3");
        computed.Toughness.Should().Be(3);
    }

    [Fact]
    public void LightOfHope_Mode2_NonCreatureTarget_NoOp()
    {
        var aura = new Enchantment("Pacifism", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        aura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(aura);

        var def = LightOfHopeFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: LightOfHopeFactory.ModeCounter,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                new object[] { aura },   // raw-target an enchantment — should no-op
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        aura.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            because: "Light of Hope mode 2 can only target a creature");
    }
}
