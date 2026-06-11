using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Regression coverage for the "spurious cast prompt" bug: casting a PERMANENT
/// (artifact / creature / enchantment / planeswalker) used to raise a stray
/// TARGET / DESTINATION prompt because the cast-time spell-definition resolver
/// (<see cref="ScryfallCardFactory.LookupSpellDefinition"/>, wired into prod via
/// <c>GameFacade.BuildSpellDefinitionResolver</c>) walked the instant/sorcery
/// <see cref="OracleSpellBinder"/> registry against the permanent's FULL oracle
/// text. That text includes the permanent's activated / triggered ability
/// clauses, which spuriously matched instant/sorcery templates:
///   * Walking Ballista — "...deals 1 damage to any target" → DamageAnyTarget,
///     a "target" TargetRequest.
///   * Agatha's Soul Cauldron (NO ETB at all) — "{T}: Exile target card from a
///     graveyard" → ExileFromGraveyard, a "target card in graveyard"
///     TargetRequest.
///
/// CR 608.3b — a permanent spell resolves by ENTERING THE BATTLEFIELD; its
/// abilities are bound at card-build time (KeywordBinder /
/// OracleTriggeredAbilityBinder / the ETB replacement chain), not by the
/// cast-time spell binder. So the prod resolver must return NO targeted
/// SpellDefinition for a permanent, and casting one must raise no target/mode
/// prompt.
/// </summary>
public class SpuriousCastPromptTests
{
    private readonly EmbeddedCardRepository _repo = new();

    /// <summary>The EXACT prod cast-time resolver — same call
    /// <c>GameFacade.BuildSpellDefinitionResolver</c> wires into TurnDriver.</summary>
    private SpellDefinition? ProdResolve(string name, Player caster)
        => new ScryfallCardFactory(_repo).LookupSpellDefinition(name, caster, raw => raw, stack: null);

    // -----------------------------------------------------------------------
    // Resolver layer — the prod binder must not synthesize a targeted spell
    // effect from a permanent's ability text.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Walking Ballista")]       // base 0/0 artifact creature, ping ability
    [InlineData("Agatha's Soul Cauldron")] // legendary artifact, NO ETB whatsoever
    public void Permanent_ProdResolver_ProducesNoTargetedSpellDefinition(string name)
    {
        var alice = new Player("Alice", 20);

        var def = ProdResolve(name, alice);

        // Either null (TurnDriver falls back to a vanilla no-target permanent
        // spell) — or, defensively, a def with NO target/mode requests. The
        // bug was a non-null def carrying a TargetRequest.
        if (def is not null)
        {
            def.TargetRequests.Should().BeEmpty(
                $"{name} is a permanent — its cast must collect no targets (CR 608.3b)");
            def.Modes.Should().BeEmpty(
                $"{name} is a permanent — its cast must offer no modes");
        }
    }

    [Fact]
    public void RealTargetedInstant_StillBindsWithTargets_NoRegression()
    {
        // Guard: the permanent skip must NOT break a legitimately-targeted
        // instant. Lightning Bolt's printed text IS its resolution effect.
        var alice = new Player("Alice", 20);

        var def = ProdResolve("Lightning Bolt", alice);

        def.Should().NotBeNull("an instant's oracle text is its resolution effect");
        def!.TargetRequests.Should().ContainSingle(
            "Lightning Bolt deals 3 damage to any target");
    }

    // -----------------------------------------------------------------------
    // Full cast path — drive SpellCastFlow.CastAsync with an agent that throws
    // on ANY target/mode/choice prompt. A spurious prompt fails loudly.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CastingWalkingBallista_PromptsOnlyForX_NeverForTargets()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = new ScryfallCardFactory(_repo).Create("Walking Ballista", alice);
        card.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(card);

        var def = ProdResolve("Walking Ballista", alice)
            ?? SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>());

        var agent = new NoTargetPromptAgent(xValue: 2);
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1, StepStateType.PreCombatMain, stack);

        // Must NOT throw — the only prompts allowed are X (it's an {X}{X} cast)
        // and mana. A target/mode prompt would throw inside the agent.
        await flow.CastAsync(alice, card, def, agent, ctx, preChosenMana: ManaPayment.Empty);

        stack.Count.Should().Be(1, "the Ballista spell is on the stack");
        agent.TargetPromptCount.Should().Be(0, "no target prompt may fire when casting a permanent");
        agent.ModePromptCount.Should().Be(0, "no mode prompt may fire when casting a permanent");
        agent.ChoosePromptCount.Should().Be(0, "no destination/choice prompt may fire when casting a permanent");
    }

    [Fact]
    public async Task CastingAgathasSoulCauldron_PromptsForNothing()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = new ScryfallCardFactory(_repo).Create("Agatha's Soul Cauldron", alice);
        card.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(card);

        var def = ProdResolve("Agatha's Soul Cauldron", alice)
            ?? SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>());

        var agent = new NoTargetPromptAgent(xValue: 0);
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1, StepStateType.PreCombatMain, stack);

        // Agatha has NO ETB / no X — any prompt at all is spurious.
        await flow.CastAsync(alice, card, def, agent, ctx, preChosenMana: ManaPayment.Empty);

        stack.Count.Should().Be(1);
        agent.TargetPromptCount.Should().Be(0);
        agent.ModePromptCount.Should().Be(0);
        agent.ChoosePromptCount.Should().Be(0);
        agent.XPromptCount.Should().Be(0, "Agatha has no variable X");
    }

    /// <summary>
    /// Records (and rejects) target/mode/choice prompts. Allows X (returns the
    /// configured value) and mana (pre-chosen, never reached). Any target,
    /// mode, or generic-choice prompt is a spurious cast prompt — counted so
    /// the assertions can fail with a clear message instead of the agent's
    /// own throw.
    /// </summary>
    private sealed class NoTargetPromptAgent : DelegatingAgent
    {
        private readonly int _x;
        public int TargetPromptCount { get; private set; }
        public int ModePromptCount { get; private set; }
        public int ChoosePromptCount { get; private set; }
        public int XPromptCount { get; private set; }

        public NoTargetPromptAgent(int xValue) => _x = xValue;

        public override Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            TargetPromptCount++;
            return Task.FromResult<IReadOnlyList<object>>(System.Array.Empty<object>());
        }

        public override Task<int> ChooseModeAsync(
            GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
        {
            ModePromptCount++;
            return Task.FromResult(0);
        }

        public override Task<IReadOnlyList<object>> ChooseAsync(
            GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        {
            ChoosePromptCount++;
            return Task.FromResult<IReadOnlyList<object>>(System.Array.Empty<object>());
        }

        public override Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        {
            XPromptCount++;
            return Task.FromResult(_x);
        }
    }
}
