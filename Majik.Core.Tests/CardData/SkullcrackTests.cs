using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Skullcrack, Avacyn Restored, {1}{R}, instant. Three effects:
///   - per-target EOT life-gain lockout (CR 614)
///   - 3 damage to that player
///   - "damage can't be prevented this turn" rider (v1 documented no-op).
/// </summary>
public class SkullcrackTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Create_HasInstantShape_Red()
    {
        var s = SkullcrackFactory.Create(_alice);

        s.Name.Should().Be("Skullcrack");
        s.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(s).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSkullcrackShape()
    {
        var dispatched = NamedCardFactory.Create("Skullcrack", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Skullcrack");
    }

    [Fact]
    public void BuildDefinition_ExposesSinglePlayerTarget_WithBurnIntent()
    {
        var def = SkullcrackFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target player");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Burn);
    }

    [Fact]
    public void Resolve_TargetPlayer_Takes3Damage()
    {
        var def = SkullcrackFactory.BuildDefinition(o => o);
        var chosen = BuildChosen(_bob);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(17,
            because: "Skullcrack deals 3 damage to the targeted player");
    }

    [Fact]
    public void Resolve_TargetPlayer_CantGainLifeAfterResolution()
    {
        var bus = new ReplacementBus();
        _bob.AttachReplacementBus(bus);

        var def = SkullcrackFactory.BuildDefinition(o => o, bus);
        var chosen = BuildChosen(_bob);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Pre-condition: 3 damage applied.
        _bob.LifeTotal.Should().Be(17);

        // Bob tries to gain 5 life — replacement rewrites to 0.
        _bob.GainLife(5);
        _bob.LifeTotal.Should().Be(17,
            because: "Skullcrack locks the targeted player out of life-gain for the turn");

        // Other players are unaffected — register Alice on the same bus and
        // confirm she still gains life.
        _alice.AttachReplacementBus(bus);
        _alice.GainLife(3);
        _alice.LifeTotal.Should().Be(23,
            because: "the lockout is scoped to the target player, not the bus");
    }

    [Fact]
    public void Resolve_LifeGainLockout_ExpiresAtEndOfTurn()
    {
        var bus = new ReplacementBus();
        _bob.AttachReplacementBus(bus);

        var def = SkullcrackFactory.BuildDefinition(o => o, bus);
        var chosen = BuildChosen(_bob);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.GainLife(2);
        _bob.LifeTotal.Should().Be(17, "lockout active");

        // CR 514.2 — cleanup drops EOT-expirable replacements.
        bus.ExpireEndOfTurn();

        _bob.GainLife(2);
        _bob.LifeTotal.Should().Be(19,
            because: "the life-gain lockout expires at end of turn");
    }

    [Fact]
    public void Resolve_NoReplacementBus_DamageStillApplied()
    {
        // Single-arg dispatcher path (no bus) — damage still resolves; the
        // life-gain rider silently no-ops.
        var def = SkullcrackFactory.BuildDefinition(o => o, replacements: null);
        var chosen = BuildChosen(_bob);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(17);

        // No bus attached → gain proceeds normally.
        _bob.GainLife(4);
        _bob.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void Resolve_MissingTarget_FizzlesCleanly()
    {
        var def = SkullcrackFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { System.Array.Empty<object>() },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        effects.Should().BeEmpty("CR 608.2b — no target → no effects");
        _bob.LifeTotal.Should().Be(20);
    }

    private static ChosenSpellParams BuildChosen(Player target) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
}
