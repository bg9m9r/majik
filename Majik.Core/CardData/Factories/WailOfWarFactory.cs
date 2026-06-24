using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wail of War (Modern Horizons 3, {2}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Choose one —
///     • Creatures target opponent controls get -1/-1 until end of turn.
///     • Return up to two target creature cards from your graveyard to your
///       hand."
///
/// CR 700.2d — modal "Choose one —" spell. Two <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so the unchosen mode doesn't gate the cast). Pattern mirrors
/// <see cref="IzzetCharmFactory"/> for the modal choose-one shape.
///
/// Mode 0 — "Creatures target opponent controls get -1/-1 until end of turn":
/// targets a SINGLE opponent player (CR 109.5 — "target opponent"), then on
/// resolution sweeps every creature on that one player's battlefield with a
/// <see cref="PumpUntilEndOfTurnEffect"/>(-1, -1) (CR 514.2 + CR 613 Layer 7c).
/// This is the per-opponent-targeted variant of
/// <see cref="CowerInFearFactory"/>'s all-opponents -1/-1 sweep — the
/// difference is the target opponent restricts the sweep to one player's
/// creatures (CR 608.2b — re-checked at resolution).
///
/// Mode 1 — "Return up to two target creature cards from your graveyard to
/// your hand": a 0..2 graveyard-creature-card target request (CR 601.2c —
/// "up to two"), each chosen card moved Graveyard → Hand. Same
/// graveyard-creature-return shape as <see cref="GravediggerFactory"/>, but
/// up to two targets and routed through the spell pipeline. The creature-card
/// filter (CR 109.3 — "creature card" = a card with the creature type in any
/// zone) is re-checked at resolution (CR 608.2b — illegal target → clean
/// no-op for that slot; remaining legal targets still resolve).
/// </summary>
[CardName("Wail of War")]
public static class WailOfWarFactory
{
    public const string CardName = "Wail of War";
    public const string Slug = "wail-of-war";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>Mode 0 — creatures target opponent controls get -1/-1 EOT.</summary>
    public const int ModeMinusOne = 0;

    /// <summary>Mode 1 — return up to two creature cards from your graveyard.</summary>
    public const int ModeReturn = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Creatures target opponent controls get -1/-1 until end of turn.",
        "Return up to two target creature cards from your graveyard to your hand.",
    };

    /// <summary>
    /// Build Wail of War as an Instant from the embedded JSON def, with
    /// owner / controller wired. Suitable for identity / shape / dispatcher
    /// tests.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Wail of War.
    /// Both modes are wired. Mode 0 takes a single opponent player; mode 1
    /// takes up to two creature cards from the caster's graveyard.
    /// </summary>
    /// <param name="caster">The spell's controller. "Your graveyard" (mode 1)
    /// and the opponent legality check (mode 0) both scope to this player.</param>
    /// <param name="targetResolver">Maps each agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target request per mode. MinTargets=0 so the
        // unchosen mode doesn't gate the cast (mirrors IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — target opponent (CR 109.5). PrintedMinTargets=1 so a
            // chosen mode-0 cast with no legal opponent rewinds (CR 601.2c).
            new TargetRequest(
                Description: "target opponent",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Wrath,
                PrintedMinTargets: 1),

            // Mode 1 — up to two creature cards in your graveyard (CR 601.2c).
            new TargetRequest(
                Description: "up to two target creature cards in your graveyard",
                MinTargets: 0,
                MaxTargets: 2,
                LegalCandidates: caster.Zones.Graveyard.GetCards()
                    .Where(c => c.HasType(CardType.Creature))
                    .Cast<object>().ToList(),
                Intent: BotIntent.Reanimate),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[] { BotIntent.Wrath, BotIntent.Reanimate },
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
                        case ModeMinusOne:
                            effectsOut.Add(BuildMinusOneEffect(caster, p, targetResolver));
                            break;
                        case ModeReturn:
                            effectsOut.Add(BuildReturnEffect(caster, p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    /// <summary>
    /// Mode 0 — "Creatures target opponent controls get -1/-1 until end of
    /// turn." Resolves the single targeted opponent, then registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(-1, -1) on each creature on that
    /// player's battlefield (CR 514.2 + CR 613 Layer 7c).
    /// </summary>
    private static IEffect BuildMinusOneEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect(
            $"{CardName} — creatures target opponent controls get -1/-1 until end of turn",
            () =>
            {
                if (p.Targets.Count <= ModeMinusOne) return;
                var slot = p.Targets[ModeMinusOne];
                if (slot.Count == 0) return;

                var resolved = resolver(slot[0]);

                // CR 608.2b — the chosen target must be a player other than
                // the caster ("target opponent", CR 109.5). Illegal at
                // resolution → clean no-op.
                if (resolved is not Player opponent) return;
                if (ReferenceEquals(opponent, caster)) return;

                // Snapshot before applying so any same-step zone-move side
                // effects don't disturb enumeration (Cower in Fear pattern).
                foreach (var creature in opponent.Zones.Battlefield
                             .GetCards()
                             .OfType<Creature>()
                             .ToList())
                {
                    if (creature.Zone != ZoneType.Battlefield) continue;

                    // Shape-only guard: skip when ActiveEffects is null (test
                    // fixtures without a live ContinuousEffectsService).
                    if (creature.ActiveEffects == null) continue;

                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, -1, -1));
                }
            });

    /// <summary>
    /// Mode 1 — "Return up to two target creature cards from your graveyard
    /// to your hand." Each chosen target is independently validated as a
    /// creature card still in the caster's graveyard (CR 608.2b /
    /// CR 109.3) and moved Graveyard → Hand.
    /// </summary>
    private static IEffect BuildReturnEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect(
            $"{CardName} — return up to two creature cards from your graveyard to your hand",
            () =>
            {
                if (p.Targets.Count <= ModeReturn) return;
                var slot = p.Targets[ModeReturn];

                foreach (var token in slot)
                {
                    var resolved = resolver(token);
                    if (resolved is not ICard card) continue;

                    // CR 608.2b — target must still be a creature card in the
                    // caster's graveyard at resolution; otherwise skip (the
                    // other chosen target, if legal, still resolves).
                    if (!card.HasType(CardType.Creature)) continue;
                    if (card.Zone != ZoneType.Graveyard) continue;
                    if (!caster.Zones.Graveyard.GetCards().Contains(card)) continue;

                    caster.Zones.Graveyard.RemoveCard(card);
                    caster.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });
}
