using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pyroblast (Ice Age / many reprints, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Counter target spell if it's blue.
///     • Destroy target permanent if it's blue."
///
/// CR 700.2d — modal "Choose one —" spell with two modes, both of which take
/// a target. Shape-wise this is the same Choose-one-with-targets instant as
/// <see cref="IzzetCharmFactory"/> / <see cref="EsperCharmFactory"/>; the
/// per-mode bodies reuse only existing primitives (counter + destroy +
/// <see cref="CardColors.GetColors"/> for the blue gate) — no new engine
/// mechanic is introduced.
///
/// The "if it's blue" clause is NOT a targeting restriction: Pyroblast may be
/// cast targeting ANY spell / ANY permanent, and the colour is checked when
/// the spell resolves (CR 608.2c — an intervening "if" condition). If the
/// chosen target is not blue at resolution, that mode does nothing.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}, red, mana value 1. Card shape comes from
///   the embedded JSON (<c>pyroblast.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - Modal "Choose one —" (CR 700.2d). The chosen mode is read from
///   <see cref="ChosenSpellParams.ModeIndex"/> (or the first entry of
///   <see cref="ChosenSpellParams.ModeIndexes"/>). Each mode declares a 0..1
///   target slot (CR 601.2c — MinTargets=0 so the unchosen mode's slot does
///   not gate the cast), mirroring <see cref="EsperCharmFactory"/>.
/// - <b>Mode 0 — "Counter target spell if it's blue"</b>: targets any spell on
///   the stack. At resolution, if the target spell is still on the stack and
///   is blue (<see cref="CardColors.GetColors"/> contains
///   <see cref="ManaColor.Blue"/>), it is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to its owner's
///   graveyard (CR 701.5). A non-blue (or missing) target is a clean no-op
///   (CR 608.2c). Counter body mirrors <see cref="ExcludeFactory"/> /
///   <see cref="IzzetCharmFactory"/>'s counter mode.
/// - <b>Mode 1 — "Destroy target permanent if it's blue"</b>: targets any
///   permanent. At resolution, if the target is still a battlefield
///   <see cref="Permanent"/> and is blue, it is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) / regeneration (CR 701.15) shields are honoured. A non-blue
///   (or no-longer-legal) target is a clean no-op (CR 608.2c). Destroy body
///   mirrors <see cref="EsperCharmFactory"/>'s destroy mode with the type
///   filter swapped for a blue-colour gate.
/// </summary>
[CardName("Pyroblast")]
public static class PyroblastFactory
{
    public const string CardName = "Pyroblast";
    public const string Slug = "pyroblast";
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
        "Counter target spell if it's blue.",
        "Destroy target permanent if it's blue.",
    };

    /// <summary>Construct Pyroblast's base shape from the embedded JSON.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for
    /// Pyroblast.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    /// <param name="stack">Active stack; required for mode 0 (counter). Null in
    /// shape-only tests — mode 0 then becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — a target request is emitted for every mode that takes a
        // target. MinTargets=0 so the unchosen mode's slot doesn't gate the
        // cast (mirrors EsperCharmFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — counter target spell (any spell on the stack; the blue
            // gate is a resolution-time check, not a targeting restriction).
            new TargetRequest("target spell", 0, 1, Array.Empty<object>(), BotIntent.Counter),
            // Mode 1 — destroy target permanent (any permanent; blue gate at
            // resolution).
            new TargetRequest("target permanent", 0, 1, Array.Empty<object>(), BotIntent.Removal),
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
                // Honor either the multi-pick list (first entry wins for a
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

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    /// <summary>
    /// Mode 0 — "Counter target spell if it's blue." CR 701.5 / CR 608.2c.
    /// </summary>
    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        new Effect($"{CardName} — counter target spell if it's blue", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;

            // CR 608.2c — "if it's blue" is an intervening condition checked at
            // resolution, not a targeting restriction. A non-blue spell is a
            // clean no-op (the spell is not countered).
            if (!CardColors.GetColors(spell.Card).Contains(ManaColor.Blue)) return;

            // CR 701.5 — counter the spell: remove from stack and move the
            // card to its owner's graveyard.
            OracleSpellBinder.RemoveFromStack(stack, spell);
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    /// <summary>
    /// Mode 1 — "Destroy target permanent if it's blue." CR 701.7 / CR 608.2c.
    /// </summary>
    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target permanent if it's blue", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // CR 608.2c — "if it's blue" intervening condition; a non-blue
            // permanent is a clean no-op (not destroyed).
            if (!CardColors.GetColors(target).Contains(ManaColor.Blue)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) / regeneration
            // (CR 701.15) honoured via the Destroy-reason gate.
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });
}
