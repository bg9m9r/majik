using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Core.Diagnostics;

namespace Majik.Bot;

/// <summary>
/// Configuration for a single <see cref="BotPlayerAgent"/> instance.
///
/// <para><c>ArchetypeName</c> must match a key registered in
/// <see cref="Decks.BotDeckCatalog"/>. <c>BotDeckValidator</c> verifies this
/// at startup so a typo doesn't fail at match start.</para>
///
/// <para><c>SearchDepth</c> bounds the minimax depth in
/// <c>Combat.CombatSearch</c>. Default 2 (my attackers x their blocks).
/// Raising it grows runtime exponentially.</para>
///
/// <para><c>RandomSeed</c> drives the per-agent <see cref="System.Random"/>
/// used for tie-breaks. Same seed + same engine state = same decision.</para>
///
/// <para><c>Strategy</c> selects the <see cref="IBotStrategy"/> implementation:
/// <c>"heuristic"</c> uses the pure-heuristic strategy; <c>"mcts"</c> uses
/// MCTS-backed combat search with heuristic fallback for all other prompts.</para>
///
/// <para><c>WeightsOverride</c> optional. When non-null, the bot uses this
/// explicit <see cref="ArchetypeWeights"/> vector directly instead of calling
/// <c>ArchetypeWeights.ForArchetype(ArchetypeName)</c>. Null preserves the
/// default archetype-lookup behaviour so existing production code is unaffected.
/// Primarily used by the self-play weight tuner to inject candidate weight
/// vectors into individual bots without changing the archetype mapping.</para>
///
/// <para><c>DecisionSink</c> optional. When non-null, EV-scored policies
/// (PriorityPolicy, ActivatedAbilityPolicy via priority pump, CombatSearch)
/// emit a structured <see cref="BotDecision"/> for each choice. Defaults to
/// no-op so prod takes zero overhead unless the server flips the
/// <c>Bot:DecisionLogging:Enabled</c> flag.</para>
///
/// <para><c>VanillaShellTracker</c> optional. When non-null, the bot consults
/// it on every decision touching a vanilla-shell card (see
/// <see cref="Majik.Core.Cards.ICard.IsVanillaShell"/>) — the first time a
/// given name is seen, a structured WARN is logged and an
/// <see cref="Majik.Core.Events.UnimplementedCardEncounteredEvent"/> fires
/// on the tracker's event bus. Defaults to no-op (the bot still
/// deprioritises vanilla shells in EV scoring, just silently).</para>
///
/// <para><c>SimCombatBudgetMs</c> limits the opponent's <see cref="Combat.CombatPolicy"/>
/// budget when this config is used for a sandbox opponent inside MCTS.
/// The default (null) uses the production default (~800 ms). Set to a small
/// value (e.g. 20) for sandbox-opponent agents so that an adversarial
/// HeuristicStrategy blocking call at every MCTS node does not dominate
/// search time. This field is only consulted by <see cref="Search.EngineSimulator"/>;
/// it has no effect on the live (top-level) BotPlayerAgent.</para>
///
/// <para><c>MaxMctsIterations</c> overrides the default MCTS iteration cap
/// (200) when <c>Strategy="mcts"</c>. Use a small value (e.g. 50) in integration
/// tests to keep the test suite runtime reasonable. A null value uses the
/// production default.</para>
///
/// <para><c>MaxMctsBudgetMs</c> overrides the wall-clock budget per MCTS call
/// (default 1500 ms) when <c>Strategy="mcts"</c>. Use a small value (e.g. 200)
/// in integration tests so that each priority decision finishes quickly. The
/// search will still run up to <c>MaxMctsIterations</c> iterations but will
/// cut off early if the budget is exhausted. A null value uses the production
/// default.</para>
///
/// <para><c>PrioritySearchEnabled</c> controls whether the MCTS search is
/// used for priority decisions when <c>Strategy="mcts"</c>. Default true
/// (search is used). Set to false in tests to skip the priority MCTS and
/// use the inner heuristic instead — useful when the priority MCTS sandbox
/// games are slow (e.g., because the sandbox heuristic hits the priority-loop
/// safety on unimplemented cards). The combat search (DeclareAttackers /
/// DeclareBlockers) is unaffected by this flag.</para>
///
/// <para><c>OpponentArchetype</c> optional. When set to a known archetype name
/// (a key in <see cref="Decks.BotDeckCatalog"/>), <see cref="Search.SearchStrategy"/>
/// runs <em>determinized</em> MCTS: the search samples the opponent's hidden zones
/// from that archetype's decklist across K worlds and votes by summed robust child
/// (see <see cref="Search.DeterminizedSearch"/>). When null (the default), the bot
/// uses today's perfect-info single-tree search — the opponent's hidden zones are
/// left exactly as captured. Null is the production-safe default: an unknown
/// opponent must NOT route through determinization, which would invent a wrong
/// hidden world.</para>
///
/// <para><c>InferOpponentArchetype</c> optional (default false). When true AND
/// <c>OpponentArchetype</c> is null, <see cref="Search.SearchStrategy"/> reads the
/// opponent's PUBLIC cards from the live <see cref="Majik.Core.Game.GameContext"/>,
/// infers a normalized belief over the curated archetypes
/// (<see cref="OpponentModel.ArchetypeInferencer"/>), allocates the determinized
/// worlds across that belief (<see cref="OpponentModel.WorldAllocator"/>), and runs
/// belief-driven determinized search (<see cref="Search.DeterminizedSearch.RunBelief"/>).
/// This is the "honest-vs-human" path: the opponent's deck is unknown, so it is
/// inferred from their revealed public cards rather than assumed. Ignored when an
/// explicit <c>OpponentArchetype</c> is set (the known-archetype path takes
/// precedence). Default false preserves today's perfect-info behaviour for every
/// existing caller.</para>
///
/// <para><c>RiskVoteThreshold</c> optional (default null). Catastrophe threshold
/// for the risk-aware two-tier vote in <see cref="Search.DeterminizedSearch"/>:
/// determinized lines whose worst per-world mean falls at or below this value are
/// demoted below safe lines. Null resolves to
/// <see cref="Search.DeterminizedSearch.DefaultCatastropheThreshold"/> (-500);
/// <see cref="double.NegativeInfinity"/> is the kill switch — it disables the
/// risk filter entirely (no line can score at or below it). Only consulted by
/// <see cref="Search.SearchStrategy"/> on the determinized paths; the perfect-info
/// search ignores it.</para>
///
/// <para><c>SearchConcurrency</c> optional (default null = ungated, today's
/// behaviour). When non-null and <c>Strategy="mcts"</c>, every top-level LIVE
/// search (<see cref="Search.SearchStrategy"/>'s PickAttackers /
/// PickPriorityAction) must hold a permit on the PROCESS-WIDE
/// <see cref="Search.SearchConcurrencyGate"/> — overlapping searches from
/// concurrent bot matches QUEUE instead of splitting the CPU, so each runs at
/// full strength (the 1-vCPU prod motivation; see ServerBotOptions). The wait
/// is bounded (<see cref="Search.SearchGate.DefaultTimeout"/>); on timeout the
/// pick falls back to the heuristic decision. The gate is shared process-wide
/// with first-configured-permits-wins semantics. Null keeps unit tests, the
/// PARALLEL strength probes, and sim-internal searches completely ungated.
/// Heuristic-strategy decisions are never gated (they are microseconds).</para>
///
/// <para><c>RolloutDepth</c> optional (default null = <c>FullTurnPlus</c>, today's
/// behaviour). When <c>Strategy="mcts"</c>, selects how far each MCTS rollout
/// plays the sandbox out before evaluating (see
/// <see cref="Search.RolloutDepth"/>): <c>"LeafEval"</c> (no playout — eval at
/// the decision point), <c>"EndOfTurn"</c> (remainder of the current turn only)
/// or <c>"FullTurnPlus"</c> (current turn plus one full turn — the default).
/// Parsed case-insensitively by <see cref="Search.SearchStrategy"/> at
/// construction; an unknown value throws <see cref="ArgumentException"/>
/// (fail-fast, mirroring the strategy-name validation). This is the #2596
/// rollout-cost lever — the live flip of a probe-gate winner is config-only
/// (<c>Bot__RolloutDepth</c>).</para>
///
/// <para><c>TreeStateReuse</c> optional (default null = off, today's
/// behaviour). When <c>Strategy="mcts"</c>, enables tree-state reuse inside
/// each search (see <see cref="Search.MctsConfig"/>): tree-node states are
/// snapshot-cached and each iteration expands / rolls out from the nearest
/// cached ancestor instead of replaying the whole root path —
/// iteration-for-iteration EQUIVALENT to the root-replay loop (equivalence-
/// gated), only cheaper. Resolved by <see cref="Search.SearchStrategy"/> at
/// construction (null → false) and inherited by every per-world determinized
/// search. The live flip of the probe-gate winner is config-only
/// (<c>Bot__TreeStateReuse</c>).</para>
/// </summary>
public sealed record BotConfig(
    string ArchetypeName,
    int SearchDepth = 2,
    int RandomSeed = 0,
    string Strategy = "heuristic",
    IBotDecisionSink? DecisionSink = null,
    VanillaShellTracker? VanillaShellTracker = null,
    int? SimCombatBudgetMs = null,
    int? MaxMctsIterations = null,
    int? MaxMctsBudgetMs = null,
    bool PrioritySearchEnabled = true,
    ArchetypeWeights? WeightsOverride = null,
    string? OpponentArchetype = null,
    bool InferOpponentArchetype = false,
    double? RiskVoteThreshold = null,
    int? SearchConcurrency = null,
    string? RolloutDepth = null,
    bool? TreeStateReuse = null);
