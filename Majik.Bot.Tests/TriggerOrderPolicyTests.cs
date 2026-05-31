using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// CR 603.3b: the active player chooses the order of their simultaneously-
/// fired triggers. <see cref="TriggerOrderPolicy"/> ranks by an effect-text
/// keyword bag and returns the order such that the highest-scoring trigger
/// resolves first (= ends up on top of the stack, = LAST in the returned
/// list because <see cref="TriggerManager"/> pushes in order).
/// </summary>
public class TriggerOrderPolicyTests
{
    private sealed class FakeEffect : IEffect
    {
        public FakeEffect(string description) { Description = description; }
        public string Description { get; }
        public ValueTask ExecuteAsync(ResolutionContext ctx) => ValueTask.CompletedTask;
    }

    private static TriggeredAbility MakeTrig(Player controller, string sourceName, string effectText)
    {
        var src = new Creature(sourceName, manaCost: string.Empty, power: 1, toughness: 1);
        src.ChangeOwner(controller);
        return new TriggeredAbility(
            source: src,
            controller: controller,
            condition: Triggers.OnEnterBattlefieldSelf(src),
            effects: new[] { (IEffect)new FakeEffect(effectText) });
    }

    [Fact]
    public void Order_EmptyList_ReturnsAsIs()
    {
        var s = new BotTestScenario();
        var ordered = TriggerOrderPolicy.Order(s.Context, System.Array.Empty<ITriggeredAbility>());
        ordered.Should().BeEmpty();
    }

    [Fact]
    public void Order_SingleTrigger_PassThrough()
    {
        var s = new BotTestScenario();
        var t = MakeTrig(s.Self, "A", "Draw a card.");
        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { t });
        ordered.Should().ContainSingle().And.Contain(t);
    }

    [Fact]
    public void Order_DamageBeatsCleanup_DamageResolvesFirst()
    {
        // Damage = race tempo, must hit before the no-op trigger so opponent
        // takes the hit. Top-of-stack = LAST in returned list.
        var s = new BotTestScenario();
        var cleanup = MakeTrig(s.Self, "Cleanup", "Each player notes the time.");
        var damage = MakeTrig(s.Self, "Bolt", "Deal 3 damage to target creature or player.");

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { cleanup, damage });

        ordered[^1].Should().BeSameAs(damage);
    }

    [Fact]
    public void Order_DrawBeatsLifeGain()
    {
        var s = new BotTestScenario();
        var life = MakeTrig(s.Self, "LifeBoost", "You gain 2 life.");
        var draw = MakeTrig(s.Self, "Cantrip", "Draw a card.");

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { life, draw });

        ordered[^1].Should().BeSameAs(draw);
    }

    [Fact]
    public void Order_TokenCreationBeatsScry()
    {
        var s = new BotTestScenario();
        var scry = MakeTrig(s.Self, "Scryer", "Scry 1.");
        var token = MakeTrig(s.Self, "Tokens", "Create a 1/1 white Soldier creature token.");

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { scry, token });

        ordered[^1].Should().BeSameAs(token);
    }

    [Fact]
    public void Order_DrawbackTriggerSinks_DiscardLastInResolution()
    {
        // "Discard a card" is a self-cost drawback — we want it on the BOTTOM
        // of the stack (resolves LAST) so the upside lands first.
        var s = new BotTestScenario();
        var discard = MakeTrig(s.Self, "Madness", "Discard a card.");
        var draw = MakeTrig(s.Self, "Cantrip", "Draw a card.");

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { draw, discard });

        // discard's lower score → ends up at index 0 (bottom of stack).
        ordered[0].Should().BeSameAs(discard);
        ordered[^1].Should().BeSameAs(draw);
    }

    [Fact]
    public void Order_SacrificeSinks()
    {
        var s = new BotTestScenario();
        var sac = MakeTrig(s.Self, "SacEdict", "Sacrifice a creature.");
        var destroy = MakeTrig(s.Self, "Disenchant", "Destroy target artifact or enchantment.");

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { destroy, sac });

        ordered[0].Should().BeSameAs(sac);
        ordered[^1].Should().BeSameAs(destroy);
    }

    [Fact]
    public void Order_StableOnTies_PreservesOriginalOrder()
    {
        // Two identical descriptions → tie → original order preserved.
        // Original order in the input carries TriggerManager's timestamp
        // semantics, so stability matters for determinism.
        var s = new BotTestScenario();
        var first = MakeTrig(s.Self, "A", "Draw a card.");
        var second = MakeTrig(s.Self, "B", "Draw a card.");

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { first, second });

        ordered[0].Should().BeSameAs(first);
        ordered[1].Should().BeSameAs(second);
    }

    [Fact]
    public void Order_NoEffects_FallsBackToSourceNameSignal()
    {
        // No IEffect strings → scorer leans on source-card name only.
        // Neither name carries keywords, so they tie and original order wins.
        var s = new BotTestScenario();
        var src1 = new Creature("Vanilla1", string.Empty, 1, 1);
        src1.ChangeOwner(s.Self);
        var src2 = new Creature("Vanilla2", string.Empty, 1, 1);
        src2.ChangeOwner(s.Self);
        var t1 = new TriggeredAbility(src1, s.Self, Triggers.OnEnterBattlefieldSelf(src1));
        var t2 = new TriggeredAbility(src2, s.Self, Triggers.OnEnterBattlefieldSelf(src2));

        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { t1, t2 });

        ordered.Should().Equal(new ITriggeredAbility[] { t1, t2 });
    }

    [Fact]
    public void Order_MixedBatch_DamageTopsThenTokenThenLifegainThenCleanup()
    {
        // Full ranking check on a realistic ETB cluster: damage > token >
        // life gain > vanilla cleanup. Damage should resolve first
        // (= top of stack = last in list), cleanup last (= bottom = first).
        var s = new BotTestScenario();
        var cleanup = MakeTrig(s.Self, "Cleanup", "Each player notes the time.");
        var life    = MakeTrig(s.Self, "Soul",    "You gain 1 life.");
        var token   = MakeTrig(s.Self, "Token",   "Create a 1/1 Spirit creature token.");
        var damage  = MakeTrig(s.Self, "Bolt",    "Deal 3 damage to any target.");

        var ordered = TriggerOrderPolicy.Order(
            s.Context,
            new ITriggeredAbility[] { life, cleanup, damage, token });

        ordered[0].Should().BeSameAs(cleanup); // weakest → resolves last
        ordered[^1].Should().BeSameAs(damage); // strongest → resolves first
    }

    [Fact]
    public void ScoreEffectText_HigherForBetterEffect()
    {
        // Direct sanity check on the scorer — keeps the keyword bag visible
        // to future contributors without forcing them to read the bigger
        // integration scenarios.
        TriggerOrderPolicy.ScoreEffectText("Deal 5 damage to target creature.")
            .Should().BeGreaterThan(TriggerOrderPolicy.ScoreEffectText("You gain 1 life."));
        TriggerOrderPolicy.ScoreEffectText("Draw a card.")
            .Should().BeGreaterThan(TriggerOrderPolicy.ScoreEffectText("Scry 1."));
        TriggerOrderPolicy.ScoreEffectText("Sacrifice a creature.")
            .Should().BeLessThan(0.0);
        TriggerOrderPolicy.ScoreEffectText(null).Should().Be(0.0);
        TriggerOrderPolicy.ScoreEffectText("   ").Should().Be(0.0);
    }
}
