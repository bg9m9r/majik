using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Counterflux (Gatecrash, {U}{U}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-14):
///   "This spell can't be countered.
///    Counter target spell you don't control.
///    Overload {1}{U}{U}{R} (You may cast this spell for its overload cost. If
///    you do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Counter each spell you don't control."
///
/// Counterflux is the <i>counter</i> analogue of
/// <see cref="CyclonicRiftFactory"/> (which <i>bounces</i> "each nonland
/// permanent you don't control" under the same overload "target" → "each"
/// rewrite): the per-object effect removes a SPELL from the stack
/// (CR 701.5) rather than bouncing a permanent, and the candidate pool is
/// every spell on the stack the controller does NOT control (CR 109.5 —
/// "you" = the spell's controller). The "can't be countered" rider follows
/// <see cref="DovinsVetoFactory"/> / <see cref="EmrakulTheAeonsTornFactory"/>:
/// a <see cref="KeywordAbility"/>("Uncounterable") marker that
/// <see cref="Majik.Core.Game.SpellCastFlow"/> reads at cast time to stamp
/// <see cref="ISpell.CannotBeCountered"/> (CR 701.5b).
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {U}{U}{R} (blue + red). The base
///   shape (name / Instant type / cost) is materialised from the embedded
///   JSON definition (<c>counterflux.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="CyclonicRiftFactory"/>. The "Uncounterable" keyword marker
///   is layered on after the build (cf.
///   <see cref="EmrakulTheAeonsTornFactory"/>), since the JSON
///   <c>SpellDefinition</c> schema does not yet express the counter request
///   or the overload sweep.
/// - <b>This spell can't be countered</b> (CR 701.5b) — the
///   "Uncounterable" <see cref="KeywordAbility"/> on the card shape causes
///   <see cref="Majik.Core.Game.SpellCastFlow"/> to set
///   <see cref="ISpell.CannotBeCountered"/> on the cast Counterflux spell,
///   so a rival counter (Negate, Counterspell, …) calling
///   <see cref="OracleSpellBinder.RemoveFromStack"/> is vetoed and
///   Counterflux resolves normally.
/// - <b>Counter target spell you don't control</b> —
///   <see cref="BuildSpellDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target spell you don't control"
///   <see cref="TargetRequest"/>. The candidate gatherer walks the stack and
///   yields spells the spell's controller does NOT control (CR 109.5). On
///   resolution the target is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves to
///   its owner's graveyard (CR 701.5).
///
/// ## Overload (CR 702.96 — structural-flag-only, mirrors Cyclonic Rift)
///
/// Overload is an alternative cost. The
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive is a
/// flag carrier: it gates the cast and carries an <c>IsOverloaded</c> flag,
/// but is not yet plumbed through
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s payment loop, so the "was
/// overloaded?" bit does not flow from cast-time to the resolving stack
/// object. Until that infra lands, Counterflux ships with
/// default-not-overloaded behaviour. The overloaded branch is structural —
/// callers opt in via <c>wasOverloaded: true</c> on
/// <see cref="BuildSpellDefinition"/>, which drops the target request and
/// counters each spell the controller does NOT control on the stack
/// (CR 702.96b "target" → "each" rewrite). Same posture as
/// <see cref="CyclonicRiftFactory"/> / <see cref="VandalblastFactory"/>.
///
/// ## CR notes
/// - CR 701.5 / 701.5b — counter a spell; an uncounterable spell can't be
///   countered.
/// - CR 702.96 / 702.96b — Overload alt-cost; "target" → "each" rewrite.
/// - CR 109.5 — "you" in an object's text refers to that object's
///   controller; "you don't control" therefore excludes the spell
///   controller's own spells on the stack.
/// - CR 608.2b — resolution-time legality re-check (target still on the
///   stack, still not controlled by the spell controller).
/// </summary>
[CardName("Counterflux")]
public static class CounterfluxFactory
{
    public const string CardName = "Counterflux";
    public const string PrintedManaCost = "{U}{U}{R}";
    public const string OverloadCostText = "{1}{U}{U}{R}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "counterflux";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {U}{U}{R}) from
    /// the embedded JSON definition and stamp the "Uncounterable" marker
    /// (CR 701.5b). Resolve behaviour (counter target spell you don't
    /// control) is built on demand via <see cref="BuildSpellDefinition"/>,
    /// mirroring <see cref="CyclonicRiftFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 701.5b — "This spell can't be countered". The Uncounterable
        // KeywordAbility marker is read at cast time by SpellCastFlow, which
        // stamps ISpell.CannotBeCountered on the resulting spell (same
        // posture as Emrakul, the Aeons Torn / Dovin's Veto).
        card.AddAbility(new KeywordAbility("Uncounterable", card, owner));

        return card;
    }

    /// <summary>
    /// Build the Counterflux <see cref="SpellDefinition"/>.
    ///
    /// Default (<paramref name="wasOverloaded"/> = false): single 1..1
    /// "target spell you don't control" request. The candidate gatherer
    /// walks the stack and yields spells the <paramref name="controller"/>
    /// does NOT control (CR 109.5). On resolve, removes the target from the
    /// stack and sends its card to its owner's graveyard (CR 701.5).
    ///
    /// Overloaded (<paramref name="wasOverloaded"/> = true): no target
    /// request; on resolve counters every spell the
    /// <paramref name="controller"/> does NOT control on
    /// <paramref name="stack"/> (CR 702.96b).
    /// </summary>
    /// <param name="controller">The spell's controller — the "you" in
    /// "you don't control" (CR 109.5).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// spells directly.</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell(s). Null in pure-shape tests; the effect becomes a no-op.</param>
    /// <param name="wasOverloaded">Whether the overload alt-cost was paid at
    /// cast time. Defaults to <c>false</c> — overload is not yet wired through
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        bool wasOverloaded = false)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        if (wasOverloaded)
        {
            // CR 702.96b — overloaded branch. "target" rewritten to "each":
            // counter each spell the controller does NOT control. Snapshot
            // the stack's spells before applying so the removals don't
            // disturb enumeration.
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect(
                        $"{CardName} (overloaded): counter each spell you don't control.",
                        () =>
                        {
                            if (stack == null) return;

                            foreach (var spell in stack.GetAll()
                                         .OfType<ISpell>()
                                         .Where(s => !IsControlledBy(s, controller))
                                         .ToList())
                            {
                                // CR 701.5 — RemoveFromStack returns false for
                                // an uncounterable spell (CR 701.5b); only send
                                // the card to the graveyard when it was actually
                                // removed.
                                if (OracleSpellBinder.RemoveFromStack(stack, spell))
                                {
                                    spell.Card.SetZone(ZoneType.Graveyard);
                                }
                            }
                        }),
                });
        }

        // Default printed cast — single 1..1 "target spell you don't
        // control" request; resolve = counter that spell.
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt: walk the stack, yield spells the spell's
                    // controller does NOT control (CR 109.5).
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<ISpell>()
                        .Where(s => !IsControlledBy(s, controller))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: counter target spell you don't control.",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;
                            // CR 109.5 — must not be controlled by the spell's
                            // controller ("you don't control").
                            if (IsControlledBy(spell, controller)) return;

                            // CR 701.5 / 608.2b — RemoveFromStack returns false
                            // for an uncounterable target; only graveyard the
                            // card when it was actually removed.
                            if (OracleSpellBinder.RemoveFromStack(stack, spell))
                            {
                                spell.Card.SetZone(ZoneType.Graveyard);
                            }
                        }),
                };
            });
    }

    /// <summary>
    /// CR 109.5 — is <paramref name="spell"/> controlled by
    /// <paramref name="controller"/>? Prefers the <see cref="Spell"/>'s own
    /// <see cref="Spell.Controller"/>, falling back to the card's controller.
    /// </summary>
    private static bool IsControlledBy(ISpell spell, Player controller)
    {
        if (spell is Majik.Core.Spells.Spell s)
        {
            return ReferenceEquals(s.Controller, controller);
        }

        return ReferenceEquals(spell.Card.Controller, controller);
    }
}
