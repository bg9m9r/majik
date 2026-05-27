using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Gifts Ungiven (Champions of Kamigawa, {3}{U}, Instant).
///
/// Oracle: "Search your library for up to four cards with different
/// names and reveal them. Target opponent chooses two of those cards.
/// Put the chosen cards into your graveyard and the rest into your
/// hand. Then shuffle."
///
/// Coverage:
/// - Identity (name, type, cost, colour) + NamedCardFactory dispatch.
/// - SpellDefinition shape: 1..1 target opponent, no modes, no X.
/// - Pile-of-4 resolution: 2 cards → graveyard, 2 → hand, library shuffles.
/// - Distinct-name rider: caster cannot reveal two cards with the same name.
/// - Caster declines early (returns null) → smaller revealed pile,
///   opponent's pick-2 step clamps to what was revealed.
/// - Library with fewer than 4 candidates → tutor stops cleanly.
/// - Illegal target at resolution → whole effect no-ops.
/// </summary>
public class GiftsUngivenTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GiftsUngivenTests()
    {
        AgentRegistry.Clear();
    }

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_BlueFourMana()
    {
        var card = GiftsUngivenFactory.Create(_alice);

        card.Name.Should().Be("Gifts Ungiven");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}");
        card.ManaCostValue.TotalValue.Should().Be(4);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsGiftsUngivenShape()
    {
        var dispatched = NamedCardFactory.Create("Gifts Ungiven", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Gifts Ungiven");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{3}{U}");
    }

    // ---------------------------------------------------------------
    // SpellDefinition shape
    // ---------------------------------------------------------------

    [Fact]
    public void BuildDefinition_HasOneTargetOpponentSlot_NoModesNoX()
    {
        var def = GiftsUngivenFactory.BuildDefinition(_alice, raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target opponent");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Full resolution — 4 distinct picks, 2 to graveyard, 2 to hand
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_FourDistinctNamePicks_TwoToGraveyard_TwoToHand_LibraryShuffles()
    {
        // Seed Alice's library with four distinct-name cards + a duplicate
        // to verify distinct-name enforcement.
        var unburial = new Sorcery("Unburial Rites", "{4}{B}");
        var griselbrand = new Creature("Griselbrand", "{4}{B}{B}{B}", 7, 7);
        var iona = new Creature("Iona, Shield of Emeria", "{6}{W}{W}", 7, 7);
        var elesh = new Creature("Elesh Norn, Grand Cenobite", "{5}{W}{W}", 4, 7);
        var unburialDup = new Sorcery("Unburial Rites", "{4}{B}"); // same name
        var filler = new Instant("Filler", "{U}");

        foreach (var c in new ICard[] { unburial, griselbrand, iona, elesh, unburialDup, filler })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = GiftsUngivenFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // 2 cards → Alice's graveyard, 2 → Alice's hand, the rest stay
        // in the library (DeterministicBotAgent picks the first candidate
        // each time, which after distinct-name filtering = Unburial Rites,
        // Griselbrand, Iona, Elesh Norn — the dup was skipped).
        _alice.Zones.Graveyard.Count.Should().Be(2,
            "Bob's agent picked 2 cards from the revealed pile to graveyard.");
        _alice.Zones.Hand.Count.Should().Be(2, "the remaining 2 revealed go to Alice's hand.");
        // Library still holds the unrevealed cards (dup + filler in
        // arbitrary post-shuffle order).
        _alice.Zones.Library.Count.Should().Be(2);

        // The revealed-and-distributed cards are exactly the 4 distinct
        // names — confirm the dup did NOT get revealed (otherwise the
        // graveyard / hand combined count would exceed 4 by 1).
        var distributed = _alice.Zones.Graveyard.GetCards()
            .Concat(_alice.Zones.Hand.GetCards()).Select(c => c.Name).ToList();
        distributed.Should().HaveCount(4);
        distributed.Distinct().Should().HaveCount(4, "all four picks had distinct names.");
    }

    // ---------------------------------------------------------------
    // Caster declines early — opponent's pick clamps to revealed
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_CasterDeclinesAfterOnePick_RevealedPileGoesToGraveyard()
    {
        // Library has a single useful card; caster picks it then declines.
        var rites = new Sorcery("Unburial Rites", "{4}{B}");
        rites.SetOwner(_alice);
        _alice.Zones.Library.AddCard(rites);
        rites.SetZone(ZoneType.Library);

        // Add filler so the library isn't empty for subsequent prompts.
        for (int i = 0; i < 3; i++)
        {
            var c = new Instant($"Filler{i}", "{U}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AgentRegistry.Set(_alice, new PickOneThenDeclineAgent(rites));
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = GiftsUngivenFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // Only one card revealed → opponent's pick-2 clamps to "all
        // revealed go to graveyard" (do-as-much-as-possible).
        _alice.Zones.Graveyard.Count.Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Single().Name.Should().Be("Unburial Rites");
        _alice.Zones.Hand.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Illegal target at resolution → no-op
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_IllegalTarget_NoOps()
    {
        for (int i = 0; i < 4; i++)
        {
            var c = new Instant($"Card{i}", "{U}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        // Resolver returns a non-Player object → CR 608.2b illegal target.
        var def = GiftsUngivenFactory.BuildDefinition(_alice, raw => "not a player");
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { new object() } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.Count.Should().Be(4, "no search happened — library untouched.");
        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.Zones.Graveyard.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Small-library — fewer than 4 candidates, no crash
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_LibraryHasOnlyTwoCards_BothRevealed_BothToGraveyard()
    {
        var a = new Sorcery("A", "{B}");
        var b = new Sorcery("B", "{B}");
        foreach (var c in new ICard[] { a, b })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = GiftsUngivenFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // Only 2 revealed → both go to graveyard (clamp), hand empty.
        _alice.Zones.Graveyard.Count.Should().Be(2);
        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.Zones.Library.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Empty library — clean no-op, library still shuffled (per CR 701.20a)
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_EmptyLibrary_NoCrashNoMutation()
    {
        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = GiftsUngivenFactory.BuildDefinition(_alice, raw => raw);
        var act = () =>
        {
            var effects = def.EffectFactory(new ChosenSpellParams(
                ModeIndex: null, X: null,
                Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
                Mana: ManaPayment.Empty));
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Graveyard.Count.Should().Be(0);
        _alice.Zones.Hand.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Local test agent — pick a specific card once, then decline.
    // ---------------------------------------------------------------

    private sealed class PickOneThenDeclineAgent : IPlayerAgent
    {
        private readonly ICard _target;
        private bool _picked;

        public PickOneThenDeclineAgent(ICard target) { _target = target; }

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx, IReadOnlyList<ICard> candidates,
            string kindLabel, CancellationToken ct = default)
        {
            if (!_picked && candidates.Contains(_target))
            {
                _picked = true;
                return Task.FromResult<ICard?>(_target);
            }
            return Task.FromResult<ICard?>(null);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int m, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int n, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest req, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard src, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> a, IReadOnlyList<Creature> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
