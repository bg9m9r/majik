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
/// Named-card factory for Hurkyl's Recall (Antiquities, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Return all artifacts target player owns to their hand."
///
/// Hurkyl's Recall is the mass-bounce analogue of
/// <see cref="EchoingTruthFactory"/>: it shares the same return-to-hand
/// routine (CR 701.10) routed through <see cref="ZoneService"/> like the
/// bounce cycle, but instead of Echoing Truth's "target permanent + same-name
/// sweep" it sweeps EVERY artifact a TARGET PLAYER owns. The single
/// "target player" request mirrors <see cref="MindRotFactory"/>.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{U}. The base shape
///   (name / Instant type / {1}{U} cost) is materialised from the embedded
///   JSON definition (<c>hurkyls-recall.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="EchoingTruthFactory"/> (the JSON <c>SpellDefinition</c>
///   schema does not yet express a player-target request or the
///   own-all-artifacts sweep, so the resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Target player</b> — <see cref="BuildDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target player"
///   <see cref="TargetRequest"/> (mirrors <see cref="MindRotFactory"/>).
/// - <b>Return all artifacts target player owns</b> — on resolution, after a
///   CR 608.2b legality re-check that the chosen target is still a
///   <see cref="Player"/>, the resolve snapshots every battlefield in the game
///   and returns every artifact <i>owned</i> by the target player. The match
///   is by <b>ownership</b> (CR 109.5 — a card's owner is the player who
///   started the game with it), NOT control: an artifact the target owns but
///   an opponent currently controls is still swept.
/// - On each bounce: the artifact is returned to <b>its owner's</b> hand
///   (CR 701.10 — "return to hand" moves the card to its owner's hand). Since
///   the sweep is ownership-keyed, the owner is always the target player, so
///   every returned artifact lands in the target player's hand ("their hand").
///   When a <see cref="ZoneService"/> is supplied the move is routed through it
///   so replacement effects / zone-change events fire (mirrors
///   <see cref="EchoingTruthFactory"/>); otherwise raw zone manipulation is
///   used.
///
/// ## Rules notes
/// - "All artifacts" includes artifact creatures, artifact lands, etc. — any
///   permanent whose type set includes <see cref="CardType.Artifact"/>
///   (CR 301). It is NOT separately targeted (the spell has a single chosen
///   target — the player), so it ignores shroud / hexproof / protection on the
///   individual artifacts; only the chosen player must be a legal target.
/// - An artifact land returned to hand simply becomes a card in hand; an
///   artifact token returned to hand ceases to exist as a state-based action
///   (CR 111.7) — the zone move still occurs here, then SBAs clean up.
/// </summary>
[CardName("Hurkyl's Recall")]
public static class HurkylsRecallFactory
{
    public const string CardName = "Hurkyl's Recall";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "hurkyls-recall";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{U}) from the
    /// embedded JSON definition. Resolve behaviour (return all artifacts the
    /// target player owns to their hand) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="EchoingTruthFactory"/>.
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
    /// Build the "return all artifacts target player owns to their hand"
    /// <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: the chosen target must still resolve to a
    ///     <see cref="Player"/>, else the spell does nothing.</item>
    ///   <item>CR 109.5 — snapshot every battlefield and collect every artifact
    ///     OWNED by the target player (controller-agnostic).</item>
    ///   <item>CR 701.10 — return each collected artifact to its owner's hand
    ///     (always the target player's hand, since the sweep is
    ///     ownership-keyed).</item>
    /// </list>
    /// </summary>
    /// <param name="allPlayers">All players in the game. The artifact sweep
    /// walks every player's battlefield (an artifact the target owns may be on
    /// an opponent's battlefield). Passed at cast time via
    /// <see cref="ChosenSpellParams.AllPlayers"/>; callers that skip the full
    /// cast flow supply it here directly (the closed-over fallback).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine <see cref="Player"/>. Pass <c>o =&gt; o</c> for tests
    /// that hand players directly.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (mirrors
    /// <see cref="EchoingTruthFactory"/>).</param>
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
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                // Prefer the live AllPlayers snapshot from ChosenSpellParams
                // (populated by SpellCastFlow); fall back to the closed-over
                // list when the caller built the SpellDefinition directly
                // (e.g. tests / bot probes). Same posture as Echoing Truth.
                var players = p.AllPlayers ?? allPlayers;
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: return all artifacts target player owns to their hand",
                        () => Resolve(resolved, players, zoneService)),
                };
            });
    }

    private static void Resolve(
        object resolved,
        IReadOnlyList<Player> allPlayers,
        ZoneService? zoneService)
    {
        // CR 608.2b — resolution-time legality re-check. The chosen target must
        // still resolve to a player, else the spell does nothing.
        if (resolved is not Player target) return;

        // CR 109.5 — collect every artifact OWNED by the target player, across
        // every battlefield (an owned artifact may be controlled by — and on
        // the battlefield of — an opponent). Snapshot first so bouncing one (a
        // zone move) doesn't disturb enumeration (mirrors Echoing Truth).
        var toBounce = allPlayers
            .SelectMany(pl => pl.Zones.Battlefield.GetCards())
            .OfType<Permanent>()
            .Where(perm => perm.HasType(CardType.Artifact)
                           && ReferenceEquals(perm.Owner, target))
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
    /// CR 701.10 — return a single artifact to its owner's hand. When a
    /// <see cref="ZoneService"/> is supplied the move is routed through it so
    /// replacement effects / zone-change events fire; otherwise raw zone
    /// manipulation is used (same posture as <see cref="EchoingTruthFactory"/>).
    /// The owner is always the target player (the sweep is ownership-keyed), so
    /// "to their hand" is satisfied; the artifact is removed from whichever
    /// battlefield it currently sits on (owner's or an opponent's).
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
