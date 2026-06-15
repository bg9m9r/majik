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
/// Tests for Fabricate (Mirrodin, {2}{U}, Sorcery).
///
/// "Search your library for an artifact card, reveal it, put it into your
/// hand, then shuffle." (CR 701.19a search + CR 701.20a post-search shuffle.)
///
/// Same tutor-to-hand shape as Sylvan Scrying; the distinguishing feature is
/// the predicate — it filters to ANY artifact card rather than lands. Shares
/// the engine's artifact predicate / agent prompt / pick→hand move via
/// <see cref="SearchSpellFactory.SearchLibrarySpell"/> with kind "artifact".
///
/// Coverage:
///  - Identity (mana cost {2}{U}) — the non-vanilla stat for this card.
///  - Resolve picks an artifact, leaving non-artifacts in the library.
///  - Resolve no-ops when the library has no artifact card.
///  - Resolve no-ops when the agent declines the find (CR 701.19a).
/// </summary>
[Trait("Color", "U")]
public class FabricateTests
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

    private static Artifact MakeArtifact(string name, Player owner)
    {
        var artifact = new Artifact(name, "{1}");
        artifact.SetOwner(owner);
        artifact.SetController(owner);
        return artifact;
    }

    [Fact]
    public void Identity_ManaCost()
    {
        var owner = new Player("A", 20);
        var card = FabricateFactory.Create(owner);

        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void Resolve_PicksAnArtifact()
    {
        // Library holds a single artifact (first candidate) plus a creature.
        // The deterministic agent picks the artifact and moves it to hand.
        var caster = new Player("A", 20);
        var sphere = MakeArtifact("Sphere of the Suns", caster);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(sphere);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FabricateFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Sphere of the Suns");
        caster.Zones.Library.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Grizzly Bears");
    }

    [Fact]
    public void Resolve_NoArtifactInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FabricateFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Grizzly Bears");
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist.
        var caster = new Player("A", 20);
        var sphere = MakeArtifact("Sphere of the Suns", caster);
        caster.Zones.Library.AddCard(sphere);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(FabricateFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Sphere of the Suns");
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns null),
    /// exercising the CR 701.19a "no card found" branch even when candidates
    /// exist. Only the library-pick hook is consulted by
    /// <see cref="SearchSpellFactory.SearchLibrarySpell"/>; the rest of the
    /// <see cref="IPlayerAgent"/> surface throws to flag accidental calls.
    /// </summary>
    private sealed class DeclineLibraryPickAgent : IPlayerAgent
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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> a, IReadOnlyList<Permanent> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
