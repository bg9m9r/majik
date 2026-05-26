using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Settle the Wreckage (Ixalan, {2}{W}{W}, Instant).
///
/// Oracle: "Exile all attacking creatures target player controls. That
/// player may search their library for that many basic land cards, put
/// them onto the battlefield tapped, then shuffle."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Exiles every attacking creature controlled by the target player;
///     non-attacking creatures + creatures controlled by others survive.
///   - Tutor offers up to N picks (N = attackers exiled). All picks land
///     tapped, controlled by the target player.
///   - N = 0 (target has no attackers) is a clean no-op.
///   - Declining the tutor mid-search is legal (CR 701.19a).
///   - Tutor is filtered to basic lands (nonbasic lands left in library).
/// </summary>
public class SettleTheWreckageTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        // AgentRegistry is process-wide; clear between tests.
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SettleTheWreckage_IsInstant_At2WW()
    {
        var s = SettleTheWreckageFactory.Create(_alice);

        s.Name.Should().Be("Settle the Wreckage");
        s.ManaCost.Should().Be("{2}{W}{W}");
        s.HasType(CardType.Instant).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SettleTheWreckage()
    {
        var card = NamedCardFactory.Create("Settle the Wreckage", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Settle the Wreckage");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile + tutor
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesAttackingCreaturesControlledByTarget_AndTutorsBasicLands()
    {
        // Bob is attacking Alice with two creatures. He also has a
        // non-attacking blocker that should survive. Alice also has a
        // creature on the field — Settle the Wreckage only touches the
        // target player's attackers, so Alice's creature survives.
        var bobAttacker1 = SeedCreature(_bob, "Bob-Knight");
        var bobAttacker2 = SeedCreature(_bob, "Bob-Soldier");
        var bobBlocker = SeedCreature(_bob, "Bob-Wall");
        var aliceCreature = SeedCreature(_alice, "Alice-Bear");

        // Two basics + one nonbasic in Bob's library — we expect exactly
        // two basics to be tutored (matching the two exiled attackers).
        var plains1 = SeedLibraryLand(_bob, "Plains");
        var plains2 = SeedLibraryLand(_bob, "Plains");
        var bayou = SeedLibraryLand(_bob, "Bayou"); // nonbasic — must NOT be picked

        // attackerLookup feeds the factory the live attacker list per
        // player — only Bob's attackers should be exposed for the test.
        var attackerLookup = (Player p) => (IReadOnlyList<Creature>)(
            p == _bob ? new[] { bobAttacker1, bobAttacker2 } : Array.Empty<Creature>());

        ResolveSettle(targetPlayer: _bob, attackerLookup);

        // Both attackers exiled.
        bobAttacker1.Zone.Should().Be(ZoneType.Exile);
        bobAttacker2.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(new ICard[] { bobAttacker1, bobAttacker2 });

        // Non-attacking + opponent's creature untouched.
        bobBlocker.Zone.Should().Be(ZoneType.Battlefield);
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield);

        // Two basics picked, both onto Bob's battlefield, tapped.
        var basicsOnField = _bob.Zones.Battlefield.GetCards()
            .Where(c => c.Name == "Plains").ToList();
        basicsOnField.Should().HaveCount(2);
        basicsOnField.Should().AllSatisfy(p =>
        {
            p.Controller.Should().BeSameAs(_bob);
            ((Permanent)p).IsTapped.Should().BeTrue();
        });

        // Nonbasic still in the library (filter excluded it).
        _bob.Zones.Library.GetCards().Should().Contain(bayou);
    }

    [Fact]
    public void Resolve_NoAttackers_IsCleanNoOp_NoTutorOffered()
    {
        // Bob has a creature, but it isn't attacking; no exile, no tutor.
        var bobCreature = SeedCreature(_bob, "Bob-Wall");
        var plains = SeedLibraryLand(_bob, "Plains");

        // An agent that throws on any prompt — if the factory short-
        // circuits N = 0, it must never be invoked.
        AgentRegistry.Set(_bob, new ThrowingPickAgent());

        var attackerLookup = (Player _) => (IReadOnlyList<Creature>)Array.Empty<Creature>();

        ResolveSettle(targetPlayer: _bob, attackerLookup);

        bobCreature.Zone.Should().Be(ZoneType.Battlefield);
        plains.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(plains);
    }

    [Fact]
    public void Resolve_TargetMayDeclineTutor_AttackersStillExiled()
    {
        var bobAttacker = SeedCreature(_bob, "Bob-Knight");
        var plains = SeedLibraryLand(_bob, "Plains");

        AgentRegistry.Set(_bob, new DecliningPickAgent());

        var attackerLookup = (Player p) => (IReadOnlyList<Creature>)(
            p == _bob ? new[] { bobAttacker } : Array.Empty<Creature>());

        ResolveSettle(targetPlayer: _bob, attackerLookup);

        // Attacker exiled regardless of the tutor decline.
        bobAttacker.Zone.Should().Be(ZoneType.Exile);
        // Plains untouched — Bob declined the search.
        plains.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(plains);
    }

    [Fact]
    public void Resolve_NoBasicsInLibrary_StillExilesAttackers()
    {
        var bobAttacker = SeedCreature(_bob, "Bob-Knight");
        // Library has only a nonbasic — tutor finds nothing legal.
        var bayou = SeedLibraryLand(_bob, "Bayou");

        var attackerLookup = (Player p) => (IReadOnlyList<Creature>)(
            p == _bob ? new[] { bobAttacker } : Array.Empty<Creature>());

        ResolveSettle(targetPlayer: _bob, attackerLookup);

        bobAttacker.Zone.Should().Be(ZoneType.Exile);
        bayou.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Battlefield.GetCards().Where(c => c.HasType(CardType.Land))
            .Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ResolveSettle(
        Player targetPlayer,
        Func<Player, IReadOnlyList<Creature>> attackerLookup)
    {
        var def = SettleTheWreckageFactory.BuildSpellDefinition(
            resolver: t => t, // identity resolver — test passes a live Player as the chosen target
            attackerLookup: attackerLookup);

        // SpellDefinition has a single 1..1 "target player" request;
        // simulate the chosen-targets DTO directly.
        var chosen = new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: 0,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetPlayer } },
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Land SeedLibraryLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Library.AddCard(l);
        l.SetZone(ZoneType.Library);
        return l;
    }

    private sealed class DecliningPickAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);

        public Task<PriorityAction> ChoosePriorityActionAsync(Majik.Core.Game.GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(Majik.Core.Game.GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(Majik.Core.Game.GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingPickAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel,
            CancellationToken ct = default)
            => throw new InvalidOperationException(
                "Tutor should not be offered when no attackers were exiled.");

        public Task<PriorityAction> ChoosePriorityActionAsync(Majik.Core.Game.GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(Majik.Core.Game.GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(Majik.Core.Game.GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
