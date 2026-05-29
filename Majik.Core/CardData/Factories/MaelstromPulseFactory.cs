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
/// Named-card factory for Maelstrom Pulse (Alara Reborn, {1}{B}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Destroy target nonland permanent and all other permanents with the
///    same name as that permanent."
///
/// Maelstrom Pulse is the {1}{B}{G} sorcery extension of the
/// "destroy target nonland permanent" pattern (cf.
/// <see cref="AnguishedUnmakingFactory"/>, which exiles a nonland permanent):
/// it destroys the chosen target AND every <i>other</i> permanent that shares
/// the target's name, across every battlefield.
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {1}{B}{G}. The base shape
///   (name / Sorcery type / {1}{B}{G} cost) is materialised from the
///   embedded JSON definition (<c>maelstrom-pulse.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="DreadboreFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not yet express a nonland-permanent target request or the
///   same-name sweep, so the resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Destroy target nonland permanent</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target nonland permanent"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents whose card-type set
///   does NOT include <see cref="CardType.Land"/> (CR 305 — Land is a card
///   type, so the filter rejects e.g. Dryad Arbor too).
/// - <b>...and all other permanents with the same name</b> — on resolution,
///   after re-checking the target is still a nonland permanent on the
///   battlefield (CR 608.2b), the resolve snapshots every battlefield in the
///   game and destroys every permanent (target included) whose
///   <see cref="ICard.Name"/> equals the target's name. The match is by
///   <i>name</i>, not card identity, and is controller-agnostic
///   (CR 201.2 — objects with the same English name) — the caster's own
///   same-name permanents are swept too.
/// - On each destroy: routed through
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7)
///   so indestructible (CR 702.12) and regeneration shields (CR 701.15) are
///   honoured per-permanent at the destroy site.
///
/// ## Rules notes
/// - The same-name sweep is NOT separately targeted (CR 701.7 — "destroy"
///   does not require targeting beyond the one chosen target), so it ignores
///   shroud / hexproof / protection on the collateral permanents; only the
///   single chosen target must be a legal target.
/// - Tokens carry a name; a token sharing the printed card's name is swept.
/// </summary>
[CardName("Maelstrom Pulse")]
public static class MaelstromPulseFactory
{
    public const string CardName = "Maelstrom Pulse";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "maelstrom-pulse";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {1}{B}{G}) from
    /// the embedded JSON definition. Resolve behaviour (destroy target
    /// nonland permanent + all same-name permanents) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="DreadboreFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "destroy target nonland permanent and all other permanents
    /// with the same name as that permanent" <see cref="SpellDefinition"/>.
    /// On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: the target must still be a nonland
    ///     <see cref="Permanent"/> on the Battlefield, else the spell does
    ///     nothing.</item>
    ///   <item>CR 201.2 — snapshot every battlefield and collect every
    ///     permanent (target included) whose name equals the target's name,
    ///     controller-agnostic.</item>
    ///   <item>CR 701.7 — destroy each collected permanent via
    ///     <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///     <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> so
    ///     indestructible / regeneration are honoured per-permanent.</item>
    /// </list>
    /// </summary>
    /// <param name="allPlayers">All players in the game. The same-name sweep
    /// walks every player's battlefield. Passed at cast time via
    /// <see cref="ChosenSpellParams.AllPlayers"/>; callers that skip the full
    /// cast flow supply it here directly (the closed-over fallback).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<Player> allPlayers,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every nonland permanent
                    // on any battlefield (CR 305 — Land is a card type).
                    // Removal intent in the bot's ranker pushes opponent
                    // permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => !c.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                // Prefer the live AllPlayers snapshot from ChosenSpellParams
                // (populated by SpellCastFlow); fall back to the closed-over
                // list when the caller built the SpellDefinition directly
                // (e.g. tests / bot probes). Same posture as CowerInFear.
                var players = p.AllPlayers ?? allPlayers;
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target nonland permanent and all "
                        + "other permanents with the same name",
                        () => Resolve(resolved, players)),
                };
            });
    }

    private static void Resolve(object resolved, IReadOnlyList<Player> allPlayers)
    {
        // CR 608.2b — resolution-time legality re-check. The chosen target
        // must still be a nonland permanent on the battlefield, else the
        // entire spell does nothing (no same-name sweep without a legal
        // target).
        if (resolved is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.HasType(CardType.Land)) return;

        var targetName = target.Name;

        // CR 201.2 — collect every permanent (target included) whose name
        // matches, across every battlefield, controller-agnostic. Snapshot
        // first so destroying one (a zone move) doesn't disturb enumeration
        // (mirrors the Pyroclasm / Cower in Fear snapshot pattern).
        var toDestroy = allPlayers
            .SelectMany(pl => pl.Zones.Battlefield.GetCards())
            .OfType<Permanent>()
            .Where(perm => string.Equals(perm.Name, targetName, StringComparison.Ordinal))
            .ToList();

        foreach (var perm in toDestroy)
        {
            // CR 608.2b — guard against a same-step move having already
            // pulled this permanent off the battlefield.
            if (perm.Zone != ZoneType.Battlefield) continue;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
            // regeneration (CR 701.15) are honoured per-permanent via the
            // Destroy-reason gate in MoveToGraveyard.
            OracleSpellBinder.MoveToGraveyard(
                perm,
                Majik.Core.Zones.ZoneMoveReason.Destroy);
        }
    }
}
