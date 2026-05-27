using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Archive Trap (Zendikar, {3}{U}{U}).
///
/// Instant — Trap. Oracle text:
///   "If an opponent searched their library this turn, you may cast this
///    spell without paying its mana cost.
///    Target opponent puts the top thirteen cards of their library into
///    their graveyard."
///
/// The pillar Modern mill finisher — combos with Ghost Quarter / Field of
/// Ruin / Path to Exile to fire for free. Coverage shape matches the
/// Brain Freeze / Glimpse the Unthinkable "target player mills N" SpellDef
/// pattern.
///
/// ## Implemented (v1)
/// - Instant {3}{U}{U} (Blue) card shape with owner / controller wired.
///   The "— Trap" subtype is omitted (the engine has no per-card trap
///   subtype slot today; Trap is flavour without rules text).
/// - <b>Mill 13 target opponent</b> — <see cref="BuildDefinition"/> declares
///   a single 1..1 "target opponent" <see cref="TargetRequest"/> and on
///   resolve mills <see cref="MillCount"/> cards from the chosen target
///   via <see cref="MillAction.Apply"/> (CR 701.13). When the chosen
///   target token does not resolve to a <see cref="Player"/> (or resolves
///   to the caster) the effect no-ops per CR 608.2b (illegal target at
///   resolution).
///
/// ## Deferred (v1 gaps)
/// - <b>Alternative cost (CR 118.9)</b>: "If an opponent searched their
///   library this turn, you may cast this spell without paying its mana
///   cost." The engine does not yet track "opponent searched their
///   library this turn" — no <c>LibrarySearchEvent</c> /
///   <c>TurnState.OpponentSearchedLibraryThisTurn</c> bookkeeping exists.
///   The alternative-cost arm is therefore not wired here; the spell can
///   only be cast at its printed {3}{U}{U} for now. When the library-search
///   tracking surface lands, this factory should add an
///   <see cref="Majik.Core.Costs.AlternativeCost"/> with a predicate over
///   that flag and route the cast through it.
/// - <b>Opponent enforcement</b>: target list is unconstrained "target
///   opponent"; the agent's pick is honoured but resolution gates on the
///   chosen player not being the caster (defensive check — same posture
///   the rest of "target opponent" v1 factories take).
/// </summary>
[CardName("Archive Trap")]
public static class ArchiveTrapFactory
{
    public const string CardName = "Archive Trap";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int MillCount = 13;

    /// <summary>
    /// Construct Archive Trap as an Instant card. Suitable for shape /
    /// dispatcher tests; the resolve-time spell body lives in
    /// <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>
    /// Build the "target opponent mills thirteen cards" SpellDefinition.
    /// </summary>
    /// <param name="caster">The spell's controller — used to validate
    /// that the chosen target is not the caster themselves (CR 109.1 —
    /// "opponent" excludes self).</param>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster (expected to yield a <see cref="Player"/>). When the
    /// resolver returns anything that isn't a <see cref="Player"/>, or
    /// returns the caster, the effect no-ops per CR 608.2b (illegal target
    /// at resolution).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — target opponent mills {MillCount} cards",
                        () =>
                        {
                            if (resolved is not Player target) return;
                            // CR 109.1 — "opponent" excludes the caster.
                            if (ReferenceEquals(target, caster)) return;
                            MillAction.Apply(target, MillCount);
                        }),
                };
            });
    }
}
