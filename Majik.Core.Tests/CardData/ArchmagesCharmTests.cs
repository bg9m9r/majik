using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 700.2d — modal "Choose one —" spell. Archmage's Charm, Modern
/// Horizons, {U}{U}{U}, three modes (counter / target-player-draws-2 /
/// gain-control-of-nonland-permanent-with-mv-≤-1).
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring CrypticCommandTests.
/// </summary>
public class ArchmagesCharmTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ArchmagesCharmTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void Create_HasInstantShape_TripleBlue()
    {
        var ac = ArchmagesCharmFactory.Create(_alice);

        ac.Name.Should().Be("Archmage's Charm");
        ac.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(ac).Should().Contain(ManaColor.Blue);
        ac.ManaCostValue.TotalValue.Should().Be(3, because: "{U}{U}{U} = mana value 3");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsArchmagesCharmShape()
    {
        var dispatched = NamedCardFactory.Create("Archmage's Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Archmage's Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesThreeModes_WithPerModeIntents()
    {
        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack);

        def.Modes.Should().HaveCount(3);
        def.Modes[0].Should().Contain("Counter");
        def.Modes[1].Should().Contain("draws two cards");
        def.Modes[2].Should().Contain("Gain control");

        def.ModeIntentsOrEmpty.Should().HaveCount(3);
        def.ModeIntentsOrEmpty[ArchmagesCharmFactory.ModeCounter]
            .Should().Be(BotIntent.Counter);
        def.ModeIntentsOrEmpty[ArchmagesCharmFactory.ModeDraw]
            .Should().Be(BotIntent.Draw);
        def.ModeIntentsOrEmpty[ArchmagesCharmFactory.ModeGainControl]
            .Should().Be(BotIntent.Removal);

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[ArchmagesCharmFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[ArchmagesCharmFactory.ModeDraw].MinTargets.Should().Be(0);
        def.TargetRequests[ArchmagesCharmFactory.ModeGainControl].MinTargets.Should().Be(0);
    }

    [Fact]
    public void Mode0_Counter_RemovesTargetSpellFromStack()
    {
        // Bob has Lightning Bolt on the stack; Alice picks the counter mode.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell }, // mode 0 — counter
            Array.Empty<object>(),     // mode 1 — draw (unused)
            Array.Empty<object>(),     // mode 2 — gain control (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ArchmagesCharmFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "counter mode sends the targeted spell to the graveyard");
    }

    [Fact]
    public void Mode1_TargetPlayerDrawsTwo_PullsTwoCardsFromTargetsLibrary()
    {
        // Bob is the target player — he draws two from his own library.
        var bobTop1 = new Instant("Counterspell", "{U}{U}") { Owner = _bob };
        var bobTop2 = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobTop1);
        _bob.Zones.Library.AddCard(bobTop2);

        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _bob },     // mode 1 — Bob is the target player
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ArchmagesCharmFactory.ModeDraw,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2,
            because: "draw mode pulls 2 cards from the target player's library");
        _bob.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Mode2_GainControl_SwapsControllerViaContinuousEffects_WhenMvLeqOne()
    {
        // Bob controls a Grizzly Bears ({1}{G} — mv 2)? No — we need
        // mv ≤ 1. Use a {G}-cost 1/1 stand-in to keep the gate happy.
        var bobBird = new Creature("Birds of Paradise", "{G}", 0, 1)
        {
            Owner = _bob, Controller = _bob,
        };
        bobBird.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBird);

        var effects = new ContinuousEffectsService();
        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack, effects);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bobBird },  // mode 2 — gain control
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ArchmagesCharmFactory.ModeGainControl,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var resolveEffects = def.EffectFactory(chosen);
        resolveEffects.Should().HaveCount(1);
        foreach (var eff in resolveEffects) eff.Execute();

        // CR 613.2 — Layer 2 EffectiveController returns Alice now.
        effects.EffectiveController(bobBird).Should().BeSameAs(_alice);
        // Underlying Controller is untouched (effect's expiry restores naturally).
        bobBird.Controller.Should().BeSameAs(_bob);
    }

    [Fact]
    public void Mode2_GainControl_GateRejectsManaValueAboveOne()
    {
        // mv = 2 — illegal target at resolution (CR 608.2b). Effect no-ops.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob, Controller = _bob,
        };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var effects = new ContinuousEffectsService();
        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack, effects);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bobBear },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ArchmagesCharmFactory.ModeGainControl,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var resolveEffects = def.EffectFactory(chosen);
        foreach (var eff in resolveEffects) eff.Execute();

        effects.EffectiveController(bobBear).Should().BeSameAs(_bob,
            because: "mv 2 fails the 'mana value 1 or less' gate at resolution");
    }

    [Fact]
    public void Mode2_GainControl_GateRejectsLandTarget()
    {
        // A land has mv 0 (≤ 1) but the "nonland" predicate must reject it.
        var bobLand = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = _bob, Controller = _bob,
        };
        bobLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobLand);

        var effects = new ContinuousEffectsService();
        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack, effects);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bobLand },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ArchmagesCharmFactory.ModeGainControl,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var resolveEffects = def.EffectFactory(chosen);
        foreach (var eff in resolveEffects) eff.Execute();

        effects.EffectiveController(bobLand).Should().BeSameAs(_bob,
            because: "lands are excluded by the 'nonland permanent' predicate");
    }

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        // CR 700.2d — pick count = 1. Caller submits two indices; runtime
        // caps at PickCount (1) and silently drops the overflow.
        var bobTop = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobTop);

        var def = ArchmagesCharmFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _bob },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: ArchmagesCharmFactory.ModeDraw,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                ArchmagesCharmFactory.ModeDraw,
                ArchmagesCharmFactory.ModeCounter, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(ArchmagesCharmFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
