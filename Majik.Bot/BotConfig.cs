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
/// </summary>
public sealed record BotConfig(
    string ArchetypeName,
    int SearchDepth = 2,
    int RandomSeed = 0,
    string Strategy = "heuristic");
