using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inspiring Call (Commander 2013 and reprints,
/// {2}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Draw a card for each creature you control with a +1/+1 counter on it.
///    Those creatures gain indestructible until end of turn. (Damage and
///    effects that say "destroy" don't destroy them.)"
///
/// ## Implementation
/// Card shape (name, Instant, {2}{G}) comes from the embedded JSON
/// (<c>inspiring-call.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> — same JSON-backed posture as
/// <see cref="TaintedStrikeFactory"/>. No new mechanic: variable card-draw
/// (draw-N over a counted set, like <see cref="DivinationFactory"/>) plus the
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Indestructible") grant used
/// by <see cref="AdantoVanguardFactory"/> / <see cref="BorosCharmFactory"/>.
///
/// The spell has no targets (CR 601 — "each creature you control with a +1/+1
/// counter on it" is a counted set evaluated at resolution, not a target).
///
/// On resolve (CR 608.3 — one-shot resolution):
///   1. Snapshot the caster's battlefield creatures that have ≥1 +1/+1
///      counter (CR 122) — the same set is used for both the draw count and
///      the indestructible grant, so a creature is counted at most once
///      regardless of how many counters it carries.
///   2. Caster draws one card per creature in that set (CR 121.1). Each draw
///      routes through <see cref="Fx.DrawCards"/> (replacement bus per draw;
///      empty library stamps the CR 704.5b SBA loss flag without throwing).
///      Drawing zero (empty set) is a no-op.
///   3. Each creature in that set gains "Indestructible" until end of turn
///      (CR 702.12 / CR 613.1f Layer 6), registered on the creature's own
///      <see cref="Creature.ActiveEffects"/> and expiring in the cleanup step
///      (CR 514.2). Creatures whose effects service is unwired (shape-only
///      tests) are skipped for the grant but still counted for the draw.
/// </summary>
[CardName("Inspiring Call")]
public static class InspiringCallFactory
{
    public const string CardName = "Inspiring Call";
    public const string Slug = "inspiring-call";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>Keyword granted until end of turn (CR 702.12).</summary>
    public const string GrantedKeyword = "Indestructible";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Inspiring Call. No modes,
    /// no X, no target requests — the body resolves entirely on the caster's
    /// own board (CR 601 — counted set, not a target).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the resolve effect: draw one card per controlled creature with a
    /// +1/+1 counter, then grant exactly those creatures indestructible until
    /// end of turn. The qualifying set is snapshotted once and reused for both
    /// halves so each creature counts at most once (CR 122).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw one card per creature you control with a +1/+1 counter; those creatures gain indestructible until end of turn.",
                () =>
                {
                    // CR 122 — snapshot the qualifying creatures (≥1 +1/+1
                    // counter). One pass drives both the draw count and the
                    // grant, so a creature with multiple counters is counted
                    // once.
                    var qualifying = caster.Zones.Battlefield
                        .GetCards()
                        .OfType<Creature>()
                        .Where(c => c.Zone == ZoneType.Battlefield
                                    && c.Counters.Count(CounterType.PlusOnePlusOne) > 0)
                        .ToList();

                    // CR 121.1 — draw one per qualifying creature. Fx.DrawCards
                    // no-ops on count 0 and routes the replacement bus per
                    // draw; an empty library stamps the SBA loss flag
                    // (CR 704.5b) without throwing.
                    Fx.DrawCards(caster, qualifying.Count);

                    // CR 702.12 / CR 613.1f Layer 6 — grant indestructible
                    // until end of turn (CR 514.2 cleanup expiry) to exactly
                    // those creatures.
                    foreach (var creature in qualifying)
                    {
                        creature.ActiveEffects?.Register(
                            new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
                    }
                }),
        };
    }
}
