using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Witherbloom Charm (Strixhaven: School of Mages,
/// {B}{G}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • You may sacrifice a permanent. If you do, draw two cards.
///     • You gain 5 life.
///     • Destroy target nonland permanent with mana value 2 or less."
///
/// CR 700.2d — modal "Choose one —" spell. Only mode 2 (destroy) takes a
/// target; modes 0 and 1 are targetless. The bound
/// <see cref="SpellDefinition"/> exposes three <see cref="TargetRequest"/>s
/// (one slot per mode) so the chosen-mode index lines up with its target
/// slot, with MinTargets=0 on every slot so unchosen modes don't gate the
/// cast (mirrors <see cref="IzzetCharmFactory"/> /
/// <see cref="ArchmagesCharmFactory"/>).
///
/// Mode 0 — "You may sacrifice a permanent. If you do, draw two cards":
/// CR 701.16 sacrifice (bypasses indestructible) routed through
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Sacrifice"/>, followed by the
/// intervening-"if you do" two-card draw (CR 121.1) — the draw fires ONLY
/// when a permanent was actually sacrificed. v1 auto-picks the first
/// candidate supplied by <c>sacrificeCandidates</c> (real agent "choose a
/// permanent to sacrifice" prompt is deferred — same queue as Bone
/// Splinters' sacrifice-target prompt). The single-arg
/// <see cref="BuildDefinition(Player, Func{object, object})"/> path supplies
/// no candidates, so the sacrifice (and therefore the draw) no-ops — same
/// posture as other factories' shape-only dispatcher overloads.
///
/// Mode 1 — "You gain 5 life": CR 119.3 — <see cref="Player.GainLife"/>.
///
/// Mode 2 — "Destroy target nonland permanent with mana value 2 or less":
/// mirrors <see cref="AbruptDecayFactory"/>. On resolution the target is
/// destroyed (CR 701.7) iff it is still on the battlefield, is not a land,
/// and its mana value is ≤ 2 at resolution (CR 608.2b / CR 202.3).
/// Indestructible (CR 702.12) and active regeneration shields (CR 701.15)
/// are honoured via the Destroy reason — Witherbloom Charm does not print
/// "can't be regenerated".
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-target prompt</b>: mode 0 auto-picks the first candidate
///   rather than letting the agent choose which permanent to sacrifice
///   (same deferred queue as Bone Splinters).
/// - <b>"May" opt-out</b>: mode 0 always sacrifices when a candidate exists
///   (the agent's opt-out of the optional sacrifice awaits the prompt
///   system). Note this only affects the caster's own choice; the
///   "if you do" gate on the draw is enforced.
/// </summary>
[CardName("Witherbloom Charm")]
public static class WitherbloomCharmFactory
{
    public const string CardName = "Witherbloom Charm";
    public const string PrintedManaCost = "{B}{G}";

    public const int ModeSacrificeDraw = 0;
    public const int ModeGainLife = 1;
    public const int ModeDestroy = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Mode 1 — life gained.</summary>
    public const int LifeGain = 5;

    /// <summary>Mode 0 — number of cards drawn after a successful sacrifice.</summary>
    public const int CardsDrawn = 2;

    /// <summary>Mode 2 — target permanent's mana value must be at most this.</summary>
    public const int MaxDestroyManaValue = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "You may sacrifice a permanent. If you do, draw two cards.",
        "You gain 5 life.",
        "Destroy target nonland permanent with mana value 2 or less.",
    };

    /// <summary>Construct Witherbloom Charm as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Shape-only dispatcher path — mode 0's sacrifice (and therefore the
    /// "if you do" draw) no-ops because no sacrifice candidate is supplied.
    /// The gain-life and destroy modes resolve fully.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver) =>
        BuildDefinition(caster, targetResolver, sacrificeCandidates: null);

    /// <summary>
    /// Build the SpellDefinition for Witherbloom Charm.
    /// </summary>
    /// <param name="caster">The spell's controller (gains life, draws, sacrifices).</param>
    /// <param name="targetResolver">Resolves the raw mode-2 target token to a live object.</param>
    /// <param name="sacrificeCandidates">v1 supplier of the caster's permanents
    /// eligible to be sacrificed for mode 0 (auto-picks the first). Pass
    /// <c>null</c> for the shape-only path.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Func<IReadOnlyList<Permanent>>? sacrificeCandidates)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index
        // lines up with its target slot. MinTargets=0 so unchosen modes
        // don't gate the cast (mirrors ArchmagesCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — sacrifice a permanent, then draw two (no target).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Draw),
            // Mode 1 — you gain 5 life (no target).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Heal),
            // Mode 2 — destroy target nonland permanent with mv ≤ 2.
            new TargetRequest(
                "target nonland permanent with mana value 2 or less",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Removal),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Draw,
                BotIntent.Heal,
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
                        case ModeSacrificeDraw:
                            effectsOut.Add(BuildSacrificeDrawEffect(caster, sacrificeCandidates));
                            break;
                        case ModeGainLife:
                            effectsOut.Add(BuildGainLifeEffect(caster));
                            break;
                        case ModeDestroy:
                            effectsOut.Add(BuildDestroyEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildSacrificeDrawEffect(
        Player caster,
        Func<IReadOnlyList<Permanent>>? sacrificeCandidates) =>
        new Effect($"{CardName} — you may sacrifice a permanent; if you do, draw two", () =>
        {
            // CR 701.16 — sacrifice. v1 auto-picks the first candidate; the
            // optional "may" opt-out + agent target prompt are deferred.
            var candidate = sacrificeCandidates?.Invoke()
                .FirstOrDefault(c => c.Zone == ZoneType.Battlefield
                    && ReferenceEquals(c.Controller, caster));
            if (candidate == null) return; // nothing sacrificed → "if you do" fails.

            OracleSpellBinder.MoveToGraveyard(candidate, ZoneMoveReason.Sacrifice);

            // CR 121.1 — "If you do, draw two cards." Intervening-if: the
            // draw only fires because a permanent was actually sacrificed.
            for (var i = 0; i < CardsDrawn; i++)
            {
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 704.5b — drawing from an empty library is tracked
                    // for the state-based loss check.
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    break;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
        });

    private static IEffect BuildGainLifeEffect(Player caster) =>
        new Effect($"{CardName} — you gain {LifeGain} life", () =>
        {
            // CR 119.3 — gaining life.
            caster.GainLife(LifeGain);
        });

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target nonland permanent with mv ≤ {MaxDestroyManaValue}", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not Permanent target) return;

            // CR 608.2b — resolution-time legality check.
            if (target.Zone != ZoneType.Battlefield) return;
            if (target.HasType(CardType.Land)) return;

            // CR 202.3 — mana value is checked at resolution.
            if (target.ManaCostValue.TotalValue > MaxDestroyManaValue) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
            // regeneration (CR 701.15) are honoured via the Destroy reason;
            // Witherbloom Charm does not print "can't be regenerated".
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });
}
