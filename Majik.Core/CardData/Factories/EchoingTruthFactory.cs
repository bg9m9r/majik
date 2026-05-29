using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Echoing Truth (Ravnica: City of Guilds, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Return target nonland permanent and all other permanents with the
///    same name as that permanent to their owners' hands."
///
/// Echoing Truth is the bounce analogue of <see cref="MaelstromPulseFactory"/>
/// (which <i>destroys</i> a nonland permanent + all same-name permanents): it
/// shares the same "target nonland permanent + same-name sweep" shape, but the
/// per-permanent effect is a return-to-owner's-hand bounce (cf.
/// <see cref="BoomerangFactory"/>, the single-target broad bounce) rather than
/// a destroy.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{U}. The base shape
///   (name / Instant type / {1}{U} cost) is materialised from the embedded
///   JSON definition (<c>echoing-truth.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="MaelstromPulseFactory"/> (the JSON <c>SpellDefinition</c>
///   schema does not yet express a nonland-permanent target request or the
///   same-name sweep, so the resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Return target nonland permanent</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1 "target
///   nonland permanent" <see cref="TargetRequest"/>. The live
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents whose card-type set does NOT include
///   <see cref="CardType.Land"/> (CR 305 — Land is a card type).
/// - <b>...and all other permanents with the same name</b> — on resolution,
///   after re-checking the target is still a nonland permanent on the
///   battlefield (CR 608.2b), the resolve snapshots every battlefield in the
///   game and returns every permanent (target included) whose
///   <see cref="ICard.Name"/> equals the target's name. The match is by
///   <i>name</i>, not card identity, and is controller-agnostic
///   (CR 201.2 — objects with the same English name) — the caster's own
///   same-name permanents are bounced too.
/// - On each bounce: each permanent is returned to <b>its owner's</b> hand
///   (CR 701.10 — "return to hand" moves the card to its owner's hand). When
///   a <see cref="ZoneService"/> is supplied the move is routed through it so
///   replacement effects / zone-change events fire (mirrors
///   <see cref="BoomerangFactory"/>); otherwise raw zone manipulation is used.
///
/// ## Rules notes
/// - The same-name sweep is NOT separately targeted (the spell has a single
///   chosen target), so it ignores shroud / hexproof / protection on the
///   collateral permanents; only the single chosen target must be a legal
///   target.
/// - Tokens carry a name; a token sharing the printed card's name is swept,
///   and (per CR 111.7) a token returned to hand ceases to exist as a
///   state-based action — the zone move still occurs here, then SBAs clean up.
/// </summary>
[CardName("Echoing Truth")]
public static class EchoingTruthFactory
{
    public const string CardName = "Echoing Truth";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "echoing-truth";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{U}) from the
    /// embedded JSON definition. Resolve behaviour (return target nonland
    /// permanent + all same-name permanents to their owners' hands) is built
    /// on demand via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="MaelstromPulseFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "return target nonland permanent and all other permanents
    /// with the same name as that permanent to their owners' hands"
    /// <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: the target must still be a nonland
    ///     <see cref="Permanent"/> on the Battlefield, else the spell does
    ///     nothing.</item>
    ///   <item>CR 201.2 — snapshot every battlefield and collect every
    ///     permanent (target included) whose name equals the target's name,
    ///     controller-agnostic.</item>
    ///   <item>CR 701.10 — return each collected permanent to <i>its
    ///     owner's</i> hand.</item>
    /// </list>
    /// </summary>
    /// <param name="allPlayers">All players in the game. The same-name sweep
    /// walks every player's battlefield. Passed at cast time via
    /// <see cref="ChosenSpellParams.AllPlayers"/>; callers that skip the full
    /// cast flow supply it here directly (the closed-over fallback).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (mirrors
    /// <see cref="BoomerangFactory"/>).</param>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<Player> allPlayers,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
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
                    Intent: BotIntent.Bounce,
                    // Agent-prompt MVP: live gather every nonland permanent on
                    // any battlefield (CR 305 — Land is a card type). Bounce
                    // intent in the bot's ranker pushes opponent permanents up.
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
                // (e.g. tests / bot probes). Same posture as MaelstromPulse.
                var players = p.AllPlayers ?? allPlayers;
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: return target nonland permanent and all "
                        + "other permanents with the same name to their owners' hands",
                        () => Resolve(resolved, players, zoneService)),
                };
            });
    }

    private static void Resolve(
        object resolved,
        IReadOnlyList<Player> allPlayers,
        ZoneService? zoneService)
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
        // first so bouncing one (a zone move) doesn't disturb enumeration
        // (mirrors the Maelstrom Pulse snapshot pattern).
        var toBounce = allPlayers
            .SelectMany(pl => pl.Zones.Battlefield.GetCards())
            .OfType<Permanent>()
            .Where(perm => string.Equals(perm.Name, targetName, StringComparison.Ordinal))
            .ToList();

        foreach (var perm in toBounce)
        {
            // CR 608.2b — guard against a same-step move having already pulled
            // this permanent off the battlefield.
            if (perm.Zone != ZoneType.Battlefield) continue;

            ReturnToOwnersHand(perm, zoneService);
        }
    }

    /// <summary>
    /// CR 701.10 — return a single permanent to its owner's hand. When a
    /// <see cref="ZoneService"/> is supplied the move is routed through it so
    /// replacement effects / zone-change events fire; otherwise raw zone
    /// manipulation is used (same posture as <see cref="BoomerangFactory"/>).
    /// </summary>
    private static void ReturnToOwnersHand(Permanent perm, ZoneService? zoneService)
    {
        var owner = perm.Owner;
        if (owner == null) return;

        var controller = perm.Controller ?? owner;

        if (zoneService != null)
        {
            zoneService.MoveCard(perm, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(perm);
            owner.Zones.Hand.AddCard(perm);
            perm.SetZone(ZoneType.Hand);
            perm.SetController(owner);
        }
    }
}
