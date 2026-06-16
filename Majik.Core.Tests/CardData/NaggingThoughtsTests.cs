using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Behavioural tests for Nagging Thoughts (Shadows over Innistrad, {1}{U},
/// Sorcery) — the look-top-two split-hand/graveyard SPELL verb.
///
/// Oracle text (verified against Scryfall):
///   "Look at the top two cards of your library. Put one of them into your
///    hand and the other into your graveyard.
///    Madness {1}{U} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// The spell body is the shared declarative verb
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.LibrarySpellFactory"/>'s
/// <c>LookAtTopPutOneInHandSpell(n: 2, restDestination: Graveyard)</c> —
/// "look at the top N, agent picks one for the HAND, the rest to the
/// GRAVEYARD" (CR 701.15 look-at). Nagging Thoughts binds to it through
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.LookAtTopPutOneInHandTemplate"/>
/// on the production cast path (<see cref="OracleSpellBinder.Bind"/>) and is
/// catalogued for intrinsic Madness at {1}{U} via <see cref="MadnessCatalog"/>.
///
/// These tests pin the RESOLUTION behaviour of that verb (the bind-only
/// assertion in <c>MadnessCardBodiesRemainingTests</c> only checked the def is
/// non-null): the two split destinations (hand vs graveyard), the genuine
/// agent pick, the deterministic no-agent default, and the short-library /
/// empty-library graceful degradation — distinct from the scry / surveil /
/// bottom-the-other (Sleight of Hand) verbs.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class NaggingThoughtsTests : IDisposable
{
    private const string OracleText =
        "Look at the top two cards of your library. Put one of them into your " +
        "hand and the other into your graveyard.\nMadness {1}{U}";

    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    private static SpellDefinition BindNaggingThoughts(Player caster) =>
        OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Nagging Thoughts",
                ManaCost = "{1}{U}",
                OracleText = OracleText,
            },
            caster, raw => raw, stack: null)
        ?? throw new System.InvalidOperationException("Nagging Thoughts failed to bind.");

    private static void Resolve(SpellDefinition def, Player caster)
    {
        var prms = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(prms))
        {
            effect.Execute();
        }
    }

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    [Fact]
    public void NaggingThoughts_Binds_ViaLookAtTopPutOneInHand()
    {
        var def = BindNaggingThoughts(_alice);

        // A self-resolving cantrip — no target slots, no modes (CR 701.15).
        def.TargetRequests.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void NaggingThoughts_CataloguedForMadness_AtPrintedCost()
    {
        // CR 702.35 — the card is intrinsically madness-enabled at {1}{U}
        // through the shared MadnessCatalog (the discard funnel handles the
        // re-cast; the body here is the resolve verb only).
        var card = NamedCardFactory.Create("Nagging Thoughts", _alice);
        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{1}{U}"));
    }

    [Fact]
    public void NaggingThoughts_NoAgent_TopToHand_SecondToGraveyard()
    {
        // Library: [top, second, third]. No agent registered → the verb's
        // deterministic default keeps the first eligible (top) for the HAND
        // and sends the OTHER revealed card (second) to the GRAVEYARD. The
        // third card (below the look-2 window) is untouched on top of the
        // remaining library.
        var top = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third = SeedLibraryCard("Third");

        Resolve(BindNaggingThoughts(_alice), _alice);

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { second });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { third });
        top.Zone.Should().Be(ZoneType.Hand);
        second.Zone.Should().Be(ZoneType.Graveyard);
        third.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void NaggingThoughts_AgentPicksSecond_SecondToHand_TopToGraveyard()
    {
        // Library: [top, second, third]. The controller actively reaches for
        // the deeper card (`second`) — that goes to HAND; the OTHER revealed
        // card (`top`) goes to the GRAVEYARD. Models a remote / human seat
        // keeping the better card. This is the discriminating split: unlike
        // Sleight of Hand the leftover goes to the GRAVEYARD, not the library
        // bottom.
        var top = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third = SeedLibraryCard("Third");

        AgentRegistry.Set(_alice, new PickRevealedByNameAgent("Second"));

        Resolve(BindNaggingThoughts(_alice), _alice);

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { second });
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { third });
        second.Zone.Should().Be(ZoneType.Hand);
        top.Zone.Should().Be(ZoneType.Graveyard);
        third.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void NaggingThoughts_SingleCardLibrary_TakesIt_NothingToGraveyard()
    {
        // Library has one card. The look-2 window shrinks to [a] (CR 121.2);
        // that card goes to hand and there is no "other" to bin.
        var a = SeedLibraryCard("A");

        Resolve(BindNaggingThoughts(_alice), _alice);

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        a.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void NaggingThoughts_EmptyLibrary_NoOp()
    {
        // Empty library — the verb short-circuits (nothing revealed). Nagging
        // Thoughts has no "draw" clause, so no draw-from-empty SBA fires.
        var def = BindNaggingThoughts(_alice);

        System.Action act = () => Resolve(def, _alice);

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    /// <summary>
    /// Test-only agent resolving <see cref="IPlayerAgent.ChooseFromRevealedAsync"/>
    /// by matching a revealed card's <see cref="ICard.Name"/> (falling back to
    /// the first eligible). Other decision hooks throw to flag accidental calls.
    /// </summary>
    private sealed class PickRevealedByNameAgent : IPlayerAgent
    {
        private readonly string _name;
        public PickRevealedByNameAgent(string name) => _name = name;

        public Task<ICard?> ChooseFromRevealedAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> revealed,
            IReadOnlyList<ICard> eligible,
            bool optional,
            string label,
            CancellationToken ct = default)
        {
            var match = eligible.FirstOrDefault(c => c.Name == _name)
                        ?? (eligible.Count > 0 ? eligible[0] : null);
            return Task.FromResult<ICard?>(match);
        }

        // ---- unused decision hooks ---------------------------------------
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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> a, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> a, IReadOnlyList<Permanent> b, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
