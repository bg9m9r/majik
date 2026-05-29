using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Damn (Modern Horizons 2, {B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy target creature. A creature destroyed this way can't be
///    regenerated.
///    Overload {2}{W}{W} (You may cast this spell for its overload cost. If
///    you do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Destroy each creature. A creature destroyed this way can't be
///    regenerated." — i.e. a Damnation-style one-sided-or-not board wipe.
///
/// The card's base shape (name, Sorcery type, {B}{B}) is materialised from
/// the embedded JSON definition (<c>damn.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The resolve behaviour
/// (destroy-no-regen, single target or each-creature) is layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express spell resolution
/// (same posture as <see cref="StormscaleScionFactory"/> et al.).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}{B}, Black (color derived from cost).
/// - <b>Destroy target creature (no regen)</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution the
///   targeted creature is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.DestroyNoRegeneration"/> iff it is still
///   a <see cref="Creature"/> on the battlefield (CR 608.2b illegal-target
///   gate). Same destroy posture as <see cref="TerminateFactory"/>:
///   indestructible (CR 702.12) still cancels the destroy, but any active
///   regeneration shield (CR 701.15) is bypassed rather than consumed —
///   honouring the printed "can't be regenerated" rider.
///
/// ## Overload (CR 702.96 — structural-flag-only, mirrors Vandalblast)
///
/// Overload is an alternative cost. The
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive (per
/// <c>MODERN_COVERAGE.md</c>) is a stub: it gates the cast and carries an
/// <c>IsOverloaded</c> flag, but is not yet plumbed through
/// <see cref="Majik.Core.Services.SpellCastFlow"/>'s payment loop, so the
/// "was overloaded?" bit does not flow from cast-time to the resolving stack
/// object. Until that infra lands, Damn ships with default-not-overloaded
/// behaviour: cast resolves as "destroy target creature". The overloaded
/// branch is structural — callers can opt in via <c>wasOverloaded: true</c>
/// on <see cref="BuildDefinition"/>, which drops the target request and
/// destroys EACH creature on every battlefield (CR 702.96b "target" → "each"
/// rewrite over "Destroy each creature"). Same posture as
/// <see cref="VandalblastFactory"/> / <see cref="MizziumMortarsFactory"/>.
///
/// ## CR notes
/// - CR 702.96 / 702.96b — Overload alt-cost; "target" → "each" rewrite.
/// - CR 701.7 — Destroy; CR 702.12 indestructible / CR 701.15 regeneration
///   honoured at the destroy site via DestroyNoRegeneration.
/// - CR 608.2b — resolution-time legality re-check (still a creature on the
///   battlefield).
/// </summary>
[CardName("Damn")]
public static class DamnFactory
{
    public const string CardName = "Damn";
    public const string Slug = "damn";
    public const string PrintedManaCost = "{B}{B}";
    public const string OverloadCostText = "{2}{W}{W}";

    /// <summary>
    /// Construct Damn from its embedded JSON definition. Card shape only —
    /// resolve behaviour (destroy-no-regen, single target or each-creature)
    /// is built on demand via <see cref="BuildDefinition"/>. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the Damn <see cref="SpellDefinition"/>.
    ///
    /// Default (<paramref name="wasOverloaded"/> = false): single 1..1
    /// "target creature" request. On resolve, validates the target is still
    /// a <see cref="Creature"/> on the Battlefield (CR 608.2b), then destroys
    /// it with the "can't be regenerated" rider (CR 701.7 / 701.15).
    ///
    /// Overloaded (<paramref name="wasOverloaded"/> = true): no target
    /// request; on resolve destroys EACH creature across
    /// <paramref name="allPlayers"/>' battlefields (CR 702.96b). Non-creature
    /// permanents are untouched.
    /// </summary>
    /// <param name="controller">The spell's controller. Accepted for parity
    /// with the other overload factories; Damn's "each" is board-wide so the
    /// controller's own creatures are also destroyed (CR 702.96b — the printed
    /// text is just "each creature", no "you don't control" clause).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    /// <param name="allPlayers">All players whose battlefields the overloaded
    /// sweep should reach. Optional for the default branch; required for the
    /// overloaded sweep. Defaults to a single-element list of
    /// <paramref name="controller"/> when omitted.</param>
    /// <param name="wasOverloaded">Whether the overload alt-cost was paid at
    /// cast time. Defaults to <c>false</c> — overload is not yet wired through
    /// <see cref="Majik.Core.Services.SpellCastFlow"/>.</param>
    public static SpellDefinition BuildDefinition(
        Player controller,
        Func<object, object> targetResolver,
        IReadOnlyList<Player>? allPlayers = null,
        bool wasOverloaded = false)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        var players = allPlayers ?? new[] { controller };

        if (wasOverloaded)
        {
            // CR 702.96b — overloaded branch. "target" rewritten to "each":
            // destroy each creature on every battlefield. Snapshot the
            // per-player creature list before applying so same-step zone
            // moves don't disturb enumeration. The "can't be regenerated"
            // rider applies to every destroyed creature (CR 701.15) via
            // DestroyNoRegeneration.
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect(
                        $"{CardName} (overloaded): destroy each creature (no regen).",
                        () =>
                        {
                            var seen = new HashSet<Creature>();
                            foreach (var pl in players)
                            {
                                foreach (var c in pl.Zones.Battlefield.GetCards()
                                             .OfType<Creature>()
                                             .ToList())
                                {
                                    if (!seen.Add(c)) continue;
                                    // CR 701.7 — Destroy. DestroyNoRegeneration:
                                    // indestructible (CR 702.12) still cancels,
                                    // regeneration shield (CR 701.15) bypassed.
                                    OracleSpellBinder.MoveToGraveyard(
                                        c, ZoneMoveReason.DestroyNoRegeneration);
                                }
                            }
                        }),
                });
        }

        // Default printed cast — single 1..1 "target creature" request;
        // resolve = destroy that creature (no regen).
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature (no regen).",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — Destroy. "A creature destroyed this
                            // way can't be regenerated" honoured via
                            // DestroyNoRegeneration: indestructible (CR 702.12)
                            // still cancels, regeneration (CR 701.15) bypassed.
                            OracleSpellBinder.MoveToGraveyard(
                                target, ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
