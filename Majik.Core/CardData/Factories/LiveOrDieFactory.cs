using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Live or Die (Mystery Booster / reprint pool, {3}{B}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Choose one —
///     • Return target creature card from your graveyard to the battlefield.
///     • Destroy target creature."
///
/// CR 700.2d — modal "Choose one —" spell. Each mode takes its own target,
/// so the bound <see cref="SpellDefinition"/> exposes one
/// <see cref="TargetRequest"/> slot per mode (the chosen-mode index lines up
/// with its target slot), each with MinTargets=0 so the unchosen mode does
/// not gate the cast (mirrors <see cref="RipApartFactory"/> /
/// <see cref="ArchmagesCharmFactory"/>).
///
/// Mode 0 — "Return target creature card from your graveyard to the
/// battlefield": a reanimate clause scoped to the caster's own graveyard
/// ("your graveyard"). Mirrors <see cref="HelpingHandFactory"/> minus the
/// mana-value cap and the "enters tapped" rider — Live or Die returns the
/// creature untapped with no restriction. CR 701.20 — graveyard →
/// battlefield under the caster's control (CR 110.2); ZoneService-routed when
/// supplied so ETB triggers fire (CR 603.6a). CR 608.2b — re-checked at
/// resolution (must still be a creature card in the caster's graveyard).
///
/// Mode 1 — "Destroy target creature": mirrors <see cref="RipApartFactory"/>'s
/// destroy clause, narrowed to creatures (CR 302). On resolution the target is
/// destroyed (CR 701.7) iff it is still a Creature on the battlefield at
/// resolution (CR 608.2b). Indestructible (CR 702.12) and active regeneration
/// shields (CR 701.15) are honoured via the Destroy reason — Live or Die does
/// not print "can't be regenerated".
///
/// Card shape comes from the embedded JSON (<c>live-or-die.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/> needs
/// the caster (the "your graveyard" source) and a target resolver supplied by
/// the caller's <see cref="GameContext"/> (not expressible in the data-only
/// JSON schema).
/// </summary>
[CardName("Live or Die")]
public static class LiveOrDieFactory
{
    public const string CardName = "Live or Die";
    public const string Slug = "live-or-die";
    public const string PrintedManaCost = "{3}{B}{B}";

    public const int ModeReturn = 0;
    public const int ModeDestroy = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Return target creature card from your graveyard to the battlefield.",
        "Destroy target creature.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Live or Die. One target slot
    /// per mode (CR 601.2c) with MinTargets=0 so the unchosen mode does not
    /// gate the cast; on resolution the chosen mode either returns a creature
    /// card from the caster's graveyard (mode 0) or destroys a creature
    /// (mode 1).
    /// </summary>
    /// <param name="caster">Spell controller — the graveyard whose creature
    /// card is returned ("your graveyard") and the destination battlefield
    /// (CR 110.2).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// cards directly.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move (mode 0) routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers fire (CR 603.6a).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index lines
        // up with its target slot. MinTargets=0 so the unchosen mode doesn't
        // gate the cast (mirrors RipApartFactory / ArchmagesCharm).
        var targetRequests = new[]
        {
            // Mode 0 — return a creature card from the caster's own graveyard
            // ("your graveyard"). CR 608.2b re-checked at resolution.
            new TargetRequest(
                "target creature card from your graveyard",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Reanimate,
                CandidateGatherer: _ => caster.Zones.Graveyard.GetCards()
                    .OfType<Creature>()
                    .Cast<object>()
                    .ToList()),
            // Mode 1 — destroy any creature on the battlefield (CR 302).
            new TargetRequest(
                "target creature",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature))
                    .Cast<object>()
                    .ToList()),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Reanimate,
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
                        case ModeReturn:
                            effectsOut.Add(BuildReturnEffect(caster, p, targetResolver, zoneService));
                            break;
                        case ModeDestroy:
                            effectsOut.Add(BuildDestroyEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildReturnEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver,
        ZoneService? zoneService) =>
        Fx.Inline($"{CardName} — return target creature card from your graveyard to the battlefield", () =>
        {
            if (p.Targets.Count <= ModeReturn) return;
            var slot = p.Targets[ModeReturn];
            if (slot.Count == 0) return;
            var live = resolver(slot[0]);

            // CR 608.2b — must still be a creature card in the caster's own
            // graveyard ("your graveyard") at resolution; else no-op.
            if (live is not Creature creature) return;
            if (creature.Zone != ZoneType.Graveyard) return;
            if (!ReferenceEquals(creature.Owner, caster)) return;

            // CR 701.20 — graveyard → battlefield under the caster's control
            // (CR 110.2). ZoneService-routed when supplied so ETB triggers fire
            // (CR 603.6a). No mana-value cap, no "enters tapped" rider.
            Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);
        });

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target creature", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check: must still be a
            // creature on the battlefield.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Creature)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) are honoured via the Destroy-reason gate in
            // MoveToGraveyard; Live or Die does not print "can't be regenerated".
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });
}
