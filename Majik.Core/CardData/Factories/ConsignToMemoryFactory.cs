using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Consign to Memory (Modern Horizons 3, {U}).
///
/// Printed oracle text (Scryfall, MH3):
///   "Replicate {1} (When you cast this spell, copy it for each time you
///    paid its replicate cost. You may choose new targets for the copies.)
///    Counter target triggered ability or colorless spell."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - Resolve-time <see cref="SpellDefinition"/> (via <see cref="BuildSpellDefinition"/>)
///   declares one 1..1 "target triggered ability or colorless spell"
///   <see cref="TargetRequest"/>. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5).
///   * Triggered-ability target: the ability ceases to exist when removed
///     from the stack (CR 701.5b — there is no "graveyard" for abilities).
///   * Colorless-spell target: the spell's card moves to its owner's
///     graveyard (CR 701.5a).
/// - Legality gate (CR 608.2b — recheck at resolution): if the chosen
///   target is no longer on the stack, or is a non-triggered ability
///   (e.g. an activated ability), or is a coloured spell, the effect does
///   nothing for it. Filter is applied defensively at resolve time rather
///   than at choose-time (<see cref="TargetRequest.LegalCandidates"/> left
///   empty) — same posture as <see cref="NegateFactory"/>'s creature gate.
/// - Colour check uses <see cref="CardColors.GetColors"/> (CR 105): a spell
///   is "colorless" iff its derived colour set is empty (no W/U/B/R/G
///   coloured pips and no hybrid/Phyrexian pips contributing colours).
///
/// ## Deferred
/// - Replicate alt-cost (CR 702.99 — pay {1} any number of times alongside
///   the cast cost; on cast, copy the spell once per replicate payment with
///   may-rechoose-targets per copy). Same gap as the Storm cycle's
///   self-copy-on-cast plumbing — no Replicate primitive yet. Production
///   single-cast posture matches every other not-yet-replicated factory:
///   the spell resolves once, no copies, dispatcher path simply ships the
///   instant shape without a Replicate marker.
/// - Activated-ability counter target: Consign to Memory does NOT counter
///   activated abilities by oracle (only triggered abilities + colorless
///   spells). The resolve-time predicate explicitly rejects
///   <see cref="ActivatedAbility"/> stack objects (and any non-spell,
///   non-triggered-ability shape) as illegal targets per CR 608.2b.
/// </summary>
[CardName("Consign to Memory")]
public static class ConsignToMemoryFactory
{
    public const string CardName = "Consign to Memory";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Build a Consign to Memory instant owned by <paramref name="owner"/>.
    /// Card shape only; the resolve-time SpellDefinition is built via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the "counter target triggered ability or colorless spell"
    /// SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token (as
    /// produced by the caller's <see cref="GameContext"/>) to a live
    /// engine object — typically the identity function when targets are
    /// already engine references.</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// object. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target triggered ability or colorless spell",
                    1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Consign to Memory — counter target triggered ability or colorless spell",
                        () =>
                        {
                            if (stack == null) return;

                            // CR 608.2b — recheck legality at resolution.
                            // Eligible targets:
                            //   * triggered ability on the stack
                            //   * colorless spell on the stack
                            // Anything else (activated ability, coloured
                            // spell, off-stack object) → clean no-op.
                            switch (resolved)
                            {
                                case ITriggeredAbility trig:
                                    if (!stack.GetAll().Contains(trig)) return;
                                    OracleSpellBinder.RemoveFromStack(stack, trig);
                                    // Abilities have no zone — they simply
                                    // cease to exist (CR 701.5b).
                                    return;

                                case ISpell spell:
                                    if (!stack.GetAll().Contains(spell)) return;
                                    if (CardColors.GetColors(spell.Card).Count != 0)
                                    {
                                        // Coloured spell → illegal at
                                        // resolution.
                                        return;
                                    }
                                    OracleSpellBinder.RemoveFromStack(stack, spell);
                                    // CR 701.5a — countered spell moves to
                                    // its owner's graveyard.
                                    spell.Card.SetZone(ZoneType.Graveyard);
                                    return;

                                // Any other IStackObject shape (e.g.
                                // IActivatedAbility) is an illegal target
                                // per the printed oracle predicate.
                                default:
                                    return;
                            }
                        }),
                };
            });
    }
}
