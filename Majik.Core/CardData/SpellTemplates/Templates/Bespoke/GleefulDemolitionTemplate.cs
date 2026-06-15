using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Bespoke spell template for Gleeful Demolition (Phyrexia: All Will Be One,
/// {R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-14):
///   "Destroy target artifact. If you controlled that artifact, create three
///    1/1 red Phyrexian Goblin creature tokens."
///
/// ## Why a bespoke template
/// The "destroy target artifact" clause alone is already covered by
/// <see cref="Destroy.DestroyArtifactEnchantmentTemplate"/> (Priority 50) —
/// but that generic template would silently DROP the conditional
/// own-artifact token rider (same hazard the Kuldotha Rebirth template guards
/// against: a generic match dropping the spell-specific extra). This template
/// owns BOTH halves and runs at a higher priority so it wins the registry
/// race for this exact oracle text.
///
/// ## Behaviour
/// - <b>Target</b> (CR 115.1): one "target artifact" — every artifact on every
///   battlefield is a legal candidate (CR 301). Bot intent: Removal.
/// - <b>Resolution</b> (CR 608.2): a single resolution that
///   1. snapshots whether the target artifact is controlled by the caster
///      ("you controlled that artifact") BEFORE destroying it — the past-tense
///      check reads the artifact's controller as it last existed on the
///      battlefield (CR 608.2). The destroy and the conditional rider are one
///      resolution, so the snapshot is taken at resolution start;
///   2. destroys the target via
///      <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///      with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///      (CR 702.12) / regeneration (CR 701.15) shields are honoured;
///   3. if the snapshot said the caster controlled it, creates three 1/1 red
///      Phyrexian Goblin creature tokens under the caster (CR 111 / 111.4).
///
/// CR 608.2b — resolution-time legality re-check: if the target has left the
/// battlefield (or is no longer an artifact) the spell does nothing on
/// resolution; the rider does not fire (its "that artifact" referent was never
/// destroyed by this spell).
/// </summary>
public sealed class GleefulDemolitionTemplate : ISpellTemplate
{
    /// <summary>CR 111 — "create three … tokens."</summary>
    public const int TokenCount = 3;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const string GoblinTokenName = "Goblin";

    // Anchor on the full oracle: destroy target artifact + the
    // own-artifact Phyrexian Goblin rider. Distinctive enough that no other
    // card matches; high priority so it beats the generic
    // DestroyArtifactEnchantmentTemplate.
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+artifact.*if\s+you\s+controlled\s+that\s+artifact.*phyrexian\s+goblin",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 120;
    public string Name => "GleefulDemolition";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        oracleText != null && Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Build(ctx.Caster, ctx.Resolver, ctx.Zones);
    }

    /// <summary>
    /// Build the runnable <see cref="SpellDefinition"/>. Shared with
    /// <see cref="Factories.GleefulDemolitionFactory.BuildSpellDefinition"/> so
    /// the prod binder path and the factory test path stay one source of truth.
    /// </summary>
    public static SpellDefinition Build(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        // CR 115.1 — one "target artifact". Live gatherer: every artifact on
        // every battlefield (CR 301).
        var targetRequest = new TargetRequest(
            "target artifact",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: ctx => ctx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .Where(c => c.HasType(CardType.Artifact))
                .Cast<object>()
                .ToList());

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { targetRequest },
            EffectFactory: p => new IEffect[]
            {
                new Effect("Gleeful Demolition: destroy target artifact (+ own-artifact Goblins)", () =>
                {
                    if (p.Targets.Count == 0 || p.Targets[0].Count == 0) return;
                    var resolved = resolver(p.Targets[0][0]);

                    // CR 608.2b — resolution-time legality re-check.
                    if (resolved is not Permanent target) return;
                    if (target.Zone != ZoneType.Battlefield) return;
                    if (!target.HasType(CardType.Artifact)) return;

                    // CR 608.2 — "If you controlled that artifact": snapshot the
                    // controller BEFORE the destroy mutates zones. The whole
                    // spell is one resolution; the past-tense condition reads
                    // the artifact's controller as it exists at resolution start.
                    var controlledByCaster = ReferenceEquals(target.Controller, caster);

                    // CR 701.7 — Destroy. Indestructible (CR 702.12) /
                    // regeneration (CR 701.15) honoured by the Destroy-reason
                    // gate in MoveToGraveyard.
                    OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);

                    if (controlledByCaster)
                    {
                        CreateGoblinTokens(caster, zones);
                    }
                }),
            });
    }

    /// <summary>
    /// CR 111 / 111.4 — create three 1/1 red Phyrexian Goblin creature tokens
    /// under <paramref name="caster"/>'s control. The token carries both the
    /// Phyrexian and Goblin creature subtypes (CR 205.3m).
    /// </summary>
    public static void CreateGoblinTokens(Player caster, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var spec = new TokenFactory.TokenSpec(
            Name: GoblinTokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Goblin },
            Keywords: null,
            Colors: new[] { ManaColor.Red });

        for (var i = 0; i < TokenCount; i++)
        {
            TokenFactory.CreateOnBattlefield(spec, caster, zones);
        }
    }
}
