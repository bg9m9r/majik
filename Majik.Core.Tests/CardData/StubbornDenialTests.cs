using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Stubborn Denial (Khans of Tarkir, {U}, Instant).
///
/// Oracle text:
///   "Choose one —
///    • Counter target noncreature spell unless its controller pays {1}.
///    • Ferocious — Counter that spell if you control a creature with
///      power 4 or greater."
///
/// CR 702.114 — Ferocious is a state check at resolution; controller
/// must control a creature with effective power ≥ 4 for the upgraded
/// branch.
///
/// Covers:
///   - Card identity (Instant, {U}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Non-ferocious: opponent pays {1} → spell resolves.
///   - Non-ferocious: opponent declines → spell countered.
///   - Ferocious active (4/4 Bear): countered unconditionally, opponent
///     can't save by paying.
///   - Ferocious via CDA (Tarmogoyf-style power ≥ 4): triggers because
///     IsFerociousActive uses ContinuousEffectsService.Compute.
///   - Creature spell target → no-op (CR 608.2b).
/// </summary>
public class StubbornDenialTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public StubbornDenialTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StubbornDenial_IsInstant_AtCostU()
    {
        var sd = StubbornDenialFactory.Create(_alice);

        sd.Name.Should().Be("Stubborn Denial");
        sd.ManaCost.Should().Be("{U}");
        sd.HasType(CardType.Instant).Should().BeTrue();
        sd.Owner.Should().BeSameAs(_alice);
        sd.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StubbornDenial()
    {
        var card = NamedCardFactory.Create("Stubborn Denial", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Stubborn Denial");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — non-ferocious branch (unless-pay rider)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NoBigCreature_OpponentPays_SpellResolves()
    {
        // No ferocious-qualifying creature in play. Bob's controller-pays
        // callback returns true → Stubborn Denial does nothing; bolt stays
        // on the stack and resolves on its own.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        await CastStubbornDenial(target: bobSpell, willOpponentPay: () => true);
        _resolver.ResolveTop(_stack); // Resolve Stubborn Denial

        // Bolt was NOT countered — still on stack (or in whatever zone the
        // bolt resolves to, but specifically not graveyard-via-counter).
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "opponent paid {1}; Stubborn Denial's effect did nothing");
    }

    [Fact]
    public async Task NoBigCreature_OpponentDeclines_SpellCountered()
    {
        // Non-ferocious + opponent doesn't pay → spell countered.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        await CastStubbornDenial(target: bobSpell, willOpponentPay: () => false);
        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "non-ferocious + opponent declines to pay → countered");
    }

    // -----------------------------------------------------------------------
    // Resolution — ferocious branch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BigCreatureOnBoard_OpponentCannotSaveByPaying()
    {
        // Alice controls a 4/4 Bear. Ferocious is active → counter
        // unconditionally; the unless-pay callback is NEVER consulted.
        var fatBear = new Creature("Big Bear", "{2}{G}{G}", 4, 4)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(fatBear);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Even if the callback claims Bob pays, ferocious should short-circuit it.
        var payCallbackInvoked = false;
        await CastStubbornDenial(target: bobSpell, willOpponentPay: () =>
        {
            payCallbackInvoked = true;
            return true;
        });
        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "ferocious is active → countered regardless of pay");
        payCallbackInvoked.Should().BeFalse(
            "ferocious bypasses the unless-pay rider entirely");
    }

    [Fact]
    public async Task FerociousViaCda_TarmogoyfPower4_TriggersFerocious()
    {
        // Wire a Tarmogoyf-style CDA creature on Alice's battlefield. Its
        // BasePower is 0, but ContinuousEffectsService.Compute reports
        // power = distinct card types across graveyards. Seed 4 types so
        // Tarmogoyf's effective power is 4.
        var effects = new ContinuousEffectsService();
        Func<IEnumerable<ICard>> allGraveyards = () =>
            _alice.Zones.Graveyard.GetCards().Concat(_bob.Zones.Graveyard.GetCards());
        var goyf = TarmogoyfFactory.Create(_alice, effects, _bus, allGraveyards);
        goyf.ActiveEffects = effects;
        // Seed in Library, then route through ZoneService so (a) the player-
        // local Zones collection actually has the card on Battlefield, and
        // (b) Tarmogoyf's CDA lifecycle hook fires on CardMovedEvent.
        _alice.Zones.Library.AddCard(goyf);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // 4 distinct card types in graveyards → goyf power = 4.
        SeedGraveyardWithTypes(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        // Sanity: printed BasePower is 0 (would fail ferocious); effective
        // via Compute is 4 (passes ferocious). This is the whole point of
        // routing through ContinuousEffectsService.Compute.
        goyf.BasePower.Should().Be(0);
        ((CreatureCharacteristics)effects.Compute(goyf)).Power.Should().Be(4);

        StubbornDenialFactory.IsFerociousActive(_alice, effects).Should().BeTrue(
            "Tarmogoyf's effective power (via Compute) is 4");
        StubbornDenialFactory.IsFerociousActive(_alice, effects: null).Should().BeFalse(
            "without ContinuousEffectsService we'd see BasePower 0 only");

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        await CastStubbornDenial(
            target: bobSpell,
            willOpponentPay: () => true, // would save if consulted
            effects: effects);
        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Tarmogoyf's CDA power (4) trips ferocious; counter unconditionally");
    }

    // -----------------------------------------------------------------------
    // Targeting — creature spell is illegal at resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TargetingCreatureSpell_IsNoOp_AtResolution()
    {
        // CR 608.2b — at resolution, if the target is a creature spell
        // the effect does nothing. Even with a 4/4 in play.
        var fatBear = new Creature("Big Bear", "{2}{G}{G}", 4, 4)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(fatBear);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        var bobCreatureSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobCreatureSpell);

        await CastStubbornDenial(target: bobCreatureSpell, willOpponentPay: () => false);
        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "creature spell is illegal target → effect does nothing");
    }

    // -----------------------------------------------------------------------
    // IsFerociousActive — helper-level coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void IsFerociousActive_BasePowerPath_RespectsThreshold()
    {
        // No effects service → BasePower path.
        StubbornDenialFactory.IsFerociousActive(_alice).Should().BeFalse(
            "empty battlefield");

        var threeThree = new Creature("Three Three", "{2}{G}", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(threeThree);
        StubbornDenialFactory.IsFerociousActive(_alice).Should().BeFalse(
            "3 < 4 — below threshold");

        var fourFour = new Creature("Four Four", "{2}{G}{G}", 4, 4)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(fourFour);
        StubbornDenialFactory.IsFerociousActive(_alice).Should().BeTrue(
            "4 = 4 — threshold met (CR 702.114)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Stubborn Denial from Alice's hand at <paramref name="target"/>
    /// using the supplied unless-pay callback. Mirrors the
    /// ForceOfNegationFactoryTests cast harness.
    /// </summary>
    private async Task CastStubbornDenial(
        object target,
        Func<bool> willOpponentPay,
        ContinuousEffectsService? effects = null)
    {
        var sd = StubbornDenialFactory.Create(_alice);
        sd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sd);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, sd,
            StubbornDenialFactory.BuildSpellDefinition(
                _alice, t => t, _stack, effects, willOpponentPay),
            agent, ctx);
    }

    /// <summary>
    /// Drop one card per supplied type-set into <paramref name="player"/>'s
    /// graveyard. Mirrors the Unholy Heat / Tarmogoyf graveyard seeding
    /// helpers.
    /// </summary>
    private static void SeedGraveyardWithTypes(Player player, params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"GySeed{i++}", "0", types);
            card.SetOwner(player);
            player.Zones.Graveyard.AddCard(card);
        }
    }
}
