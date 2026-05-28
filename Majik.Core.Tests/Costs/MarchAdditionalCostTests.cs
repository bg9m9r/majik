using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 601.2f + CR 117.7c — March cycle additional-cost-with-cost-reduction.
///
/// Covers:
///   - Cost-shape primitives (CanPay / Pay / ApplyTo / AvailableHandCards).
///   - Exiling 0 cards = base cost; exiling 1 card = -{2} generic.
///   - Mixed-colour hand — only colour-matching cards are eligible.
///   - Cast-flow integration: {X}{B} cast at X=5 with 2 black cards
///     exiled → effective {1}{B} (caster needs to pay 1 generic + B).
/// </summary>
public class MarchAdditionalCostTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MarchAdditionalCostTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Creature BlackCreatureInHand(Player owner, string name)
    {
        var c = new Creature(name, "{1}{B}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static Creature WhiteCreatureInHand(Player owner, string name)
    {
        var c = new Creature(name, "{1}{W}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, turnNumber: 1,
            PhaseStateType.PreCombatMain, _stack);

    private static SpellDefinition NoOpDef() =>
        new(Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

    // ── primitive tests ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        var act = () => new MarchAdditionalCost(null!, ManaColor.Black, Array.Empty<ICard>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullChosen_Throws()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var act = () => new MarchAdditionalCost(spell, ManaColor.Black, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReductionAmount_IsTwoPerExiledCard()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var a = BlackCreatureInHand(_alice, "Black A");
        var b = BlackCreatureInHand(_alice, "Black B");

        var cost = new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { a, b });

        cost.ExiledCount.Should().Be(2);
        cost.ReductionAmount.Should().Be(4, "March reduces generic by {2} per exiled card");
    }

    [Fact]
    public void ApplyTo_ZeroExiled_PreservesCost()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var cost = new MarchAdditionalCost(spell, ManaColor.Black, Array.Empty<ICard>());

        var reduced = cost.ApplyTo(ManaCost.Parse("{X}{B}").AddGenericCost(5));
        // {X}{B} with X=5 folded → {5}{B}, no reduction.
        reduced.Generic.Should().Be(5);
        reduced.Black.Should().Be(1);
    }

    [Fact]
    public void ApplyTo_OneExiled_ReducesGenericByTwo_PreservesBlackPip()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var a = BlackCreatureInHand(_alice, "Black A");
        var cost = new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { a });

        // {X=5}{B} → folded to {5}{B} → -{2} from March → {3}{B}.
        var reduced = cost.ApplyTo(ManaCost.Parse("{X}{B}").AddGenericCost(5));
        reduced.Generic.Should().Be(3);
        reduced.Black.Should().Be(1);
    }

    [Fact]
    public void ApplyTo_OverShoot_FloorsAtZero()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var cards = Enumerable.Range(0, 5)
            .Select(i => BlackCreatureInHand(_alice, $"Black {i}"))
            .Cast<ICard>().ToList();
        var cost = new MarchAdditionalCost(spell, ManaColor.Black, cards);

        // {X=2}{B} folded → {2}{B}; 5 exiles → -{10} but floored at 0.
        var reduced = cost.ApplyTo(ManaCost.Parse("{X}{B}").AddGenericCost(2));
        reduced.Generic.Should().Be(0, "CR 117.7c — cost reductions floor at zero");
        reduced.Black.Should().Be(1, "colour pip is preserved");
    }

    [Fact]
    public void CanPay_BlackCardInHand_True()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var a = BlackCreatureInHand(_alice, "Black A");

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { a })
            .CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_WhiteCardOffered_False()
    {
        // White hand card for a black March → ineligible (colour predicate fails).
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var white = WhiteCreatureInHand(_alice, "White W");

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { white })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_SelfReference_False()
    {
        // Caster can't pay March cost using the spell itself (it's mid-cast).
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        spell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(spell);

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { spell })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_DuplicateCard_False()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var a = BlackCreatureInHand(_alice, "Black A");

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { a, a })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_CardNotInHand_False()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var notInHand = new Creature("Black Detached", "{1}{B}", 1, 1);
        notInHand.SetOwner(_alice);
        // Zone defaults — not Hand.

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { notInHand })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_OpponentOwnedCard_False()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var bobCard = BlackCreatureInHand(_bob, "Bob's Black");

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { bobCard })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Pay_ExilesEveryChosenCard()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var a = BlackCreatureInHand(_alice, "Black A");
        var b = BlackCreatureInHand(_alice, "Black B");

        var cost = new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { a, b });
        cost.Pay(_alice).Should().BeTrue();

        a.Zone.Should().Be(ZoneType.Exile);
        b.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Hand.GetCards().Should().NotContain(new ICard[] { a, b });
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { a, b });
    }

    [Fact]
    public void Pay_IllegalSelection_ReturnsFalseAndDoesNotExile()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var white = WhiteCreatureInHand(_alice, "White");

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { white })
            .Pay(_alice).Should().BeFalse();
        white.Zone.Should().Be(ZoneType.Hand, "illegal cost is rejected, no partial exile");
    }

    [Fact]
    public void AvailableHandCards_FiltersByColor_ExcludesSelf()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        spell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(spell);

        var blackA = BlackCreatureInHand(_alice, "Black A");
        var blackB = BlackCreatureInHand(_alice, "Black B");
        var white = WhiteCreatureInHand(_alice, "White");

        var pool = MarchAdditionalCost.AvailableHandCards(_alice, spell, ManaColor.Black);

        pool.Should().Contain(new ICard[] { blackA, blackB });
        pool.Should().NotContain(white);
        pool.Should().NotContain(spell);
    }

    [Fact]
    public void Description_IncludesCount()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var a = BlackCreatureInHand(_alice, "Black A");

        new MarchAdditionalCost(spell, ManaColor.Black, new ICard[] { a })
            .Description.Should().Contain("1");

        new MarchAdditionalCost(spell, ManaColor.Black, Array.Empty<ICard>())
            .Description.Should().Contain("none");
    }

    // ── cast-flow integration ───────────────────────────────────────────────

    [Fact]
    public async Task Cast_MarchOfWretchedSorrow_X5_With2BlackCardsExiled_EffectiveCostIs1B()
    {
        // Seed the spell + 2 black cards in Alice's hand.
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        spell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(spell);

        var a = BlackCreatureInHand(_alice, "Pawn");
        var b = BlackCreatureInHand(_alice, "Cultist");

        var march = MarchOfWretchedSorrowFactory.BuildAdditionalCost(
            spell, new ICard[] { a, b });

        var target = new Creature("Decoy", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var agent = new CapturingManaAndXAgent(xValue: 5, target: target);

        var def = MarchOfWretchedSorrowFactory.BuildSpellDefinition(_alice, o => o);

        var castSpell = await _flow.CastAsync(
            _alice, spell, def, agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { march });

        // {X=5}{B} folded → {5}{B}; -{4} from March → {1}{B}.
        agent.LastPromptedCost.Should().NotBeNull();
        agent.LastPromptedCost!.Generic.Should().Be(1, "5 generic − {4} from 2 exiles");
        agent.LastPromptedCost!.Black.Should().Be(1, "colour pip preserved");

        a.Zone.Should().Be(ZoneType.Exile);
        b.Zone.Should().Be(ZoneType.Exile);
        spell.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public async Task Cast_MarchOfWretchedSorrow_NoExiles_FullPrintedCost()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        spell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(spell);

        // No additional-cost wired — caster declines the optional March.
        var target = new Creature("Decoy", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var agent = new CapturingManaAndXAgent(xValue: 3, target: target);
        var def = MarchOfWretchedSorrowFactory.BuildSpellDefinition(_alice, o => o);

        await _flow.CastAsync(_alice, spell, def, agent, Ctx());

        // {X=3}{B} folded → {3}{B}. No reduction.
        agent.LastPromptedCost!.Generic.Should().Be(3);
        agent.LastPromptedCost!.Black.Should().Be(1);
    }

    // ── capturing agent ─────────────────────────────────────────────────────

    /// <summary>Test-only agent: captures the mana cost the cast flow
    /// asks it to pay, and answers a fixed X + target.</summary>
    private sealed class CapturingManaAndXAgent : IPlayerAgent
    {
        private readonly int _x;
        private readonly object? _target;
        public ManaCost? LastPromptedCost { get; private set; }

        public CapturingManaAndXAgent(int xValue, object? target = null)
        {
            _x = xValue;
            _target = target;
        }

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, ManaCost cost, CancellationToken ct = default)
        {
            LastPromptedCost = cost;
            return Task.FromResult(ManaPayment.Empty);
        }

        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(_x);

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            if (_target == null) return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
            return Task.FromResult<IReadOnlyList<object>>(new[] { _target });
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
