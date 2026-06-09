using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
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
/// End-to-end tests for Bloodthirsty Adversary (Innistrad: Midnight Hunt,
/// {1}{R}) — Creature — Vampire 2/2 with Haste.
///   "When this creature enters, you may pay {2}{R} any number of times. When
///    you pay this cost one or more times, put that many +1/+1 counters on this
///    creature, then exile up to that many target instant and/or sorcery cards
///    with mana value 3 or less from your graveyard and copy them. You may cast
///    any number of the copies without paying their mana costs."
///
/// Exercises the two binder-chain primitives the card needs:
///   1. <see cref="Majik.Core.Primitives.RepeatableManaPayment"/> — the
///      resolution-time "pay {2}{R} any number of times" loop (count N).
///   2. <see cref="Card.GrantRuntimeFlashback"/> at {0} — the free
///      cast-from-graveyard recursion (Snapcaster's mechanism, free cost), cast
///      through the real <see cref="SpellCastFlow"/> and exiled on resolution.
///
/// Coverage:
///   * Identity (Creature — Vampire, {1}{R}, 2/2) + NamedCardFactory dispatch +
///     Haste keyword.
///   * The ETB is a real <see cref="ITriggeredAbility"/> in card.Abilities (so
///     the pool-wide audit stops flagging MissingTrigger), wired on BOTH the
///     canonical and the prod effects-aware routed build.
///   * Pay twice ⇒ two +1/+1 counters + up to two free graveyard recursions.
///   * Pay zero ⇒ no counters, no grant (reflexive "one or more times" never
///     fires — CR 603.2).
///   * "up to that many" — at most N targets granted free flashback.
///   * Legality filter (instant/sorcery, mv ≤ 3, your graveyard).
///   * The granted free flashback actually casts the spell from the graveyard
///     through SpellCastFlow and exiles it (CR 702.34b).
///   * The grant expires at end of turn (CR 514.2).
/// </summary>
public class BloodthirstyAdversaryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BloodthirstyAdversaryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    private Creature OnBattlefield(IEventBus? bus = null)
    {
        var adversary = BloodthirstyAdversaryFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(adversary);
        adversary.SetZone(ZoneType.Battlefield);
        return adversary;
    }

    private Instant GraveInstant(string name, string cost, Player owner)
    {
        var c = new Instant(name, cost) { Owner = owner };
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    private GameContext Game() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    /// <summary>
    /// Drive the ETB trigger's resolution through the real async effect path
    /// with a live <see cref="ResolutionContext"/> (agent + game), exactly as
    /// the engine resolves a triggered ability off the stack.
    /// </summary>
    private void ResolveEtb(Creature adversary, IPlayerAgent? agent)
    {
        var etb = adversary.Abilities.OfType<TriggeredAbility>().Single();
        var ctx = ResolutionContext.For(_alice, agent, Game(), chosenTargets: null);
        foreach (var e in etb.Effects)
        {
            e.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Agent that pays the repeatable {2}{R} exactly
    /// <paramref name="times"/> times then declines, and (when targets are
    /// chosen) returns <paramref name="targets"/> for the up-to-N request.</summary>
    private static ScriptedAgent PayThenTarget(int times, params object[] targets)
    {
        var agent = new ScriptedAgent();
        for (var i = 0; i < times; i++) agent.QueueYesNo(true);
        agent.QueueYesNo(false); // stop paying
        agent.QueueTargets(targets);
        return agent;
    }

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void BloodthirstyAdversary_IsVampireCreature_AtCost1R_2_2()
    {
        var a = BloodthirstyAdversaryFactory.Create(_alice);

        a.Name.Should().Be("Bloodthirsty Adversary");
        a.HasType(CardType.Creature).Should().BeTrue();
        a.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        a.ManaCost.Should().Be("{1}{R}");
        a.Power.Should().Be(2);
        a.Toughness.Should().Be(2);
        a.Owner.Should().BeSameAs(_alice);
        a.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BloodthirstyAdversary()
    {
        var card = NamedCardFactory.Create("Bloodthirsty Adversary", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bloodthirsty Adversary");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void HasHasteKeyword()
    {
        var a = BloodthirstyAdversaryFactory.Create(_alice);

        a.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "Bloodthirsty Adversary has Haste (CR 702.10)");
    }

    // ------------------------------------------------------------------
    // The ETB is a real triggered ability (pool-wide audit guard)
    // ------------------------------------------------------------------

    [Fact]
    public void Etb_IsARealTriggeredAbility_InCardAbilities()
    {
        var a = BloodthirstyAdversaryFactory.Create(_alice);
        a.Abilities.OfType<ITriggeredAbility>().Should().ContainSingle(
            "the ETB reflexive ability is a real TriggeredAbility in card.Abilities");
    }

    /// <summary>
    /// PROD-PATH regression guard (same class as the Festival Crasher /
    /// Stormbreath Dragon fix). The production GameFacade routed build
    /// dispatches the effects-aware overload, NOT the single-arg one. If the
    /// generator doesn't see Create(Player, ContinuousEffectsService) the routed
    /// build falls through to shape-only dispatch and the ETB trigger is absent
    /// in live play (the MissingTrigger bug). This builds the card exactly as
    /// prod does and asserts the trigger is bound.
    /// </summary>
    [Fact]
    public void EffectsAwareDispatch_WiresEtbTrigger_OnProdPath()
    {
        var effects = new ContinuousEffectsService(_bus);

        var built = NamedCardFactory.Create("Bloodthirsty Adversary", _alice, effects);
        built.Should().BeOfType<Creature>();

        built.Abilities.OfType<ITriggeredAbility>().Should().ContainSingle(
            "the prod effects-aware dispatch must route through the "
            + "Create(Player, ContinuousEffectsService) overload — not shape-only");
    }

    // ------------------------------------------------------------------
    // Resolution-time repeatable payment ⇒ +1/+1 counters
    // ------------------------------------------------------------------

    [Fact]
    public void Etb_PaidTwice_PlacesTwoPlusOnePlusOneCounters()
    {
        var a = OnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("{4}{R}{R}")); // {2}{R} ×2

        ResolveEtb(a, PayThenTarget(times: 2));

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "paying {2}{R} twice puts two +1/+1 counters on this creature");
    }

    [Fact]
    public void Etb_PaidZeroTimes_IsCleanNoOp()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);

        // Agent declines the very first payment prompt.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        ResolveEtb(a, agent);

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the reflexive 'when you pay one or more times' never fires at N==0 (CR 603.2)");
        bolt.RuntimeFlashbackCost.Should().BeNull("no recursion when N==0");
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Etb_CannotAffordPayment_NoCountersNoGrant()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);

        // Empty pool — agent says yes but can't pay; the loop ends at N==0.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        ResolveEtb(a, agent);

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        bolt.RuntimeFlashbackCost.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Up-to-N free graveyard recursion via flashback grant
    // ------------------------------------------------------------------

    [Fact]
    public void Etb_PaidOnce_GrantsFreeFlashbackToOneChosenTarget()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        _alice.AddManaToPool(ManaCost.Parse("{2}{R}")); // pay once

        ResolveEtb(a, PayThenTarget(times: 1, bolt));

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bolt.RuntimeFlashbackCost.Should().NotBeNull(
            "the chosen graveyard instant is granted flashback");
        bolt.RuntimeFlashbackCost!.TotalValue.Should().Be(0,
            "the recursion is free — 'without paying their mana costs' (CR 601.3b)");
    }

    [Fact]
    public void Etb_UpToThatMany_GrantsAtMostNTargets()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var shock = GraveInstant("Shock", "{R}", _alice);
        _alice.AddManaToPool(ManaCost.Parse("{2}{R}")); // pay once

        // Paid once, but two targets handed in — only the first is taken
        // ("exile UP TO that many").
        ResolveEtb(a, PayThenTarget(times: 1, bolt, shock));

        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        shock.RuntimeFlashbackCost.Should().BeNull("only N targets are taken (N == 1)");
    }

    [Fact]
    public void Etb_PaidTwice_GrantsTwoTargets()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var shock = GraveInstant("Shock", "{R}", _alice);
        _alice.AddManaToPool(ManaCost.Parse("{4}{R}{R}")); // pay twice

        ResolveEtb(a, PayThenTarget(times: 2, bolt, shock));

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        shock.RuntimeFlashbackCost.Should().NotBeNull();
    }

    // ------------------------------------------------------------------
    // Legality filters
    // ------------------------------------------------------------------

    [Fact]
    public void LegalTargets_FiltersByTypeManaValueAndOwner()
    {
        GraveInstant("Lightning Bolt", "{R}", _alice);          // legal (mv 1)
        GraveInstant("Lightning Bolt 2", "{1}{R}{R}", _alice);  // legal (mv 3)
        var big = new Sorcery("Big", "{2}{R}{R}") { Owner = _alice }; // mv 4 — illegal
        big.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(big);
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice }; // not I/S
        bears.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bears);
        GraveInstant("Bob Bolt", "{R}", _bob);                 // wrong owner

        var legal = BloodthirstyAdversaryFactory.LegalTargets(_alice);

        legal.Should().HaveCount(2);
        legal.Should().OnlyContain(c => c.ManaCostValue.TotalValue <= 3);
    }

    [Fact]
    public void Etc_ManaValueGreaterThanThree_NotGranted()
    {
        var a = OnBattlefield();
        var big = new Sorcery("Cruel Ultimatum", "{U}{U}{B}{B}{B}{R}{R}") { Owner = _alice };
        big.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(big);
        _alice.AddManaToPool(ManaCost.Parse("{2}{R}")); // pay once

        // The big spell isn't in LegalTargets, so the agent can't pick it; even
        // if forced, IsLegalGraveyardTarget rejects it at resolution.
        ResolveEtb(a, PayThenTarget(times: 1, big));

        big.RuntimeFlashbackCost.Should().BeNull("mana value > 3 is not a legal target");
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the +1/+1 counters still land regardless of whether a legal target exists");
    }

    // ------------------------------------------------------------------
    // The granted free flashback actually casts from the graveyard
    // ------------------------------------------------------------------

    [Fact]
    public async Task GrantedFlashback_CastsSpellFromGraveyardFree_AndExiles()
    {
        var a = OnBattlefield(_bus);
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        _alice.AddManaToPool(ManaCost.Parse("{2}{R}")); // pay once

        ResolveEtb(a, PayThenTarget(times: 1, bolt));

        bolt.RuntimeFlashbackCost.Should().NotBeNull();

        // Cast Bolt from the graveyard using the granted free flashback —
        // through the real SpellCastFlow, with an empty mana payment ({0}).
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);
        var altCost = new FlashbackAlternativeCost(bolt.RuntimeFlashbackCost!);

        var boltSpell = await _flow.CastAsync(
            _alice, bolt,
            new SpellDefinition(
                Modes: Array.Empty<string>(), HasVariableX: false,
                TargetRequests: new[]
                {
                    new TargetRequest("any target", 1, 1, Array.Empty<object>()),
                },
                EffectFactory: p => new IEffect[]
                {
                    new Effect("Lightning Bolt: deal 3 damage", () =>
                    {
                        if (p.Targets[0][0] is Player pl) pl.LoseLife(3);
                    }),
                }),
            agent, Game(),
            alternativeCost: altCost);

        bolt.Zone.Should().Be(ZoneType.Stack);
        boltSpell.Resolve();

        _bob.LifeTotal.Should().Be(17, "the free flashback cast deals 3 to Bob");
        bolt.Zone.Should().Be(ZoneType.Exile, "CR 702.34b — flashback exiles after resolution");
    }

    // ------------------------------------------------------------------
    // End-of-turn cleanup of the grant
    // ------------------------------------------------------------------

    [Fact]
    public void GrantedFlashback_ExpiresAtEndOfTurn()
    {
        var a = OnBattlefield(_bus);
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        _alice.AddManaToPool(ManaCost.Parse("{2}{R}")); // pay once

        ResolveEtb(a, PayThenTarget(times: 1, bolt));
        bolt.RuntimeFlashbackCost.Should().NotBeNull("grant is live before EOT");

        _bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));

        bolt.RuntimeFlashbackCost.Should().BeNull(
            "CR 514.2 — the runtime flashback grant expires at end of turn");
    }
}
