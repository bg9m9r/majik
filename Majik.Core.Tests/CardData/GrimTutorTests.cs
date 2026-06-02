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
/// Tests for Grim Tutor ({1}{B}{B}, Sorcery).
///
/// "Search your library for a card, put that card into your hand, then
///  shuffle. You lose 3 life." (CR 701.19a / 701.20a / 119.3)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks any card from library and puts it into hand;
///    controller loses 3 life.
///  - Empty library — no tutor possible; controller still loses 3 life
///    (the life-loss is an unconditional instruction).
///  - Agent decline (CR 701.19a) → no tutor; controller still loses 3.
/// </summary>
public class GrimTutorTests
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
        var card = GrimTutorFactory.Create(owner);

        card.Name.Should().Be("Grim Tutor");
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GrimTutor()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Grim Tutor", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Grim Tutor");
        card.ManaCost.Should().Be("{1}{B}{B}");
    }

    [Fact]
    public void Resolve_PicksAnyCard_PutsInHand_AndLoses3Life()
    {
        // Library order: Forest (deterministic pick — first), Bear, Wrath.
        // The pick is unfiltered — any card is eligible — so the
        // deterministic first-match agent returns the Forest.
        var caster = new Player("A", 20);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        var wrath = new Sorcery("Wrath of God", "2WW");
        wrath.SetOwner(caster); wrath.SetController(caster);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(bear);
        caster.Zones.Library.AddCard(wrath);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GrimTutorFactory.BuildSpellDefinition(caster));

        // Forest tutored to hand — Grim Tutor goes to hand, not top of library.
        caster.Zones.Hand.GetCards().Select(c => c.Name)
            .Should().ContainSingle().Which.Should().Be("Forest");

        // Library still holds the remaining two cards; Forest is gone.
        caster.Zones.Library.GetCards().Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Grizzly Bears", "Wrath of God" });

        // CR 119.3 — controller's life total drops by 3.
        caster.LifeTotal.Should().Be(17);
        caster.LifeLostThisTurn.Should().Be(3);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoTutor_StillLoses3Life()
    {
        // CR 701.19a — no card found is fine. The "You lose 3 life"
        // clause is a separate resolve instruction and still fires.
        var caster = new Player("A", 20);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GrimTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Library.GetCards().Should().BeEmpty();
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.LifeTotal.Should().Be(17);
        caster.LifeLostThisTurn.Should().Be(3);
    }

    [Fact]
    public void Resolve_AgentDeclines_NoTutor_StillLoses3Life()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist. DeclineLibraryPickAgent returns null. The
        // life-loss instruction still fires.
        var caster = new Player("A", 20);
        var brainstorm = new Instant("Brainstorm", "U");
        brainstorm.SetOwner(caster); brainstorm.SetController(caster);
        caster.Zones.Library.AddCard(brainstorm);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(GrimTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Brainstorm");
        caster.LifeTotal.Should().Be(17);
        caster.LifeLostThisTurn.Should().Be(3);
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns
    /// null), exercising the CR 701.19a "no card found" branch even
    /// when candidates exist. Only the library-pick hook is exercised
    /// by Grim Tutor's resolve closure; the rest of the
    /// <see cref="IPlayerAgent"/> surface throws to flag accidental
    /// calls from future engine changes.
    /// </summary>
    private sealed class DeclineLibraryPickAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);

        // ---- unused decision hooks -----------------------------------
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
