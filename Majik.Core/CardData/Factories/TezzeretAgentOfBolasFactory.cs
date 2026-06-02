using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
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
/// ## Deferred (v1 gaps)
/// - <b>Loyalty target prompts</b>: <see cref="LoyaltyAbility"/> doesn't
///   declare <see cref="Majik.Core.Targeting.TargetRequest"/>s. -1 / -4 pick
///   from the supplied resolvers deterministically; the +1 "you may reveal"
///   auto-accepts. Agent-driven choice is the same gap Karn / Jace / Nahiri
///   have.
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
        // CR 606 (loyalty) + CR 613 (continuous). Layer 4 adds Creature
        // (CR 613.1c — additive, the Artifact type is preserved); the Compute
        // creature-row upgrade then provides a P/T row that the Layer-7b
        // set-base lands on (CR 613.7b). v1 picks the first battlefield
        // artifact from the resolver.
        tezzeret.AddAbility(new LoyaltyAbility(tezzeret, Minus1Loyalty, () =>
        {
            if (effects == null) return;
            var candidates = targetArtifactResolver?.Invoke();
            if (candidates == null) return;
            var target = candidates.FirstOrDefault(p =>
                p != null
                && p.Zone == ZoneType.Battlefield
                && p.HasType(CardType.Artifact));
            if (target == null) return;

            // Wire the target to consult the layer system so the Layer-4
            // Creature grant + Layer-7b 5/5 surface at GetPower / GetToughness.
            target.ActiveEffects = effects;
            effects.Register(new TezzeretAnimateArtifactEffect(target));
            effects.Register(new TezzeretSetBasePTEffect(
                target, AnimateBasePower, AnimateBaseToughness));
        }));

        // -- -4: Target player loses X life and you gain X life, where X is
        //    twice the number of artifacts you control. ---------------------
        // CR 606 (loyalty) + CR 119.3 (life change). X computed at resolution
        // (CR 608.2). Two separate life changes (CR 118.3 — not a transfer):
        // the target loses, the controller gains.
        tezzeret.AddAbility(new LoyaltyAbility(tezzeret, Minus4Loyalty, () =>
        {
            var controller = tezzeret.Controller ?? owner;
            var targets = targetPlayerResolver?.Invoke();
            if (targets == null) return;
            var target = targets.FirstOrDefault();
            if (target == null) return;

            var x = Minus4ArtifactMultiplier * ArtifactCount(controller);
            if (x <= 0) return;
            Fx.LoseLife(target, x);
            Fx.GainLife(controller, x);
        }));

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
}
