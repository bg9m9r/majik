using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Defile (Modern Horizons 2, {B}).
///
/// Instant. Oracle text:
///   "Defile deals damage to target creature equal to the number of Swamps
///    you control. That creature gets -X/-X until end of turn, where X is
///    that damage."
///
/// ## Implemented (v1)
/// - Instant card shape ({B}, Black) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - 1..1 "target creature" <see cref="TargetRequest"/> (Intent: Removal),
///   mirrors <see cref="DismemberFactory"/>'s target shape.
/// - Resolve effect (<see cref="BuildSpellDefinition"/>):
///   1. <c>N</c> = number of Swamps controller controls — scans the
///      controller's battlefield for permanents with the
///      <see cref="CardSubtype.Swamp"/> land subtype (CR 305.6 — the
///      basic-land-name → subtype mapping).
///   2. Deals <c>N</c> damage to the chosen creature via
///      <see cref="Fx.DealDamage"/> (CR 119 — damage is "marked" on the
///      creature until cleanup, lethal damage triggers SBAs CR 704.5g).
///   3. Registers a <see cref="PumpUntilEndOfTurnEffect"/>(<c>-N</c>,
///      <c>-N</c>) on the target's <see cref="Creature.ActiveEffects"/>
///      (CR 514.2 — expires EOT). Cumulative with the damage half:
///      marked damage ≥ effective toughness still triggers lethal
///      (CR 704.5g) so the printed "damage + -X/-X" combo is the same
///      lethal that Dismember / Last Gasp impose.
/// - <c>N == 0</c> (no Swamps) collapses to a clean no-op: 0 damage is a
///   no-op via <see cref="Fx.DealDamage"/>'s amount-≤-0 guard, and the
///   pump is skipped (no point registering <c>(0, 0)</c>).
///
/// ## Deferred (v1 gaps)
/// - <b>Snow-land Swamps</b>: counted because <see cref="CardSubtype.Swamp"/>
///   is the subtype, regardless of Snow supertype (CR 205.4a — same as
///   Boros Reckoner's "Mountain" count would). Defile's printed text
///   says "Swamps", not "non-Snow Swamps", so this is correct.
/// - <b>Indestructible</b>: damage marking and the -X/-X both apply; the
///   lethal SBA gap inherited from the destroy family is irrelevant here
///   (Defile is damage, not destroy — CR 704.5g applies cleanly).
/// </summary>
[CardName("Defile")]
public static class DefileFactory
{
    public const string CardName = "Defile";
    public const string PrintedManaCost = "{B}";

    /// <summary>CardDef DSL — card shape only. The N-damage + -N/-N body
    /// lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Count how many Swamps <paramref name="controller"/> currently
    /// controls (CR 305.6 — Swamp subtype on a land controller owns).
    /// Exposed for bot policies + tests that want to sample the value
    /// without resolving the full spell. Returns 0 for null input.
    /// </summary>
    public static int CountSwamps(Player controller)
    {
        if (controller == null) return 0;
        return controller.Zones.Battlefield.GetCards()
            .OfType<Card>()
            .Count(c => c.HasType(CardType.Land) && c.HasSubtype(CardSubtype.Swamp));
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Mirrors
    /// <see cref="DismemberFactory.BuildDefinition"/>'s shape modulo the
    /// dynamic <c>N</c> read.
    /// </summary>
    /// <param name="caster">Spell controller — whose Swamp count is read
    /// at resolution.</param>
    /// <param name="targetResolver">Target resolver supplied by the
    /// caller's <see cref="GameContext"/> (chosen target → live game
    /// object). Same shape as <see cref="ForceOfNegationFactory.BuildDefinition"/>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — N damage + -N/-N where N = Swamps controller controls",
                        () => Resolve(caster, resolved)),
                };
            });
    }

    private static void Resolve(Player caster, object target)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (target is not Creature creature) return;
        if (creature.Zone != ZoneType.Battlefield) return;

        var n = CountSwamps(caster);
        if (n <= 0) return; // no Swamps → 0 damage + 0/0 pump = clean no-op

        // CR 119 — N damage marked on the creature; lethal SBA at cleanup
        // / next SBA check (CR 704.5g + 704.5h).
        Fx.DealDamage(creature, n);

        // CR 514.2 — -N/-N until end of turn. Cumulative with the damage:
        // marked damage ≥ effective toughness lethals via 704.5g, while
        // toughness ≤ 0 lethals via 704.5h. Same shape as Last Gasp /
        // Dismember's pure pump path.
        if (creature.ActiveEffects == null) return;
        creature.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(creature, -n, -n));
    }
}
