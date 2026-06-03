using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for the Offspring keyword subsystem (CR 702.169) — the optional
/// additional cast cost (<see cref="OffspringAdditionalCost"/>, CR 702.169a)
/// plus the resolving permanent's ETB token-copy trigger
/// (<see cref="OffspringAbility"/>, CR 702.169b — "create a 1/1 token copy of
/// it"). Driven through Manifold Mouse + Pawpatch Recruit (Bloomburrow).
/// </summary>
public class OffspringTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public OffspringTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // OffspringAdditionalCost (CR 702.169a) — the optional cast-time payment
    // -----------------------------------------------------------------------

    [Fact]
    public void OffspringCost_Pay_DrainsMana_AndStampsSentinel()
    {
        var mouse = ManifoldMouseFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        var cost = ManifoldMouseFactory.BuildOffspringCost(mouse);
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        mouse.WasOffspringPaid.Should().BeTrue("CR 702.169a — paying stamps the sentinel");
        _alice.ManaPool.IsEmpty.Should().BeTrue("the {2} additional cost drained the pool");
    }

    [Fact]
    public void OffspringCost_CannotPay_WhenShortMana()
    {
        var mouse = ManifoldMouseFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{1}")); // only 1, needs 2

        var cost = ManifoldMouseFactory.BuildOffspringCost(mouse);
        cost.CanPay(_alice).Should().BeFalse();
        cost.Pay(_alice).Should().BeFalse();

        mouse.WasOffspringPaid.Should().BeFalse();
        _alice.ManaPool.IsEmpty.Should().BeFalse("no partial payment — pool untouched (CR 601.2g)");
    }

    [Fact]
    public void OffspringSentinel_DefaultsFalse_AndClears()
    {
        var mouse = ManifoldMouseFactory.Create(_alice);
        mouse.WasOffspringPaid.Should().BeFalse("declined Offspring is the default posture");

        mouse.SetWasOffspringPaid(true);
        mouse.WasOffspringPaid.Should().BeTrue();
        mouse.ClearWasOffspringPaid();
        mouse.WasOffspringPaid.Should().BeFalse("CR 400.7 — cleared after the ETB reads it");
    }

    // -----------------------------------------------------------------------
    // OffspringAbility ETB (CR 702.169b) — create a 1/1 token copy
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_OffspringPaid_CreatesOneOneTokenCopy()
    {
        var triggers = new TriggerManager(_stack, _bus);
        var mouse = ManifoldMouseFactory.Create(_alice, triggers);
        mouse.SetWasOffspringPaid(true);

        EnterBattlefield(mouse);
        FireOffspringEtb(mouse);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1, "CR 702.169b — Offspring paid mints one token copy");
        var token = tokens[0];
        token.Name.Should().Be("Manifold Mouse", "the token is a copy — same name");
        token.BasePower.Should().Be(1, "CR 702.169b — except it's 1/1");
        token.BaseToughness.Should().Be(1, "CR 702.169b — except it's 1/1");
        token.GetEffectiveSubtypes().Should().Contain(CardSubtype.Mouse, "copies subtypes");
        token.Controller.Should().BeSameAs(_alice);

        // Sentinel consumed (CR 400.7).
        mouse.WasOffspringPaid.Should().BeFalse();
    }

    [Fact]
    public void Etb_OffspringNotPaid_CreatesNoToken()
    {
        var triggers = new TriggerManager(_stack, _bus);
        var mouse = ManifoldMouseFactory.Create(_alice, triggers);
        // Offspring declined — sentinel stays false.

        EnterBattlefield(mouse);
        FireOffspringEtb(mouse);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken)
            .Should().Be(0, "CR 702.169b — no token when Offspring wasn't paid");
    }

    [Fact]
    public void Etb_TokenCopy_CopiesKeywords_AndForcesOneOne_OnPawpatch()
    {
        var triggers = new TriggerManager(_stack, _bus);
        var rabbit = PawpatchRecruitFactory.Create(_alice, triggers);
        rabbit.SetWasOffspringPaid(true);

        EnterBattlefield(rabbit);
        FireOffspringEtb(rabbit);

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.IsToken);

        token.Name.Should().Be("Pawpatch Recruit");
        token.BasePower.Should().Be(1, "Pawpatch is a printed 2/1 but the copy is 1/1");
        token.BaseToughness.Should().Be(1);
        token.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Trample", "the copy keeps the source's keyword abilities");
        token.GetEffectiveSubtypes().Should().Contain(CardSubtype.Rabbit);
    }

    // -----------------------------------------------------------------------
    // Full cast pipeline through SpellCastFlow (CR 601.2 → 702.169)
    // -----------------------------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task Cast_WithOffspring_SurvivesResolution_ThenEtbMintsCopy()
    {
        var triggers = new TriggerManager(_stack, _bus);
        var mouse = ManifoldMouseFactory.Create(_alice, triggers);
        mouse.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mouse);
        _alice.AddManaToPool(ManaCost.Parse("{2}")); // Offspring {2}

        var (agent, ctx) = ScriptedCast();

        var spell = await _flow.CastAsync(
            _alice, mouse, CreatureSpellDef(), agent, ctx,
            additionalCosts: new[] { ManifoldMouseFactory.BuildOffspringCost(mouse) });

        // The Offspring sentinel must SURVIVE cast (it is read after the
        // creature enters, not during spell resolution).
        mouse.WasOffspringPaid.Should().BeTrue();

        // Resolve the spell's printed body (none) then move to battlefield +
        // fire the ETB — the sentinel is still set when the ETB reads it.
        foreach (var e in spell.Effects) e.Execute();
        EnterBattlefield(mouse);
        FireOffspringEtb(mouse);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken)
            .Should().Be(1, "the 1/1 token copy is minted on the post-resolution ETB");
        mouse.WasOffspringPaid.Should().BeFalse("ETB consumed the flag (CR 400.7)");
    }

    [Fact]
    public async System.Threading.Tasks.Task Cast_WithoutOffspring_NoToken()
    {
        var triggers = new TriggerManager(_stack, _bus);
        var mouse = ManifoldMouseFactory.Create(_alice, triggers);
        mouse.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mouse);

        var (agent, ctx) = ScriptedCast();

        // No Offspring additional cost layered onto the cast.
        var spell = await _flow.CastAsync(_alice, mouse, CreatureSpellDef(), agent, ctx);

        mouse.WasOffspringPaid.Should().BeFalse();

        foreach (var e in spell.Effects) e.Execute();
        EnterBattlefield(mouse);
        FireOffspringEtb(mouse);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken)
            .Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ManifoldMouse_Identity_AndDispatch()
    {
        var mouse = ManifoldMouseFactory.Create(_alice);
        mouse.HasType(CardType.Creature).Should().BeTrue();
        mouse.Name.Should().Be("Manifold Mouse");
        mouse.ManaCost.Should().Be("{1}{R}");
        mouse.BasePower.Should().Be(1);
        mouse.BaseToughness.Should().Be(2);
        mouse.Subtypes.Should().Contain(CardSubtype.Mouse).And.Contain(CardSubtype.Soldier);
        mouse.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Offspring");

        var dispatched = NamedCardFactory.Create("Manifold Mouse", _alice);
        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Manifold Mouse");
    }

    [Fact]
    public void PawpatchRecruit_Identity_AndDispatch()
    {
        var rabbit = PawpatchRecruitFactory.Create(_alice);
        rabbit.HasType(CardType.Creature).Should().BeTrue();
        rabbit.Name.Should().Be("Pawpatch Recruit");
        rabbit.ManaCost.Should().Be("{G}");
        rabbit.BasePower.Should().Be(2);
        rabbit.BaseToughness.Should().Be(1);
        rabbit.Subtypes.Should().Contain(CardSubtype.Rabbit).And.Contain(CardSubtype.Warrior);
        rabbit.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Offspring").And.Contain("Trample");

        var dispatched = NamedCardFactory.Create("Pawpatch Recruit", _alice);
        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Pawpatch Recruit");
    }

    // -----------------------------------------------------------------------
    // Manifold Mouse begin-combat grant (CR 508.1 / 702.4 / 702.19)
    // -----------------------------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task BeginCombat_GrantsChosenKeyword_ToTargetMouse()
    {
        var triggers = new TriggerManager(_stack, _bus);
        var mouse = ManifoldMouseFactory.Create(_alice, triggers);
        EnterBattlefield(mouse);

        // A second Mouse to target, wired to the layer system.
        var ally = new Creature("Ally Mouse", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Mouse }) { Owner = _alice, Controller = _alice };
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = new Majik.Core.Effects.ContinuousEffectsService();

        var combatTrigger = mouse.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new Majik.Core.Events.StepStartedEvent(
                StepStateType.BeginningOfCombat, _alice)));

        combatTrigger.SetChosenTargets(new[] { new object[] { ally } });

        // Choose Trample (index 1).
        var agent = new ScriptedAgent();
        agent.QueueMode(1);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.BeginningOfCombat, _stack);
        var rctx = Majik.Core.Abilities.ResolutionContext.For(_alice, agent, ctx, chosenTargets: null);

        foreach (var e in combatTrigger.Effects)
        {
            await e.ExecuteAsync(rctx);
        }

        ally.HasEffectiveKeyword("Trample").Should().BeTrue("granted Trample until end of turn");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (ScriptedAgent, GameContext) ScriptedCast()
    {
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain, _stack);
        return (agent, ctx);
    }

    /// <summary>Minimal permanent SpellDefinition — a vanilla creature's
    /// printed body is empty; its behaviour is the ETB + abilities on the
    /// permanent.</summary>
    private static SpellDefinition CreatureSpellDef() =>
        new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>());

    private void EnterBattlefield(Creature c)
    {
        if (c.Zone == ZoneType.Battlefield) return;

        // Ensure the card is tracked in a from-zone the ZoneService can move it
        // out of (a freshly-built test creature defaults to Library but isn't
        // in the library collection). Use Hand as the staging zone unless the
        // card already lives somewhere tracked (e.g. on the stack post-cast).
        if (c.Zone == ZoneType.Stack)
        {
            _zones.MoveCard(c, ZoneType.Stack, ZoneType.Battlefield, controller: _alice);
            return;
        }

        c.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(c);
        _zones.MoveCard(c, ZoneType.Hand, ZoneType.Battlefield, controller: _alice);
    }

    private static void FireOffspringEtb(Creature c)
    {
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.IsTriggered(
                new CardMovedEvent(c, ZoneType.Stack, ZoneType.Battlefield))
                && t.TargetRequests.Count == 0); // the Offspring ETB takes no targets
        foreach (var e in etb.Effects) e.Execute();
    }
}
