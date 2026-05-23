using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Archmage's Charm (Modern Horizons, {U}{U}{U}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • Counter target spell.
///     • Target player draws two cards.
///     • Gain control of target nonland permanent with mana value 1 or less."
///
/// CR 700.2d — modal "Choose one —" spell. Mode 0 (counter) and mode 2
/// (gain-control) take a target; mode 1 (target-player-draws-two) takes a
/// player target. The bound <see cref="SpellDefinition"/> exposes three
/// <see cref="TargetRequest"/>s (one per mode); only the chosen mode's
/// slot is filled at cast time (MinTargets=0 so unchosen modes don't gate
/// the cast). Mode 2's "nonland permanent with mana value 1 or less" gate
/// is enforced at resolution time (CR 608.2b — illegal target → effect
/// does nothing) since the engine has no agent-side filter for "mv ≤ 1
/// nonland permanent" yet.
///
/// Mode 2 wires <see cref="ControlChangeEffect"/> (CR 613.2) on the
/// supplied <see cref="ContinuousEffectsService"/>; the single-arg
/// <see cref="BuildDefinition(Player, Func{object, object}, Majik.Core.Stack.Stack)"/>
/// path leaves <c>effects</c> null so the control swap is a no-op while
/// the counter / draw modes still resolve. Use
/// <see cref="BuildDefinition(Player, Func{object, object}, Majik.Core.Stack.Stack, ContinuousEffectsService)"/>
/// for fully-wired Layer 2 control swap.
///
/// Pattern mirrors <see cref="CrypticCommandFactory"/> for the modal
/// shape and <see cref="WishclawTalismanFactory"/> for the
/// ControlChangeEffect registration.
/// </summary>
public static class ArchmagesCharmFactory
{
    public const string CardName = "Archmage's Charm";

    public const int ModeCounter = 0;
    public const int ModeDraw = 1;
    public const int ModeGainControl = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Mode 2 — target permanent's mana value must be at most this.</summary>
    public const int MaxGainControlManaValue = 1;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{U}{U}{U}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target spell.",
        "Target player draws two cards.",
        "Gain control of target nonland permanent with mana value 1 or less.",
    };

    /// <summary>
    /// Single-arg BuildDefinition path — mode 2's control change no-ops
    /// without a live <see cref="ContinuousEffectsService"/>; the counter
    /// and draw modes still resolve fully.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack) =>
        BuildDefinition(caster, targetResolver, stack, effects: null);

    /// <summary>
    /// Build the SpellDefinition for Archmage's Charm. Mode 2's
    /// <see cref="ControlChangeEffect"/> is registered against
    /// <paramref name="effects"/> when supplied; pass <c>null</c> for
    /// shape / counter / draw tests where Layer 2 isn't needed.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that
        // takes a target. MinTargets=0 so unchosen modes don't gate the
        // cast (mirrors CrypticCommandFactory).
        var targetRequests = new[]
        {
            // Mode 0 — counter target spell.
            new TargetRequest("target spell", 0, 1, Array.Empty<object>(), BotIntent.Counter),
            // Mode 1 — target player draws two cards.
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Draw),
            // Mode 2 — gain control of target nonland permanent with mv ≤ 1.
            new TargetRequest("target nonland permanent", 0, 1, Array.Empty<object>(), BotIntent.Removal),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Counter,
                BotIntent.Draw,
                BotIntent.Removal, // gain-control is removal-adjacent (opponent loses permanent)
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
                        case ModeDraw:
                            effectsOut.Add(BuildDrawEffect(p, targetResolver));
                            break;
                        case ModeGainControl:
                            effectsOut.Add(BuildGainControlEffect(caster, p, targetResolver, effects));
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
        new Effect("Archmage's Charm — counter target spell", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;
            OracleSpellBinder.RemoveFromStack(stack, spell);
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    private static IEffect BuildDrawEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Archmage's Charm — target player draws two cards", () =>
        {
            if (p.Targets.Count <= ModeDraw) return;
            var slot = p.Targets[ModeDraw];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not Player target) return;
            for (int i = 0; i < 2; i++)
            {
                var top = target.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                target.Zones.Library.RemoveCard(top);
                target.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
        });

    private static IEffect BuildGainControlEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver,
        ContinuousEffectsService? effects) =>
        new Effect("Archmage's Charm — gain control of target nonland permanent with mv ≤ 1", () =>
        {
            if (p.Targets.Count <= ModeGainControl) return;
            var slot = p.Targets[ModeGainControl];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not Permanent perm) return;

            // CR 608.2b — resolution-time legality. The agent-side filter
            // doesn't yet enforce "nonland permanent with mv ≤ 1"; we
            // guard at resolve.
            if (perm.HasType(CardType.Land)) return;
            if (perm.Zone != ZoneType.Battlefield) return;
            if (perm.ManaCostValue.TotalValue > MaxGainControlManaValue) return;

            // CR 613.2 — Layer 2 control-changing effect. Without a live
            // ContinuousEffectsService the swap silently no-ops (shape-
            // only test path) — matches WishclawTalismanFactory's
            // single-arg dispatcher behaviour.
            if (effects == null) return;
            if (ReferenceEquals(perm.Controller, caster)) return;
            effects.Register(new ControlChangeEffect(perm, caster));
        });
}
