using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for March of Reckless Joy (Kamigawa: Neon Dynasty,
/// {X}{R}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, you may exile any number
///    of red cards from your hand. This spell costs {2} less to cast for
///    each card exiled this way.
///    Exile the top X cards of your library. You may play up to two of
///    those cards until the end of your next turn."
///
/// ## Implemented (v1)
///
/// - <b>Instant</b> at <c>{X}{R}</c>, owner/controller wired.
/// - <b>March additional cost (CR 601.2f + CR 117.7c)</b> — reuses
///   <see cref="MarchAdditionalCost"/> with <see cref="ManaColor.Red"/>.
///   The cost is OPTIONAL. For each red hand card exiled, the cast's
///   generic cost is reduced by {2}, floored at zero. Applied after X is
///   folded into Generic per
///   <see cref="Majik.Core.Game.SpellCastFlow.ComputeAndApplyTotalCost"/>,
///   so an {X=5}{R} cast with 2 red cards exiled reduces 5 → 1 generic
///   (the {R} pip is preserved).
/// - <b>Resolve — exile top X + play up to 2 until end of next turn</b>:
///   the resolve body moves the top X cards of the caster's library to
///   their exile zone (CR 701.21), then stamps a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) on <b>at most 2</b> of
///   those cards — the first two in library order. The remaining cards
///   beyond the cap are exiled but receive no grant, which is consistent
///   with "you may play up to TWO" (CR 118.9). If the library has fewer
///   than X cards, the resolve exiles what is available.
/// - <b>"Until the end of your next turn" cleanup</b>: when an
///   <see cref="IEventBus"/> is supplied, schedules a
///   <see cref="StepStartedEvent"/> handler that counts Cleanup steps
///   belonging to the caster (CR 514.2). The second such cleanup (the
///   caster's NEXT turn) clears all grants. The first cleanup belongs to
///   the caster's CURRENT turn and must be skipped (instant may be cast
///   on any turn but the grant window spans to the end of the caster's
///   NEXT turn). This matches the pattern from
///   <see cref="RecklessImpulseFactory"/> and
///   <see cref="LightUpTheStageFactory"/>.
///
/// ## "Play up to two" cap design
///
/// The cap is enforced at grant time: only the first two exiled cards
/// receive a <see cref="Card.GrantRuntimeExileCast"/> stamp. Subsequent
/// cards (index ≥ 2) are in exile but have no grant, so
/// <see cref="ExileCastAlternativeCost.CanCastFor"/> will return false for
/// them. This is the simplest faithful model: each grant is per-card, not
/// shared, so no counter or controller-state bookkeeping is needed.
///
/// ## Design references
///
/// - March additional cost + colour swap: <see cref="MarchOfWretchedSorrowFactory"/>
///   (black sibling) / <see cref="MarchOfOtherworldlyLightFactory"/> (white).
/// - Exile top N + next-turn grant: <see cref="RecklessImpulseFactory"/>
///   (fixed 2), <see cref="LightUpTheStageFactory"/> (fixed 3).
/// - X-spell shape: <see cref="BonfireOfTheDamnedFactory"/> /
///   <see cref="ChordOfCallingFactory"/> for HasVariableX idiom.
///
/// ## Sibling cards (cycle)
///
/// All five "March of …" cards from Kamigawa: Neon Dynasty reuse
/// <see cref="MarchAdditionalCost"/> with a different colour:
///   * <i>March of Wretched Sorrow</i> — {X}{B} — black exile; damage + life.
///   * <i>March of Otherworldly Light</i> — {X}{W} — white exile; exile target.
///   * <i>March of Burgeoning Life</i> — {X}{G} — green exile; creature tutor.
///   * <i>March of Swirling Mist</i> — {X}{U} — blue exile; phase out.
/// </summary>
[CardName("March of Reckless Joy")]
public static class MarchOfRecklessJoyFactory
{
    public const string CardName = "March of Reckless Joy";
    public const string PrintedManaCost = "{X}{R}";

    /// <summary>The colour of the cards eligible for the March exile —
    /// red for this card. Surfaced for the bot's
    /// <see cref="MarchAdditionalCost.AvailableHandCards"/> probe.</summary>
    public const ManaColor MarchExileColor = ManaColor.Red;

    /// <summary>Maximum number of exiled cards on which a play-from-exile
    /// grant is stamped ("you may play up to TWO of those cards").
    /// CR 118.9 — only two cards receive the permission.</summary>
    public const int MaxPlayGrants = 2;

    /// <summary>Construct the runtime card shape.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reusable <see cref="MarchAdditionalCost"/> for this
    /// spell with the caller-selected hand cards. Pass an empty list when
    /// the caster declines the optional cost (the spell still casts at
    /// full {X}{R}). Mirrors
    /// <see cref="MarchOfWretchedSorrowFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static MarchAdditionalCost BuildAdditionalCost(
        ICard source, IReadOnlyList<ICard> exiledHandCards)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exiledHandCards);
        return new MarchAdditionalCost(source, MarchExileColor, exiledHandCards);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when March of Reckless
    /// Joy is cast. <see cref="SpellDefinition.HasVariableX"/> is true so
    /// the cast flow prompts for X at cast time. Resolution exiles the top
    /// X cards of the caster's library and grants play-from-exile on up to
    /// two of those cards until the end of the caster's next turn.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile top {x} of library; may play up to {MaxPlayGrants} until end of your next turn.",
                        () =>
                        {
                            if (x <= 0) return;

                            var stamped = new List<Card>(MaxPlayGrants);
                            var grantIndex = 0;

                            for (var i = 0; i < x; i++)
                            {
                                // CR 701.21 — move from top of library to exile.
                                // If library runs out mid-loop, stop (no SBA flag
                                // for exile underflow, per CR 701.21 / 701.20).
                                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                                if (top == null) break;

                                caster.Zones.Library.RemoveCard(top);
                                caster.Zones.Exile.AddCard(top);
                                top.SetZone(ZoneType.Exile);

                                // CR 118.9 — stamp the play-from-exile grant on
                                // the first MaxPlayGrants (= 2) cards only.
                                // "You may play up to TWO of those cards" — cards
                                // beyond the cap are exiled but receive no grant.
                                if (grantIndex < MaxPlayGrants && top is Card concrete)
                                {
                                    concrete.GrantRuntimeExileCast(caster, concrete.ManaCostValue);
                                    stamped.Add(concrete);
                                    grantIndex++;
                                }
                            }

                            if (stamped.Count == 0 || eventBus == null) return;

                            // CR 514.2 — schedule "until end of your next turn"
                            // cleanup. Count Cleanup steps where the active player
                            // is the caster. The cast resolves as an instant, so
                            // the first such Cleanup may belong to THIS turn
                            // (caster's own turn) or to an OPPONENT's next turn
                            // (if cast on opponent's turn). Either way, the
                            // second cleanup belonging to the caster is the end
                            // of the caster's NEXT turn — matches Reckless Impulse
                            // and Light Up the Stage EOT-window shape.
                            var cleanupsSeen = 0;
                            Action<StepStartedEvent>? handler = null;
                            handler = (e) =>
                            {
                                if (e.StepType != PhaseStateType.Cleanup) return;
                                if (!ReferenceEquals(e.Player, caster)) return;
                                cleanupsSeen++;
                                if (cleanupsSeen < 2) return;

                                foreach (var s in stamped)
                                {
                                    s.ClearRuntimeExileCast();
                                }
                                if (handler != null) eventBus.Unsubscribe(handler);
                            };
                            eventBus.Subscribe(handler);
                        }),
                };
            });
    }
}
