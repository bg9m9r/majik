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
/// Tests for Mystical Tutor ({U}, Instant).
///
/// "Search your library for an instant or sorcery card, reveal it, put
/// it on top of your library, then shuffle." (CR 701.19a / 701.19c)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks an instant from library and places it at index 0.
///  - Resolve picks a sorcery from library and places it at index 0.
///  - Library has no instants/sorceries → resolve is a no-op (other
///    candidates left alone).
///  - Agent decline (returns null) → no-op even when candidates exist
///    (CR 701.19a explicitly allows declining).
/// </summary>
public class MysticalTutorTests
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

    private static Instant MakeInstant(string name, Player owner, string manaCost = "U")
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    private static Sorcery MakeSorcery(string name, Player owner, string manaCost = "1U")
    {
        var card = new Sorcery(name, manaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = MysticalTutorFactory.Create(owner);

        card.Name.Should().Be("Mystical Tutor");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MysticalTutor()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Mystical Tutor", owner);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Mystical Tutor");
        card.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void Resolve_PicksInstant_PlacesOnTopOfLibrary()
    {
        // Library contains a creature (filtered out), a Forest (filtered
        // out), and a Counterspell (eligible — Instant). Deterministic
        // agent picks the first eligible candidate.
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        var counterspell = MakeInstant("Counterspell", caster, "UU");
        caster.Zones.Library.AddCard(bear);
        caster.Zones.Library.AddCard(counterspell);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(MysticalTutorFactory.BuildSpellDefinition(caster));

        // Hand untouched — Mystical Tutor goes to top of library, not hand.
        caster.Zones.Hand.GetCards().Should().BeEmpty();

        // Library still holds both cards; Counterspell must be at index 0.
        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(2);
        libCards[0].Name.Should().Be("Counterspell");
        libCards[1].Name.Should().Be("Grizzly Bears");
    }

    [Fact]
    public void Resolve_PicksSorcery_PlacesOnTopOfLibrary()
    {
        var caster = new Player("A", 20);
        // Order in library: Bear (filtered), Wrath (eligible sorcery),
        // Mountain (filtered). Deterministic agent picks the first
        // eligible match — Wrath of God.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        var wrath = MakeSorcery("Wrath of God", caster, "2WW");
        var mountain = new Land("Mountain",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Mountain });
        mountain.SetOwner(caster); mountain.SetController(caster);
        caster.Zones.Library.AddCard(bear);
        caster.Zones.Library.AddCard(wrath);
        caster.Zones.Library.AddCard(mountain);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(MysticalTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();

        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(3);
        libCards[0].Name.Should().Be("Wrath of God");
        // Non-eligible cards retain their relative order.
        libCards.Skip(1).Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Grizzly Bears", "Mountain" });
    }

    [Fact]
    public void Resolve_NoInstantOrSorceryInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        caster.Zones.Library.AddCard(bear);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(MysticalTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        // Library order untouched — predicate matched nothing.
        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(2);
        libCards[0].Name.Should().Be("Grizzly Bears");
        libCards[1].Name.Should().Be("Forest");
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist. DeclineLibraryPickAgent always returns null
        // from ChooseLibraryPickAsync.
        var caster = new Player("A", 20);
        var brainstorm = MakeInstant("Brainstorm", caster, "U");
        caster.Zones.Library.AddCard(brainstorm);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(MysticalTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Brainstorm");
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns
    /// null), exercising the CR 701.19a "no card found" branch even
    /// when candidates exist. Only the library-pick hook is exercised
    /// by Mystical Tutor's resolve closure; the rest of the
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
