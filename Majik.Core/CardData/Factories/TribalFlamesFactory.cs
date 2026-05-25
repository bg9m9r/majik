using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Primitives;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using DomainRule = Majik.Core.Rules.Domain;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tribal Flames (Onslaught / Modern Horizons 2,
/// {1}{R}).
///
/// Sorcery. Oracle text:
///   "Tribal Flames deals X damage to any target, where X is the number
///    of basic land types among lands you control."
///
/// ## Implementation
///
/// CR 702.16 — <b>Domain</b>. Count the number of distinct basic land
/// types ({Plains, Island, Swamp, Mountain, Forest}) among lands the
/// spell's controller controls. A single dual land (e.g. Stomping
/// Ground = Mountain + Forest) contributes both of its basic land
/// types. Duplicates across multiple lands collapse — only DISTINCT
/// basic types count.
///
/// Subtype source is the <see cref="ContinuousEffectsService"/> layer
/// pipeline (CR 613.1d). When the live service is supplied, the count
/// uses <see cref="PermanentCharacteristics.Subtypes"/> from
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>, so layer
/// 4 retype effects (Blood Moon, Spreading Seas, Urborg, Yavimaya) feed
/// through correctly: under Blood Moon every nonbasic becomes Mountain
/// → domain ≤ 1. Without the layer service the printed subtypes are
/// used (suitable for tests / shape-only use).
///
/// Card-shape only here; the resolve-time spell definition (target +
/// damage effect with domain-driven amount) is built on-demand via
/// <see cref="BuildSpellDefinition(Player, ContinuousEffectsService?, Func{object, object})"/>
/// because <see cref="SpellDefinition"/> needs a target resolver
/// supplied by the caller's <see cref="GameContext"/>.
/// </summary>
[CardName("Tribal Flames")]
public static class TribalFlamesFactory
{
    public const string CardName = "Tribal Flames";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>
    /// The five basic land subtypes that contribute to Domain
    /// (CR 702.16 / CR 205.3i / 305.6). Forwarded to
    /// <see cref="Domain.BasicLandTypes"/> — the canonical primitive.
    /// Kept here as a back-compat surface for callers / tests pinned to
    /// the old factory-local handle.
    /// </summary>
    public static IReadOnlySet<CardSubtype> BasicLandTypes => DomainRule.BasicLandTypes;

    /// <summary>CardDef DSL — card shape only. Domain-driven damage body
    /// is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Tribal Flames
    /// is cast. Single 1..1 "any target" request; on resolution the
    /// controller's battlefield is sampled, distinct basic land types
    /// are counted (CR 702.16), and that many damage is dealt to the
    /// chosen target.
    /// </summary>
    /// <param name="controller">Spell controller — the lands whose
    /// distinct basic-type count drives X.</param>
    /// <param name="effects">Live continuous-effects service. When
    /// supplied, effective subtypes from the layer pipeline are used
    /// (Blood Moon, Spreading Seas, Urborg, Yavimaya feed through).
    /// When null, printed subtypes are used.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        ContinuousEffectsService? effects,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Tribal Flames: deal Domain damage", () =>
                    {
                        var amount = DomainRule.CountTypes(controller, effects);
                        Fx.DealDamage(target, amount);
                    }),
                };
            });
    }

    /// <summary>
    /// CR 702.16 — number of distinct basic land types among lands the
    /// controller controls. Thin shim over the canonical
    /// <see cref="DomainRule.CountTypes(Player, ContinuousEffectsService?)"/>
    /// primitive; preserved here as a back-compat surface for callers /
    /// tests pinned to the old factory-local handle. New code should
    /// call <see cref="DomainRule.CountTypes(Player, ContinuousEffectsService?)"/>
    /// directly.
    /// </summary>
    public static int CountDomain(Player controller, ContinuousEffectsService? effects) =>
        DomainRule.CountTypes(controller, effects);
}
