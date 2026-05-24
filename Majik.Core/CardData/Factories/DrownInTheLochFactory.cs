using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Drown in the Loch (Throne of Eldraine, {U}{B}).
///
/// Instant. Oracle text (as specified by the implementation brief —
/// the printed Eldraine card uses a fixed mv-3 gate; this engine spec
/// models it as a graveyard-derived X so the cap scales with the
/// opponent's graveyard depth):
///   "Choose one. X is the largest mana value among cards in opponents'
///    graveyards.
///     • Counter target spell with mana value X or less.
///     • Destroy target creature with mana value X or less."
///
/// CR 700.2d — modal "Choose one" spell. Both modes take a single
/// target; <see cref="TargetRequest.MinTargets"/> is 0 so the unchosen
/// mode's slot doesn't gate the cast (mirrors
/// <see cref="ArchmagesCharmFactory"/> / <see cref="CrypticCommandFactory"/>).
///
/// X is computed at resolution time from
/// <see cref="ChosenSpellParams.AllPlayers"/> — the largest
/// <see cref="ICard.ManaCostValue"/>.<see cref="ValueObjects.ManaCost.TotalValue"/>
/// across every card in any opponent's graveyard. If
/// <see cref="ChosenSpellParams.AllPlayers"/> isn't supplied (shape-only
/// callers) X defaults to 0 and both modes' gates collapse to "mv ≤ 0",
/// which means only zero-cost targets are affected — lossy but matches
/// the dispatcher posture used by Cryptic Command / Archmage's Charm.
///
/// CR 608.2b — illegal-target check at resolution. The mv-≤-X gate is
/// enforced inside the resolve closure (the engine's target prompt
/// doesn't yet support "mana value ≤ X" filters), so an over-cost
/// target picked at cast time no-ops cleanly.
///
/// CR 701.5 — counter via <see cref="OracleSpellBinder.RemoveFromStack"/>
/// + zone move to graveyard. CR 701.7 — destroy via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/>. Indestructible /
/// regeneration riders are deferred (same gap as
/// <see cref="SlaughterPactFactory"/> and the rest of the single-target
/// destroy family).
/// </summary>
[CardName("Drown in the Loch")]
public static class DrownInTheLochFactory
{
    public const string CardName = "Drown in the Loch";
    public const string PrintedManaCost = "{U}{B}";

    public const int ModeCounter = 0;
    public const int ModeDestroy = 1;

    /// <summary>CR 700.2d — "Choose one" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target spell with mana value X or less.",
        "Destroy target creature with mana value X or less.",
    };

    /// <summary>
    /// Compute X — the largest mana value among cards in any
    /// opponent's graveyard. Returns 0 when no opponents are visible or
    /// every opponent graveyard is empty.
    /// </summary>
    public static int ComputeX(Player caster, IReadOnlyList<Player>? allPlayers)
    {
        if (allPlayers == null) return 0;

        var max = 0;
        foreach (var p in allPlayers)
        {
            if (ReferenceEquals(p, caster)) continue;
            foreach (var card in p.Zones.Graveyard.GetCards())
            {
                if (card is not Card concrete) continue;
                var mv = concrete.ManaCostValue.TotalValue;
                if (mv > max) max = mv;
            }
        }
        return max;
    }

    /// <summary>
    /// Build the SpellDefinition for Drown in the Loch. The caller
    /// resolves targets through <paramref name="targetResolver"/>
    /// (typically a <c>StackResolver</c>) and supplies the live
    /// <paramref name="stack"/> for the counter mode.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — emit a target request per mode that takes a
        // target. MinTargets=0 so unchosen modes don't gate the cast.
        var targetRequests = new[]
        {
            // Mode 0 — counter target spell (mv ≤ X resolution gate).
            // Agent-prompt MVP: gatherer enumerates the live stack so the
            // agent ranks among the actual spells in flight (Counter intent
            // picks the most-expensive entry).
            new TargetRequest(
                "target spell",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Counter,
                CandidateGatherer: ctx => ctx.Stack.GetAll()
                    .OfType<Majik.Core.Spells.ISpell>()
                    .Cast<object>()
                    .ToList()),
            // Mode 1 — destroy target creature (mv ≤ X resolution gate).
            // Agent-prompt MVP: enumerate every creature on the battlefield;
            // Removal intent ranks opponent's biggest threat first.
            new TargetRequest(
                "target creature",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .OfType<Creature>()
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
                // Honour either the multi-pick list (first entry wins
                // for Choose-one) or the legacy scalar ModeIndex.
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
                            effectsOut.Add(BuildCounterEffect(caster, p, targetResolver, stack));
                            break;
                        case ModeDestroy:
                            effectsOut.Add(BuildDestroyEffect(caster, p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildCounterEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        Fx.Inline("Drown in the Loch — counter target spell with mv ≤ X", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;
            if (spell.Card is not Card spellCard) return;

            // CR 608.2b — mv-≤-X gate at resolution. X comes from the
            // current opponent-graveyard scan (CR 700.2g — modes evaluate
            // against game state at resolution, not declaration).
            var x = ComputeX(caster, p.AllPlayers);
            if (spellCard.ManaCostValue.TotalValue > x) return;

            // CR 701.5 — counter → top of graveyard via the shared facade.
            Fx.Counter(stack, spell);
        });

    private static IEffect BuildDestroyEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        Fx.Inline("Drown in the Loch — destroy target creature with mv ≤ X", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not Creature creature) return;

            // CR 608.2b — mv-≤-X gate at resolution.
            var x = ComputeX(caster, p.AllPlayers);
            if (creature.ManaCostValue.TotalValue > x) return;

            // CR 701.7 — destroy → owner's graveyard (Indestructible /
            // regeneration deferred, same gap as SlaughterPactFactory).
            Fx.MoveToGraveyard(creature);
        });
}
