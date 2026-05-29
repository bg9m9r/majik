using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cathartic Pyre (Innistrad: Midnight Hunt, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Cathartic Pyre deals 3 damage to target creature or planeswalker.
///     • Discard up to two cards, then draw that many cards."
///
/// CR 700.2d — modal "Choose one —" spell. Two <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so the unchosen mode doesn't gate the cast). Pattern mirrors
/// <see cref="AbradeFactory"/> / <see cref="IzzetCharmFactory"/> for the modal
/// choose-one shape.
///
/// ## Implemented (v1)
/// - <b>Identity</b> — Instant, {1}{R}, red. Card shape comes from the
///   embedded JSON (<c>cathartic-pyre.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="DemandAnswersFactory"/> / <see cref="AbradedBluffsFactory"/>).
/// - <b>Mode 0 — "3 damage to target creature or planeswalker"</b>: a single
///   1..1 "target creature or planeswalker" candidate gatherer (CR 301 /
///   306.7) like <see cref="BitterTriumphFactory"/>; on resolution
///   <see cref="Fx.DealDamageAny(object, int)"/> routes creature damage
///   (CR 120.1a) and planeswalker loyalty removal (CR 306.7) — same posture as
///   <see cref="SearingBlazeFactory"/>'s planeswalker-aware damage. CR 608.2b
///   resolution-time re-check: target must still be a creature or planeswalker
///   on the battlefield.
/// - <b>Mode 1 — "discard up to two cards, then draw that many cards"</b>:
///   <see cref="Fx.Discard(Player, int)"/> discards up to two (CR 701.16),
///   returning the count actually discarded, then
///   <see cref="Fx.DrawCards(Player, int)"/> draws exactly that many
///   (CR 120). An empty hand discards zero → draws zero (the "that many" is
///   the actual discard count, not the printed maximum). Empty library
///   mid-draw stamps the SBA-loss flag (CR 704.5b) without throwing.
///
/// ## Deferred (v1 gaps)
/// - <b>"Discard up to two" agent prompt</b>. v1 uses the deterministic
///   <see cref="Fx.Discard"/> first-in-hand pick and always discards the
///   maximum available (capped at two) rather than letting the controller
///   choose how many / which cards. Same queue as Faithless Looting /
///   Izzet Charm / Liliana of the Veil.
/// </summary>
[CardName("Cathartic Pyre")]
public static class CatharticPyreFactory
{
    public const string CardName = "Cathartic Pyre";
    public const string Slug = "cathartic-pyre";
    public const string PrintedManaCost = "{1}{R}";

    public const int ModeDamage  = 0;
    public const int ModeRummage = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>CR 120 / 701.16 — "Discard up to two cards".</summary>
    public const int MaxRummage = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Cathartic Pyre deals 3 damage to target creature or planeswalker.",
        "Discard up to two cards, then draw that many cards.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Cathartic Pyre. Both modes
    /// are wired.
    /// </summary>
    /// <param name="caster">The player who cast Cathartic Pyre; discards and
    /// draws for mode 1.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand engine
    /// objects directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes a
        // target. MinTargets=0 so the unchosen mode doesn't gate the cast
        // (mirrors AbradeFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — 3 damage to target creature or planeswalker. Live
            // gatherer (CR 301 / 306.7): every creature + planeswalker on
            // every battlefield. Bot ranks opponent permanents highest via
            // Removal intent (mirrors BitterTriumphFactory).
            new TargetRequest(
                Description: "target creature or planeswalker",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature)
                        || c.HasType(CardType.Planeswalker))
                    .Cast<object>()
                    .ToList()),
            // Mode 1 — discard up to two, then draw that many (no target).
            new TargetRequest(
                Description: "no target",
                MinTargets: 0,
                MaxTargets: 0,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Draw),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,
                BotIntent.Draw,
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
                        case ModeDamage:
                            effectsOut.Add(BuildDamageEffect(p, targetResolver));
                            break;
                        case ModeRummage:
                            effectsOut.Add(BuildRummageEffect(caster));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — deals 3 damage to target creature or planeswalker", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check: the target must
            // still be a creature or planeswalker on the battlefield.
            if (resolved is not Permanent target) return;
            if (target.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Creature)
                && !target.HasType(CardType.Planeswalker)) return;

            // CR 120.1a (creature damage) / CR 306.7 (planeswalker loyalty
            // removal) — Fx.DealDamageAny routes both.
            Fx.DealDamageAny(target, 3);
        });

    private static IEffect BuildRummageEffect(Player caster) =>
        new Effect($"{CardName} — discard up to two cards, then draw that many", () =>
        {
            // CR 701.16 — "Discard up to two cards." v1 deterministic
            // first-in-hand pick (agent-driven choice deferred — same queue
            // as Faithless Looting / Izzet Charm). Returns the count actually
            // discarded (empty hand → zero).
            var discarded = Fx.Discard(caster, MaxRummage).Count;

            // CR 120 — "then draw that many cards." Draw exactly the number
            // actually discarded — NOT the printed maximum. Empty library
            // stamps the SBA-loss flag (CR 704.5b) without throwing.
            Fx.DrawCards(caster, discarded);
        });
}
