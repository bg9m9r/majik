using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Render Silent (Dragon's Maze, {W}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target spell. Its controller can't cast spells this turn."
///
/// ## Why it gets its own factory
/// Render Silent is a hard counter (<see cref="CounterspellFactory"/>'s
/// "counter target spell" body — no filter, no escape clause) plus a
/// turn-scoped cast-lockout rider against the countered spell's controller.
/// Both halves already ship as engine primitives — no new mechanic is
/// required:
/// <list type="bullet">
///   <item>Counter body: <see cref="OracleSpellBinder.RemoveFromStack"/> +
///   graveyard move (CR 701.5 / CR 608.2b), identical to Counterspell.</item>
///   <item>Lockout rider: <see cref="CastingRestrictions.AddCannotCastAnySpell"/>
///   keyed by the card's object reference — the same total-cast block rail
///   <see cref="OrimSChantFactory"/> / Voice of Victory / Grand Abolisher use
///   (CR 601.3). <see cref="ActionValidator.ValidateCastSpell"/> consults
///   <see cref="CastingRestrictions.CannotCastAnySpell"/> and rejects every
///   subsequent cast by that player — creature and noncreature alike.</item>
/// </list>
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}{U}{U}, white/blue (CR 105.2). Card shape
///   comes from the embedded JSON (<c>render-silent.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>"Counter target spell" (CR 701.5)</b>: single 1..1 "target spell"
///   request, no type filter (any spell is a legal target). At resolution the
///   target is removed from the stack and its card goes to the graveyard.
/// - <b>"Its controller can't cast spells this turn" (CR 601.3 / CR 514.2)</b>:
///   the restriction is registered against the COUNTERED spell's controller
///   (read off the live <see cref="ISpell.Controller"/> at resolution — not a
///   separate target), keyed by the Render Silent card so end-of-turn cleanup
///   can tear it down via
///   <see cref="CastingRestrictions.RemoveCannotCastAnySpell(object)"/>. The
///   rider applies even though the spell is the same target as the counter —
///   the controller is determined by the chosen spell, so no second
///   <see cref="TargetRequest"/> is needed (matching the printed text:
///   "Its controller", referring back to the countered spell).
///
/// ## Rules citations
/// - CR 701.5 — counter a spell (remove from stack, card to graveyard).
/// - CR 601.3 — "<player> can't cast spells" cast restriction.
/// - CR 514.2 — "this turn" effects expire at cleanup; the caller / end-of-turn
///   machinery clears the rider via the card token.
///
/// ## Deferred (v1 gaps)
/// - <b>End-of-turn auto-clear wiring</b>: like Orim's Chant, the rider is
///   keyed by the card token and cleared by the caller / end-of-turn machinery
///   (or <see cref="CastingRestrictions.Clear"/> in tests). The factory does
///   not subscribe its own <see cref="Majik.Core.Events.TurnEndedEvent"/>
///   handler — the total-cast-block rail is already swept by the shared turn
///   lifecycle that owns the Voice-of-Victory / Orim's-Chant rail.
/// </summary>
[CardName("Render Silent")]
public static class RenderSilentFactory
{
    public const string CardName = "Render Silent";
    public const string Slug = "render-silent";
    public const string PrintedManaCost = "{W}{U}{U}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Declares a single
    /// 1..1 "target spell" request (no type filter). On resolution: counter
    /// the target spell (CR 701.5) — remove from stack, card to graveyard —
    /// then register a turn-scoped total-cast block against that spell's
    /// controller (CR 601.3), keyed by <paramref name="card"/> so it can be
    /// cleared at end of turn.
    /// </summary>
    /// <param name="card">The cast Render Silent instance — used as the unique
    /// restriction token so the lockout can be cleared via
    /// <see cref="CastingRestrictions.RemoveCannotCastAnySpell(object)"/>.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object (chosen → live stack object). Pass
    /// <c>o =&gt; o</c> for tests.</param>
    /// <param name="stack">Live stack — required to remove the countered spell.
    /// Null in pure-shape tests; the counter effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        ICard card,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Render Silent — counter target spell; its controller can't cast spells this turn",
                        () =>
                        {
                            if (resolved is not ISpell spell) return;

                            // Capture the controller BEFORE countering, while the
                            // spell object is still intact (CR 601.3 — "its
                            // controller" refers to the countered spell).
                            var controller = spell.Controller;

                            // "Counter target spell." (CR 701.5) — remove from the
                            // stack and send the card to its owner's graveyard.
                            if (stack != null)
                            {
                                OracleSpellBinder.RemoveFromStack(stack, spell);
                                spell.Card.SetZone(ZoneType.Graveyard);
                            }

                            // "Its controller can't cast spells this turn."
                            // (CR 601.3) — total cast block keyed by the Render
                            // Silent card so end-of-turn cleanup can remove it
                            // (CR 514.2). Same rail as Orim's Chant / Voice of
                            // Victory / Grand Abolisher.
                            if (controller != null)
                            {
                                CastingRestrictions.AddCannotCastAnySpell(card, controller);
                            }
                        }),
                };
            });
    }
}
