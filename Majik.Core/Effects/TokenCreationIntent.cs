using Majik.Core.Players;
using Majik.Core.Tokens;

namespace Majik.Core.Effects;

/// <summary>
/// CR 111.10 / CR 614 — "would-create-tokens" intent. An effect that would
/// create one or more tokens publishes this through
/// <see cref="ReplacementBus"/> before minting; replacements can rewrite
/// <see cref="Count"/> (Doubling Season, Parallel Lives, Anointed Procession
/// — "creates twice that many of those tokens instead") or cancel the
/// creation entirely (return <c>null</c> from <c>Replace</c>).
///
/// Callers route token creation through <see cref="TokensService.Create"/>
/// (or the <c>TokenFactory.CreateOnBattlefield</c> overload that takes a
/// <see cref="ReplacementBus"/>), then mint the post-replacement
/// <see cref="Count"/> copies of the spec.
///
/// CR 616.1c — each registered replacement fires at most once per intent,
/// so two copies of Parallel Lives stack as 1 → 2 → 4 (each doubles
/// independently), and Parallel Lives + Anointed Procession stack as
/// 1 → 2 → 4 multiplicatively.
/// </summary>
public sealed record TokenCreationIntent(
    Player Controller,
    TokenFactory.TokenSpec Spec,
    int Count);
