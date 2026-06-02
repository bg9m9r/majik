using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Red Elemental Blast (Alpha, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Counter target blue spell.
///     • Destroy target blue permanent."
///
/// Functional sibling of Pyroblast. CR 700.2d — modal "Choose one —" spell.
/// Each mode takes its own target, so the bound <see cref="SpellDefinition"/>
/// exposes one <see cref="TargetRequest"/> slot per mode (the chosen-mode
/// index lines up with its target slot), each with MinTargets=0 so the
/// unchosen mode does not gate the cast (mirrors <see cref="RipApartFactory"/>
/// / <see cref="IzzetCharmFactory"/>).
///
/// Unlike Counterspell / Shatter, the "blue" constraint here lives on the
/// TARGET ("target blue spell" / "target blue permanent"), not as a
/// resolve-time rider. The restriction is enforced at gather time (the
/// per-mode <see cref="TargetRequest.CandidateGatherer"/> only offers blue
/// objects) and re-checked at resolution (CR 608.2b) — a non-blue object that
/// slips through (or loses blue after targeting) makes the spell a clean
/// no-op rather than countering/destroying it.
///
/// Mode 0 — "Counter target blue spell": mirrors
/// <see cref="CounterspellFactory"/> / Izzet Charm mode 0. On resolution the
/// blue target spell is removed from the stack via
/// <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves to its
/// owner's graveyard (CR 701.5).
///
/// Mode 1 — "Destroy target blue permanent": mirrors
/// <see cref="RipApartFactory"/>'s destroy clause. On resolution the blue
/// permanent is destroyed (CR 701.7) iff still on the battlefield and blue at
/// resolution. Indestructible (CR 702.12) / regeneration (CR 701.15) are
/// honoured via the Destroy-reason gate in
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> — Red Elemental Blast does
/// not print "can't be regenerated".
///
/// Card shape comes from the embedded JSON (<c>red-elemental-blast.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/> needs
/// a target resolver supplied by the caller's <see cref="GameContext"/> (not
/// expressible in the data-only JSON schema).
/// </summary>
[CardName("Red Elemental Blast")]
public static class RedElementalBlastFactory
{
    public const string CardName = "Red Elemental Blast";
    public const string Slug = "red-elemental-blast";
    public const string PrintedManaCost = "{R}";

    public const int ModeCounter = 0;
    public const int ModeDestroy = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target blue spell.",
        "Destroy target blue permanent.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Red Elemental Blast. One
    /// target slot per mode (CR 601.2c) with MinTargets=0 so the unchosen mode
    /// does not gate the cast; on resolution the chosen mode either counters a
    /// blue spell (mode 0) or destroys a blue permanent (mode 1).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand the
    /// spell/permanent directly.</param>
    /// <param name="stack">Active stack; required for mode 0 (counter). Null in
    /// pure-shape tests; the counter effect becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index lines
        // up with its target slot. MinTargets=0 so the unchosen mode doesn't
        // gate the cast (mirrors RipApartFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — counter target blue spell. Candidates are blue spells on
            // the stack (CR 608.2b — the "blue" restriction is on the target).
            new TargetRequest(
                "target blue spell",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Counter,
                CandidateGatherer: ctx => (stack?.GetAll() ?? Array.Empty<IStackObject>())
                    .OfType<ISpell>()
                    .Where(IsBlue)
                    .Cast<object>()
                    .ToList()),
            // Mode 1 — destroy target blue permanent. Candidates are blue
            // permanents on the battlefield across all players (CR 110).
            new TargetRequest(
                "target blue permanent",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .OfType<Permanent>()
                    .Where(IsBlue)
                    .Cast<object>()
                    .ToList()),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Counter,
                BotIntent.Removal,
            },
            EffectFactory: p =>
            {
                // Honour either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeCounter:
                            effectsOut.Add(BuildCounterEffect(p, targetResolver, stack));
                            break;
                        case ModeDestroy:
                            effectsOut.Add(BuildDestroyEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        new Effect($"{CardName} — counter target blue spell", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;

            // CR 608.2b — "target blue spell" re-checked at resolution. A spell
            // that is not blue (or has lost blue since targeting) is no longer
            // a legal target, so the counter is a no-op.
            if (!IsBlue(spell)) return;

            // CR 701.5 — counter the spell: remove from stack, move card to
            // graveyard.
            OracleSpellBinder.RemoveFromStack(stack, spell);
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target blue permanent", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // Oracle constraint: target must be blue at resolution (CR 608.2b).
            if (!IsBlue(target)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) are honoured via the Destroy-reason gate in
            // MoveToGraveyard; Red Elemental Blast does not print "can't be
            // regenerated".
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    /// <summary>True when the spell's card is blue (CR 105.2 / CR 202.2).</summary>
    private static bool IsBlue(ISpell spell) =>
        CardColors.GetColors(spell.Card).Contains(ManaColor.Blue);

    /// <summary>True when the permanent is blue (CR 105.2 / CR 202.2).</summary>
    private static bool IsBlue(ICard card) =>
        CardColors.GetColors(card).Contains(ManaColor.Blue);
}
