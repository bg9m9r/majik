using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tezzeret, Agent of Bolas (Mirrodin Besieged,
/// {2}{U}{B}).
///
/// Legendary Planeswalker — Tezzeret, starting loyalty 3.
/// Oracle text (Scryfall, verified):
///   "+1: Look at the top five cards of your library. You may reveal an
///        artifact card from among them and put it into your hand. Put the
///        rest on the bottom of your library in any order.
///    −1: Target artifact becomes an artifact creature with base power and
///        toughness 5/5.
///    −4: Target player loses X life and you gain X life, where X is twice
///        the number of artifacts you control."
///
/// ## Shape source
/// Card identity (name, Legendary Planeswalker — Tezzeret, {2}{U}{B},
/// loyalty 3) is loaded from
/// <c>Majik.Core/CardData/Cards/tezzeret-agent-of-bolas.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (same posture as
/// <see cref="NahiriTheHarbingerFactory"/>). The JSON carries no abilities —
/// the three loyalty abilities are layered on below.
///
/// ## Implemented (v1)
/// - Legendary Planeswalker — Tezzeret at {2}{U}{B}, starting loyalty 3
///   (CR 306.1 / CR 205.3m — Tezzeret planeswalker subtype).
/// - <b>+1: dig 5, reveal an artifact → hand, rest → bottom (CR 606 +
///   CR 701.18 reveal + CR 701.20 zone move)</b>: peeks the top five cards
///   of the controller's library, sends the FIRST artifact card found to
///   hand (the "you may reveal" is auto-accepted when an artifact is present —
///   the heuristic default; agent-driven choice is the same gap the other
///   planeswalkers have), then puts the remaining looked-at cards on the
///   bottom of the library in their looked-at order ("in any order" — v1
///   keeps the stable order, CR 701.20). With no artifact among the five the
///   whole batch goes to the bottom.
/// - <b>-1: target artifact becomes an artifact creature with base P/T 5/5
///   (CR 606 + CR 613)</b>: when <paramref name="targetArtifactResolver"/>
///   resolves a battlefield artifact AND an
///   <see cref="ContinuousEffectsService"/> is supplied, registers a pair of
///   target-captured continuous effects against that service:
///     - <see cref="TezzeretAnimateArtifactEffect"/> — Layer 4 (CR 613.1c):
///       adds <see cref="CardType.Creature"/> on top of the artifact's
///       printed types ("becomes an artifact creature" — the Artifact type is
///       preserved). The Layer-4 Creature grant drives
///       <see cref="ContinuousEffectsService"/>'s creature-row upgrade so the
///       artifact gets a P/T row for the set-base below.
///     - <see cref="TezzeretSetBasePTEffect"/> — Layer 7b (CR 613.7b): sets
///       the artifact's base power/toughness to 5/5.
///   The target is wired to consult the service (<c>ActiveEffects</c>) so
///   GetPower / GetToughness surface the 5/5. Both effects persist (no
///   end-of-turn expiry — this is a permanent characteristic-defining change,
///   not a "until end of turn" pump). Mirrors the Layer-4 + Layer-7b pairing
///   in <see cref="TezzeretsTouchFactory"/> (aura-attached there;
///   target-captured here).
/// - <b>-4: target player loses X, you gain X, X = 2× artifacts you control
///   (CR 606 + CR 119.3)</b>: when <paramref name="targetPlayerResolver"/>
///   resolves a player, computes X = 2 × (artifacts the controller controls)
///   at resolution, then <see cref="Fx.LoseLife"/> on the target and
///   <see cref="Fx.GainLife"/> on the controller (CR 118.3 — two separate
///   life changes, not a transfer).
///
/// ## Implemented (v1) — loyalty target prompts
/// - <b>-1 / -4 declare real <see cref="TargetRequest"/>s</b> (CR 602.2b):
///   the -1 (target artifact) and -4 (target player) declare a TargetRequest
///   with a live <c>CandidateGatherer</c> so the loyalty dispatch path
///   (<c>TurnDriver.DispatchLoyalty</c> → <c>CandidateGatherer</c> →
///   <c>agent.ChooseTargetsAsync</c> → <c>SetChosenTargets</c>) prompts the
///   activating player's agent. Each body reads the CHOSEN target off the
///   <see cref="ResolutionContext"/> (<c>rc.ChosenTargets[0][0]</c>) with a
///   CR 608.2b legality re-check, falling back to the captured resolver only
///   on the legacy direct-activation path (the captured resolver was null on
///   the routed prod build — the resolver-null bug class).
///
/// ## Deferred (v1 gaps)
/// - <b>+1 "you may reveal"</b>: the +1 auto-accepts the optional reveal and
///   sends the first artifact to hand. The +1 is NON-targeted (it digs the
///   controller's own library), so agent-driven reveal choice is a separate
///   gap from the target-prompt wiring.
/// - <b>+1 reveal visibility</b>: the "reveal an artifact" step has no visible
///   reveal event (the engine doesn't yet model hidden-info reveals) — same
///   posture as Jace's +2 peek.
/// </summary>
[CardName("Tezzeret, Agent of Bolas")]
public static class TezzeretAgentOfBolasFactory
{
    public const string CardName = "Tezzeret, Agent of Bolas";
    public const string Slug = "tezzeret-agent-of-bolas";
    public const int StartingLoyalty = 3;

    public const int Plus1Loyalty = +1;
    public const int Minus1Loyalty = -1;
    public const int Minus4Loyalty = -4;

    /// <summary>CR 606 — how many cards the +1 looks at.</summary>
    public const int Plus1LookCount = 5;

    /// <summary>CR 613.7b — base power the -1 artifact becomes.</summary>
    public const int AnimateBasePower = 5;

    /// <summary>CR 613.7b — base toughness the -1 artifact becomes.</summary>
    public const int AnimateBaseToughness = 5;

    /// <summary>CR 606 — the -4 multiplier (X = twice the artifact count).</summary>
    public const int Minus4ArtifactMultiplier = 2;

    /// <summary>
    /// Construct Tezzeret with no resolvers / effects wired — +1 still runs
    /// (own-library dig is controller-scoped); -1 / -4 no-op while the
    /// loyalty change still applies (CR 606.3). Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetArtifactResolver: null, targetPlayerResolver: null, effects: null);

    /// <summary>
    /// Construct Tezzeret, Agent of Bolas.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetArtifactResolver">Returns candidate artifacts for
    /// the -1 animate. v1 picks the first battlefield artifact. May be null —
    /// the -1 clause no-ops.</param>
    /// <param name="targetPlayerResolver">Returns candidate players for the -4
    /// life-drain. v1 picks the first. May be null — the -4 clause no-ops.</param>
    /// <param name="effects">Continuous-effects service the -1 animate
    /// registers against (Layer 4 add-Creature + Layer 7b set-base 5/5). May
    /// be null — the -1 clause no-ops (the loyalty change still applies).</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Permanent>>? targetArtifactResolver,
        Func<IReadOnlyList<Player>>? targetPlayerResolver,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Tezzeret, {2}{U}{B}, loyalty 3). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var tezzeret = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1: Look at the top five cards of your library. You may reveal an
        //    artifact card from among them and put it into your hand. Put the
        //    rest on the bottom of your library in any order. ----------------
        // CR 606 (loyalty) + CR 701.18 (reveal) + CR 701.20 (zone move).
        // v1: dig 5, send the first artifact card to hand (auto-accept the
        // optional reveal when an artifact is present), then bottom the rest in
        // looked-at order ("in any order" — stable v1 order).
        tezzeret.AddAbility(new LoyaltyAbility(tezzeret, Plus1Loyalty, () =>
        {
            var controller = tezzeret.Controller ?? owner;
            var looked = controller.Zones.Library.GetCards()
                .Take(Plus1LookCount)
                .ToList();
            if (looked.Count == 0) return;

            // First artifact card among the looked-at five → hand. CR 110.1 /
            // CR 301.1 — an "artifact card" is a card whose types include
            // Artifact.
            var artifact = looked.FirstOrDefault(IsArtifactCard);
            if (artifact != null)
            {
                controller.Zones.Library.RemoveCard(artifact);
                controller.Zones.Hand.AddCard(artifact);
                artifact.SetZone(ZoneType.Hand);
            }

            // "Put the rest on the bottom of your library in any order."
            // Library is top-at-index-0; Zone.AddCard appends → bottom. Remove
            // each remaining looked-at card from the top, re-add to the bottom
            // in looked-at order (the artifact, if any, is already gone).
            foreach (var card in looked)
            {
                if (ReferenceEquals(card, artifact)) continue;
                controller.Zones.Library.RemoveCard(card);
                controller.Zones.Library.AddCard(card);
                card.SetZone(ZoneType.Library);
            }
        }));

        // -- -1: Target artifact becomes an artifact creature with base power
        //    and toughness 5/5. ---------------------------------------------
        // CR 606 (loyalty) + CR 115 (target artifact) + CR 613 (continuous).
        // Layer 4 adds Creature (CR 613.1c — additive, the Artifact type is
        // preserved); the Compute creature-row upgrade then provides a P/T row
        // that the Layer-7b set-base lands on (CR 613.7b). The target artifact
        // is chosen by the activating player's agent via a TargetRequest (any
        // battlefield artifact — "target artifact"); the body reads the CHOSEN
        // artifact off the ResolutionContext (slot 0) with a CR 608.2b legality
        // re-check, falling back to the captured targetArtifactResolver only on
        // the legacy direct-activation path.
        var animateRequest = new TargetRequest(
            Description: "Target artifact becomes a 5/5 artifact creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Permanent>()
                .Where(c => c.HasType(CardType.Artifact))
                .Cast<object>()
                .ToList());

        tezzeret.AddAbility(new LoyaltyAbility(
            tezzeret,
            Minus1Loyalty,
            new[]
            {
                Fx.Inline("Target artifact becomes a 5/5 artifact creature", rc =>
                {
                    if (effects == null) return default;
                    var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Permanent
                        : null)
                        ?? targetArtifactResolver?.Invoke()?.FirstOrDefault(p =>
                            p != null
                            && p.Zone == ZoneType.Battlefield
                            && p.HasType(CardType.Artifact));
                    // CR 608.2b — re-check the target's legality on resolution.
                    if (target == null) return default;
                    if (target.Zone != ZoneType.Battlefield) return default;
                    if (!target.HasType(CardType.Artifact)) return default;

                    // Wire the target to consult the layer system so the Layer-4
                    // Creature grant + Layer-7b 5/5 surface at GetPower /
                    // GetToughness.
                    target.ActiveEffects = effects;
                    effects.Register(new TezzeretAnimateArtifactEffect(target));
                    effects.Register(new TezzeretSetBasePTEffect(
                        target, AnimateBasePower, AnimateBaseToughness));
                    return default;
                }),
            },
            targetRequests: new[] { animateRequest }));

        // -- -4: Target player loses X life and you gain X life, where X is
        //    twice the number of artifacts you control. ---------------------
        // CR 606 (loyalty) + CR 115 (target player) + CR 119.3 (life change).
        // X computed at resolution (CR 608.2). Two separate life changes
        // (CR 118.3 — not a transfer): the target loses, the controller gains.
        // The target player is chosen by the activating player's agent via a
        // TargetRequest (any player — "target player"); the body reads the
        // CHOSEN player off the ResolutionContext (slot 0), falling back to the
        // captured targetPlayerResolver only on the legacy direct-activation
        // path.
        var drainRequest = new TargetRequest(
            Description: "Target player loses X life (X = twice your artifacts)",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Burn,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers.Cast<object>().ToList());

        tezzeret.AddAbility(new LoyaltyAbility(
            tezzeret,
            Minus4Loyalty,
            new[]
            {
                Fx.Inline("Target player loses X; you gain X (X = twice your artifacts)", rc =>
                {
                    var controller = rc.Controller ?? tezzeret.Controller ?? owner;
                    var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Player
                        : null)
                        ?? targetPlayerResolver?.Invoke()?.FirstOrDefault();
                    if (target == null) return default;

                    var x = Minus4ArtifactMultiplier * ArtifactCount(controller);
                    if (x <= 0) return default;
                    Fx.LoseLife(target, x);
                    Fx.GainLife(controller, x);
                    return default;
                }),
            },
            targetRequests: new[] { drainRequest }));

        return tezzeret;
    }

    /// <summary>CR 110.1 / CR 301.1 — an "artifact card" is a card whose
    /// types include Artifact. Cards carry a static type-line (no continuous
    /// effects apply in the library), so a printed-type check is correct
    /// here.</summary>
    private static bool IsArtifactCard(ICard card) =>
        card is Card c && c.HasType(CardType.Artifact);

    /// <summary>Count the artifacts <paramref name="controller"/> controls
    /// (CR 109.1 / 301.1). Includes artifact creatures and artifact
    /// lands.</summary>
    private static int ArtifactCount(Player controller)
    {
        var count = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is Permanent perm && perm.HasType(CardType.Artifact))
            {
                count++;
            }
        }
        return count;
    }
}

/// <summary>
/// CR 613.1c — Layer 4 type-adding effect for Tezzeret's -1. While the target
/// artifact is on the battlefield, it gains <see cref="CardType.Creature"/> in
/// addition to its other types ("becomes an artifact creature" — the Artifact
/// type is preserved). Target-captured (reads a fixed <c>_target</c>, unlike
/// <see cref="AuraAnimateArtifactEffect"/>'s attachment-based variant). Does
/// NOT expire at end of turn — the -1 is a permanent characteristic change.
/// The Layer-4 Creature grant drives <see cref="ContinuousEffectsService"/>'s
/// creature-row upgrade so <see cref="TezzeretSetBasePTEffect"/> has a P/T row
/// to land on.
/// </summary>
public sealed class TezzeretAnimateArtifactEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public TezzeretAnimateArtifactEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The artifact being animated.</summary>
    public Permanent Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() => _target.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 613.1c — additive: Creature added on top of the printed Artifact
        // (and any other) type.
        chars.Types.Add(CardType.Creature);
    }
}

/// <summary>
/// CR 613.7b — Layer 7b set-base-P/T effect for Tezzeret's -1. While the target
/// artifact is on the battlefield, its base power and toughness become the
/// supplied values (5/5). Target-captured. Does NOT expire at end of turn.
/// Overrides <see cref="AppliesTo(Permanent)"/> so the effect is selected
/// during the pre-upgrade <c>applicable</c> filter (the artifact is not yet a
/// creature row at that point), mirroring <see cref="AuraSetBasePTEffect"/>.
/// </summary>
public sealed class TezzeretSetBasePTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>CR 613.7b — base power the artifact becomes.</summary>
    public int NewPower { get; }

    /// <summary>CR 613.7b — base toughness the artifact becomes.</summary>
    public int NewToughness { get; }

    public TezzeretSetBasePTEffect(Permanent target, int power, int toughness)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _target;

    public override bool IsActive() => _target.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="TezzeretSetBasePTEffect"/> bound to
    /// <paramref name="clonedSource"/> for the search-sandbox clone.
    /// preserves: NewPower, NewToughness; target → clonedSource.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new TezzeretSetBasePTEffect(clonedSource, NewPower, NewToughness);
}
