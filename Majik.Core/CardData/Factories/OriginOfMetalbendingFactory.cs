using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Origin of Metalbending (Avatar: The Last Airbender,
/// {1}{G}).
///
/// Instant — Lesson. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy target artifact or enchantment.
///     • Put a +1/+1 counter on target creature you control. It gains
///       indestructible until end of turn. (Damage and effects that say
///       "destroy" don't destroy it.)"
///
/// CR 700.2d — modal "Choose one —" spell. Each mode takes its own target, so
/// the bound <see cref="SpellDefinition"/> exposes one
/// <see cref="TargetRequest"/> slot per mode (the chosen-mode index lines up
/// with its target slot), each with MinTargets=0 so the unchosen mode does
/// not gate the cast (mirrors <see cref="RipApartFactory"/> /
/// <see cref="BorosCharmFactory"/>).
///
/// "Lesson" is a subtype with no rules outside the Learn mechanic (CR 702.149)
/// — it does not affect this card's resolution, so it is not modelled here.
///
/// Mode 0 — "Destroy target artifact or enchantment": mirrors
/// <see cref="RipApartFactory"/>'s destroy mode. On resolution the target is
/// destroyed (CR 701.7) iff it is still a Permanent on the battlefield with
/// type Artifact or Enchantment at resolution (CR 608.2b / CR 301–303).
/// Indestructible (CR 702.12) and active regeneration shields (CR 701.15) are
/// honoured via the Destroy reason — Origin of Metalbending does not print
/// "can't be regenerated".
///
/// Mode 1 — "Put a +1/+1 counter on target creature you control. It gains
/// indestructible until end of turn.": re-checks the resolved target is still
/// a battlefield <see cref="Creature"/> controlled by the caster (CR 608.2b —
/// "you control" is re-evaluated at resolution), then places one
/// <see cref="CounterType.PlusOnePlusOne"/> counter (CR 122 / CR 121.2) via
/// <see cref="Fx.PlaceCounter"/> and registers a layer-6
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Indestructible"
/// (CR 613.1f / 702.12, expiring at cleanup CR 514.2) — same indestructible
/// grant shape as <see cref="BorosCharmFactory"/>'s mode 1.
///
/// Card shape comes from the embedded JSON (<c>origin-of-metalbending.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a modal <see cref="SpellDefinition"/>
/// needs a target resolver + caster supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
/// </summary>
[CardName("Origin of Metalbending")]
public static class OriginOfMetalbendingFactory
{
    public const string CardName = "Origin of Metalbending";
    public const string Slug = "origin-of-metalbending";

    public const int ModeDestroy = 0;
    public const int ModeCounter = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy target artifact or enchantment.",
        "Put a +1/+1 counter on target creature you control. It gains indestructible until end of turn.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
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
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for Origin
    /// of Metalbending. One target slot per mode (CR 601.2c) with MinTargets=0
    /// so the unchosen mode does not gate the cast.
    /// </summary>
    /// <param name="caster">The player casting the spell — needed to scope
    /// mode 1's "target creature you control" candidate gathering and its
    /// resolution-time control re-check (CR 608.2b).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service. Required for mode 1's indestructible grant to register the
    /// layer-6 effect. When null the counter is still placed but the
    /// indestructible grant falls back to the target's own
    /// <see cref="Permanent.ActiveEffects"/> (mirrors BorosCharm / TemurBattleRage).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index lines
        // up with its target slot. MinTargets=0 so the unchosen mode doesn't
        // gate the cast (mirrors RipApartFactory).
        var targetRequests = new[]
        {
            // Mode 0 — destroy target artifact or enchantment.
            new TargetRequest(
                "target artifact or enchantment",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Removal,
                // Agent-prompt: every artifact + enchantment on the
                // battlefield across all players (CR 301 / CR 303).
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Artifact)
                             || c.HasType(CardType.Enchantment))
                    .Cast<object>()
                    .ToList()),

            // Mode 1 — +1/+1 counter on target creature you control.
            new TargetRequest(
                "target creature you control",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Buff,
                // Agent-prompt: only creatures the caster controls (CR 302).
                CandidateGatherer: ctx => caster.Zones.Battlefield
                    .GetCards()
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
                BotIntent.Removal,
                BotIntent.Buff,
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
                        case ModeDestroy:
                            effectsOut.Add(BuildDestroyEffect(p, targetResolver));
                            break;
                        case ModeCounter:
                            effectsOut.Add(BuildCounterEffect(p, caster, targetResolver, continuousEffects));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: destroy target artifact or enchantment
    // -----------------------------------------------------------------------

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target artifact or enchantment", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // Oracle constraint: target must be an artifact or enchantment at
            // resolution (CR 608.2b / CR 301–303).
            if (!target.HasType(CardType.Artifact)
                && !target.HasType(CardType.Enchantment)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) are honoured via the Destroy-reason gate in
            // MoveToGraveyard; this card does not print "can't be regenerated".
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    // -----------------------------------------------------------------------
    // Mode 1: +1/+1 counter on target creature you control;
    //         it gains indestructible until end of turn
    // -----------------------------------------------------------------------

    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Player caster,
        Func<object, object> resolver,
        ContinuousEffectsService? continuousEffects) =>
        new Effect($"{CardName} — +1/+1 counter + indestructible until end of turn", () =>
        {
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check. "Target creature
            // you control": still a battlefield creature AND still controlled
            // by the caster (control is re-evaluated at resolution).
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (!ReferenceEquals(target.Controller, caster)) return;

            // CR 122 / CR 121.2 — place one +1/+1 counter.
            Fx.PlaceCounter(target, CounterType.PlusOnePlusOne, 1);

            // CR 613.1f / 702.12 — grant indestructible until end of turn
            // (CR 514.2 cleanup expiry). Prefer the supplied continuous-effects
            // service; fall back to the target's own ActiveEffects so the grant
            // still registers when the caller didn't pass one (mirrors
            // BorosCharm / TemurBattleRage).
            var svc = continuousEffects ?? target.ActiveEffects;
            svc?.Register(new GrantKeywordUntilEndOfTurnEffect(target, "Indestructible"));
        });
}
