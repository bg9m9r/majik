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
/// Tests for Vampiric Tutor ({B}, Instant).
///
/// "Search your library for a card, then shuffle. Put that card on top.
///  You lose 2 life." (CR 701.19a / 701.19c / 119.3)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks any card from library and places it at index 0;
///    controller loses 2 life.
///  - Empty library — no tutor possible; controller still loses 2 life
///    (the cost-side life-loss is an unconditional instruction).
///  - Agent decline (CR 701.19a) → no tutor; controller still loses 2.
/// </summary>
public class VampiricTutorTests
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
        var card = VampiricTutorFactory.Create(owner);

        card.Name.Should().Be("Vampiric Tutor");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VampiricTutor()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Vampiric Tutor", owner);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vampiric Tutor");
        card.ManaCost.Should().Be("{B}");
    }

    [Fact]
    public void Resolve_PicksAnyCard_PlacesOnTopOfLibrary_AndLoses2Life()
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

        Resolve(VampiricTutorFactory.BuildSpellDefinition(caster));

        // Hand untouched — Vampiric Tutor goes to top of library, not hand.
        caster.Zones.Hand.GetCards().Should().BeEmpty();

        // Library still holds all three; Forest must be at index 0.
        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(3);
        libCards[0].Name.Should().Be("Forest");
        // Non-picked cards retain their relative order.
        libCards.Skip(1).Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Grizzly Bears", "Wrath of God" });

        // CR 119.3 — controller's life total drops by 2.
        caster.LifeTotal.Should().Be(18);
        caster.LifeLostThisTurn.Should().Be(2);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoTutor_StillLoses2Life()
    {
        // CR 701.19a — no card found is fine. The "You lose 2 life"
        // clause is a separate resolve instruction and still fires.
        var caster = new Player("A", 20);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(VampiricTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Library.GetCards().Should().BeEmpty();
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.LifeTotal.Should().Be(18);
        caster.LifeLostThisTurn.Should().Be(2);
    }

    [Fact]
    public void Resolve_AgentDeclines_NoTutor_StillLoses2Life()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist. DeclineLibraryPickAgent returns null. The
        // life-loss instruction still fires.
        var caster = new Player("A", 20);
        var brainstorm = new Instant("Brainstorm", "U");
        brainstorm.SetOwner(caster); brainstorm.SetController(caster);
        caster.Zones.Library.AddCard(brainstorm);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(VampiricTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Brainstorm");
        caster.LifeTotal.Should().Be(18);
        caster.LifeLostThisTurn.Should().Be(2);
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns
    /// null), exercising the CR 701.19a "no card found" branch even
    /// when candidates exist. Only the library-pick hook is exercised
    /// by Vampiric Tutor's resolve closure; the rest of the
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
