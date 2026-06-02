using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for By Force (Modern Horizons, {X}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy X target artifacts."
///
/// ## Why it gets its own factory
/// By Force is the stripped-down, artifact-only cousin of
/// <see cref="IndomitableCreativityFactory"/> ("Destroy X target artifacts
/// and/or creatures …") and the X-scaled multi-target sibling of
/// <see cref="VandalblastFactory"/> ("Destroy target artifact you don't
/// control"). It is pure removal: X chosen artifacts are destroyed, no rider.
/// Every primitive it leans on already ships — the open-cardinality
/// <see cref="TargetRequest"/> + <see cref="SpellDefinition.HasVariableX"/>
/// pattern from Indomitable Creativity, and the Destroy-reason graveyard move
/// from Vandalblast — so no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, printed cost <c>{X}{R}</c>, Red. Card shape comes from the
///   embedded JSON (<c>by-force.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="SpellDefinition.HasVariableX"/> = true so the cast flow prompts
///   for X at cast time (CR 601.2f). The chosen X arrives at resolution as the
///   cardinality of the supplied target list.
/// - One <see cref="TargetRequest"/> with <c>MinTargets = 0,
///   MaxTargets = int.MaxValue</c> gathering every artifact (CR 301) on every
///   battlefield (By Force can target artifacts ANY player controls — there is
///   no "you don't control" clause, unlike Vandalblast). v1 simplification —
///   the engine's <see cref="TargetRequest"/> can't yet bind
///   <c>MinTargets = X</c> dynamically (no X-keyed target-count primitive),
///   so callers supply exactly X chosen targets via
///   <see cref="ChosenSpellParams.Targets"/> and the resolve closure clamps to
///   the supplied list. Same posture as Indomitable Creativity.
/// - Resolve: for each chosen target still on the battlefield (CR 608.2b) AND
///   still an Artifact, destroy via <see cref="Fx.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> — indestructible (CR 702.12) and
///   regeneration (CR 701.15) gates apply normally at the destroy site.
///
/// ## Rules citations
/// - CR 601.2f — X is chosen as the spell is cast (variable cost).
/// - CR 301 — Artifact card type; the only legal target type.
/// - CR 608.2b — resolution-time legality re-check (still on battlefield,
///   still an artifact) before each destroy.
/// - CR 701.7 — Destroy; CR 702.12 indestructible / CR 701.15 regeneration
///   honoured at the destroy site.
///
/// ## v1 gaps (shared with Indomitable Creativity)
/// - <b>X-keyed target count</b>: there is no <c>MinTargets = X</c> binding on
///   <see cref="TargetRequest"/>; callers must pre-supply exactly X targets.
///   The resolve closure trusts the chosen-target list cardinality.
/// </summary>
[CardName("By Force")]
public static class ByForceFactory
{
    public const string CardName = "By Force";
    public const string Slug = "by-force";
    public const string PrintedManaCost = "{X}{R}";

    /// <summary>Build the card shape (name / Sorcery / {X}{R}) from the
    /// embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> By Force uses on resolution.
    /// <see cref="SpellDefinition.HasVariableX"/> is true so the engine prompts
    /// for X at cast time; the <see cref="TargetRequest"/> is open-cardinality
    /// and callers pre-supply exactly X targets (see class xmldoc gap note).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition()
    {
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "X target artifacts",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 301 — any artifact on any battlefield is a legal
                    // candidate. By Force has no "you don't control" clause.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p => new IEffect[]
            {
                new Effect(
                    $"{CardName}: destroy X target artifacts.",
                    () => Resolve(p.Targets.Count == 0
                        ? Array.Empty<object>()
                        : p.Targets[0])),
            });
    }

    /// <summary>
    /// Resolve By Force against the supplied chosen targets: destroy each that
    /// is still a legal artifact permanent on the battlefield (CR 608.2b).
    /// Returns the permanents actually destroyed (left the battlefield).
    /// Exposed for direct invocation by tests / bots without driving the full
    /// cast flow.
    /// </summary>
    public static IReadOnlyList<Permanent> Resolve(IReadOnlyList<object> chosenTargets)
    {
        ArgumentNullException.ThrowIfNull(chosenTargets);

        var destroyed = new List<Permanent>();
        foreach (var raw in chosenTargets)
        {
            // CR 608.2b — resolution-time legality re-check.
            if (raw is not Permanent perm) continue;
            if (perm.Zone != ZoneType.Battlefield) continue;
            // Oracle constraint: must still be an artifact (CR 301 / 608.2b).
            if (!perm.HasType(CardType.Artifact)) continue;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) are honoured by the Destroy-reason gate in
            // MoveToGraveyard; such a permanent stays on the battlefield and is
            // not recorded as destroyed.
            Fx.MoveToGraveyard(perm, ZoneMoveReason.Destroy);

            if (perm.Zone != ZoneType.Battlefield)
            {
                destroyed.Add(perm);
            }
        }

        return destroyed;
    }
}
