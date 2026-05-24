using Majik.Bot.Diagnostics;
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
/// <c>"heuristic"</c> in v1; <c>"mcts"</c> reserved for v2.</para>
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
/// </summary>
public sealed record BotConfig(
    string ArchetypeName,
    int SearchDepth = 2,
    int RandomSeed = 0,
    string Strategy = "heuristic",
    IBotDecisionSink? DecisionSink = null,
    VanillaShellTracker? VanillaShellTracker = null);
