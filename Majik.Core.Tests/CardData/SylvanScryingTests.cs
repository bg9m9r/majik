using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sylvan Scrying (Mirrodin, {1}{G}, Sorcery).
///
/// "Search your library for a land card, reveal it, put it into your
/// hand, then shuffle." (CR 701.19a)
///
/// Distinguishing feature vs. Cultivate / Rampant Growth: tutors ANY land
/// — basic OR nonbasic. The Tron-enabling primitive.
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks any land (basic and nonbasic candidates both eligible).
///  - Resolve no-ops when the library has no land card.
///  - Resolve no-ops when the agent declines the find (returns null) —
///    CR 701.19a explicitly permits declining.
///  - The (non-land) cards in the library are not touched.
/// </summary>
public class SylvanScryingTests
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

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    private static Land MakeNonbasicLand(string name, Player owner)
    {
        var land = new Land(name, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = SylvanScryingFactory.Create(owner);

        card.Name.Should().Be("Sylvan Scrying");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SylvanScrying()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Sylvan Scrying", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sylvan Scrying");
        card.ManaCost.Should().Be("{1}{G}");
    }

    [Fact]
    public void Resolve_PicksAnyLand_IncludingNonbasic()
    {
        // Tron use case: library holds a Forest, a Mountain, and Urza's
        // Tower (nonbasic). The deterministic agent picks the first
        // land candidate — wire the library so the nonbasic land is
        // first, proving the predicate accepts it.
        var caster = new Player("A", 20);
        var tower = MakeNonbasicLand("Urza's Tower", caster);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        var mountain = MakeBasicLand("Mountain", caster, CardSubtype.Mountain);
        caster.Zones.Library.AddCard(tower);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(mountain);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SylvanScryingFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Urza's Tower");
        caster.Zones.Library.GetCards().Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Forest", "Mountain" });
    }

    [Fact]
    public void Resolve_NoLandInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SylvanScryingFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Grizzly Bears");
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist. The DeclineLibraryPickAgent always returns
        // null from ChooseLibraryPickAsync, exercising that branch.
        var caster = new Player("A", 20);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(SylvanScryingFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Forest");
    }

    [Fact]
    public void Resolve_NonLandCardsInLibraryAreNotPicked()
    {
        // Predicate filters to lands only — adjacent creature stays
        // in the library when a single Forest is also present.
        var caster = new Player("A", 20);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(grizzly);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SylvanScryingFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Forest");
        caster.Zones.Library.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Grizzly Bears");
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns
    /// null), exercising the CR 701.19a "no card found" branch even
    /// when candidates exist. Only the library-pick hook is exercised
    /// by SearchSpellFactory.SearchLibrarySpell; the rest of the
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

        // ---- unused decision hooks ----------------------------------------
        // The Sylvan Scrying resolve closure only consults
        // ChooseLibraryPickAsync. Any other call indicates the engine
        // started asking for something new — fail loudly.
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
