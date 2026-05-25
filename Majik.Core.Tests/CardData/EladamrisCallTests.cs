using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Eladamri's Call (Planeshift / MH2, {G}{W}, Instant).
///
/// "Search your library for a creature card, reveal that card, put it
///  into your hand, then shuffle." (CR 701.19a)
///
/// Distinguishing feature vs. Worldly Tutor / Diabolic Tutor: instant-
/// speed creature-only tutor at 2 mana. The Bant/Naya toolbox tutor.
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks any creature.
///  - Resolve no-ops when the library has no creature card.
///  - Resolve no-ops when the agent declines the find (CR 701.19a).
///  - Non-creature cards in the library are not touched.
/// </summary>
public class EladamrisCallTests
{
    private static ChosenSpellParams EmptyChoices() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(EmptyChoices()))
        {
            fx.Execute();
        }
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = EladamrisCallFactory.Create(owner);

        card.Name.Should().Be("Eladamri's Call");
        card.ManaCost.Should().Be("{G}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EladamrisCall()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Eladamri's Call", owner);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Eladamri's Call");
        card.ManaCost.Should().Be("{G}{W}");
    }

    [Fact]
    public void Resolve_PicksCreatureCard()
    {
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(caster);
        bear.SetController(caster);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(caster);
        caster.Zones.Library.AddCard(bolt);
        caster.Zones.Library.AddCard(bear);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(EladamrisCallFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Grizzly Bears");
        caster.Zones.Library.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Lightning Bolt",
                "non-creature cards stay in the library — predicate filters to creatures");
    }

    [Fact]
    public void Resolve_NoCreatureInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(caster);
        caster.Zones.Library.AddCard(bolt);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(EladamrisCallFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Lightning Bolt");
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist.
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(caster);
        bear.SetController(caster);
        caster.Zones.Library.AddCard(bear);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgentForEladamri());

        Resolve(EladamrisCallFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Grizzly Bears");
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns
    /// null), exercising the CR 701.19a "no card found" branch even
    /// when candidates exist. Local copy mirrors the same shape used by
    /// <see cref="SylvanScryingTests"/>.
    /// </summary>
    private sealed class DeclineLibraryPickAgentForEladamri : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);

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
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
