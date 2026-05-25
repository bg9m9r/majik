using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karn, the Great Creator (War of the Spark, {4}).
///
/// Legendary Planeswalker — Karn, starting loyalty 5.
/// Oracle text:
///   "Activated abilities of artifacts your opponents control can't be
///    activated.
///    +1: Until your next turn, up to one target noncreature artifact
///         becomes an artifact creature with power and toughness each
///         equal to its mana value.
///    -2: You may reveal an artifact card you own from outside the game
///         or choose a face-up artifact card you own in exile. Put that
///         card into your hand."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 5, Karn subtype, mana cost {4}.
/// - <b>Printed static</b>: registered via
///   <see cref="OpponentArtifactActivatedSuppressionEffect"/> when the
///   runtime (owner, effects, eventBus, …) overload is used. A predicate
///   restriction is pushed into
///   <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/> rejecting
///   any non-mana <see cref="IActivatedAbility"/> whose source is an
///   on-battlefield artifact controlled by an opponent of Karn's
///   controller. CR 605 mana-ability exemption is honoured by the
///   registry itself (and ManaAbilityActivator routes mana abilities
///   around <see cref="Majik.Core.Rules.ActionValidator"/> entirely).
/// - <b>+1</b>: target a permanent (deterministic auto-pick — first
///   noncreature artifact on the battlefield via
///   <paramref name="battlefieldResolver"/>). Registers a Layer 4
///   <see cref="KarnAnimateArtifactEffect"/> adding Creature type, and
///   a Layer 7b <see cref="BecomesPTEffect"/> (or
///   <see cref="KarnAnimatedShimPTEffect"/> when the target is not a
///   <see cref="Creature"/> C# instance) carrying P/T = target's mana
///   value at resolution time (CR 613.7b / 208.1 — once stamped, later
///   cost-modifications don't shift it). Both effects flagged
///   <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>; "until your
///   next turn" is approximated as EOT (see
///   <see cref="KarnAnimateArtifactEffect"/> for the v1 rationale).
/// - <b>-2 wishboard</b>: accepts a
///   <c>Func&lt;Player, ICard?&gt;</c> selector — typically returns an
///   artifact card from the sideboard ("outside the game") or a face-up
///   exiled artifact owned by the controller. The chosen card is moved
///   into the controller's hand. If the selector returns null or no
///   selector is wired, the loyalty change still applies (CR 606.3).
///   <b>Wishboard auto-wiring (PR for `WishTutorEffect`)</b>: when the
///   caller doesn't supply a <c>wishSelector</c>, the -2 falls through
///   to <see cref="WishTutorEffect"/> filtered by
///   <see cref="WishTutorEffect.Predicates.ArtifactCard"/> against
///   <see cref="Player.Wishboard"/>. This means a factory consumer that
///   has populated the controller's sideboard with the deck's wish-pool
///   gets the -2 wish for free without supplying an explicit selector.
///   The exile-zone reach (face-up artifact in exile owned by the
///   controller) still requires the explicit selector — wishboard
///   auto-wiring only covers the sideboard half.
///
/// ## Deferred (v1 gaps)
/// - <b>"Until your next turn" precise duration</b>: collapsed to
///   end-of-turn because no controller-keyed duration primitive exists.
///   Observationally equivalent for combat math on Karn's turn; differs
///   on opposing turns immediately following Karn's, where the printed
///   ability would still hold until Karn's NEXT untap step.
/// - <b>Targeting prompt</b>: <see cref="LoyaltyAbility"/> does not yet
///   declare <see cref="Majik.Core.Targeting.TargetRequest"/>s. +1
///   auto-picks the first matching artifact deterministically.
/// - <b>Non-Creature animate combat math</b>: see
///   <see cref="KarnAnimateArtifactEffect"/> — the layer system needs
///   to upgrade <see cref="PermanentCharacteristics"/> to
///   <see cref="CreatureCharacteristics"/> when Layer 4 grants Creature
///   type. Until then, the BecomesPTEffect / shim is registered for
///   layer-correctness but its P/T values aren't surfaced for non-
///   Creature targets at Compute time.
/// - <b>True wishboard exile reach</b>: the simplified
///   <c>Func&lt;Player, ICard?&gt;</c> selector covers both "outside
///   the game" and "face-up artifact in exile" via the caller's
///   choosing logic. No exile-zone scan is built in — tests / bots
///   supply the resolved card directly.
/// </summary>
[CardName("Karn, the Great Creator")]
public static class KarnTheGreatCreatorFactory
{
    public const string CardName = "Karn, the Great Creator";
    public const string Cost = "{4}";

    /// <summary>
    /// Construct a Karn with no live wiring. The printed static is not
    /// registered (no event bus / continuous-effects service). The +1
    /// and -2 ability bodies are still attached so the loyalty changes
    /// apply (CR 606.3); the +1 effect-registration step is gated on
    /// non-null <c>effects</c>, so it no-ops here.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, battlefieldResolver: null, wishSelector: null);

    /// <summary>
    /// Construct a fully-wired Karn.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 / 7b
    /// registration of the +1 animate-artifact effect. May be null —
    /// the +1 still picks a target and decrements/increments loyalty
    /// but no continuous effect is registered.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking of the
    /// printed static. May be null — the lifecycle still syncs once on
    /// <see cref="OpponentArtifactActivatedSuppressionEffect.Attach"/>.</param>
    /// <param name="battlefieldResolver">Source for the +1's target pool.
    /// Returns the live battlefield snapshot at activation time. May be
    /// null — the +1 no-ops (legal — "up to one target").</param>
    /// <param name="wishSelector">Resolves the -2's chosen artifact card
    /// (outside the game OR face-up artifact in exile owned by the
    /// controller). May be null — the -2 no-ops while loyalty still
    /// decrements per CR 606.3.</param>
    public static Planeswalker Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IReadOnlyList<Permanent>>? battlefieldResolver,
        Func<Player, ICard?>? wishSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var karn = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Karn });

        karn.SetOwner(owner);
        karn.SetController(owner);

        // -- Printed static: opponent-artifact activated suppression -----
        if (eventBus != null)
        {
            var lifecycle = new OpponentArtifactActivatedSuppressionEffect(
                source: karn,
                controller: owner,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        // -- +1 ability: animate target noncreature artifact ------------
        karn.AddAbility(new LoyaltyAbility(karn, +1, () =>
        {
            var target = PickAnimateTarget(battlefieldResolver);
            if (target == null) return; // "up to one" — empty is legal
            if (effects == null) return; // no service wired — shape-only path

            var mv = target.ManaCostValue.TotalValue;

            // Layer 4 type-add — chars.Types gains Creature for the duration.
            effects.Register(new KarnAnimateArtifactEffect(target));

            // Layer 7b base-P/T set — applies fully when target is a
            // Creature C# instance; otherwise the effect is registered
            // for inspection / layer-correctness, see
            // KarnAnimateArtifactEffect xmldoc.
            if (target is Creature targetCreature)
            {
                effects.Register(new BecomesPTEffect(targetCreature, mv, mv));
            }
            else
            {
                effects.Register(new KarnAnimatedShimPTEffect(target, mv, mv));
            }
        }));

        // -- -2 ability: wishboard fetch --------------------------------
        // CR 606.3 — loyalty cost is paid regardless of whether the body
        // finds a target. Two paths:
        //   1. Caller supplied an explicit <c>wishSelector</c> — preserves
        //      pre-PR test posture, with extra reach into the face-up
        //      exile artifact pool the simplified selector model covers.
        //   2. No selector — fall through to the new
        //      <see cref="WishTutorEffect"/> primitive filtered to
        //      artifact cards in <see cref="Player.Wishboard"/>. Lets
        //      callers that populate the sideboard get the wishboard
        //      half of the printed ability "for free" (CR 408).
        karn.AddAbility(new LoyaltyAbility(karn, -2, () =>
        {
            if (wishSelector != null)
            {
                var chosen = wishSelector(owner);
                if (chosen == null) return;

                // Source-side removal: if the chosen card was a face-up
                // artifact in the controller's exile, pull it out of exile
                // first (CR 406.3). "Outside the game" cards have no engine-
                // tracked zone — they appear in tests / bots as raw
                // <see cref="ICard"/> instances and we just route them into
                // the controller's hand.
                if (chosen is Card card && card.Zone == ZoneType.Exile)
                {
                    owner.Zones.Exile.RemoveCard(card);
                }

                owner.Zones.Hand.AddCard(chosen);
                if (chosen is Card cc)
                {
                    cc.SetZone(ZoneType.Hand);
                }
                return;
            }

            // No explicit selector → wishboard tutor primitive (CR 408).
            // Predicate gates on Artifact (CR 301.1); the printed text's
            // "you own" clause is implicit — the wishboard is by definition
            // the controller's own outside-the-game pool.
            new WishTutorEffect(
                predicate: WishTutorEffect.Predicates.ArtifactCard,
                pileLabel: "an artifact card you own from outside the game")
                .Resolve(owner);
        }));

        return karn;
    }

    private static Permanent? PickAnimateTarget(
        Func<IReadOnlyList<Permanent>>? resolver)
    {
        if (resolver == null) return null;
        var board = resolver.Invoke();
        if (board == null) return null;
        foreach (var perm in board)
        {
            // "noncreature artifact" — CR 109.1, 301.1.
            if (!perm.HasType(CardType.Artifact)) continue;
            if (perm.HasType(CardType.Creature)) continue;
            return perm;
        }
        return null;
    }
}

/// <summary>
/// Shim recording the P/T intent of Karn's +1 when the target is not a
/// <see cref="Creature"/> C# instance. The effect itself doesn't apply
/// in the current layer-system pipeline (<see cref="ContinuousEffectsService.Compute(Permanent)"/>
/// builds a <see cref="PermanentCharacteristics"/> for non-Creature
/// permanents, with no P/T fields), but tests / future Compute upgrades
/// can inspect <see cref="NewPower"/> and <see cref="NewToughness"/> to
/// surface the intended values.
/// </summary>
internal sealed class KarnAnimatedShimPTEffect : ContinuousEffect
{
    private readonly Permanent _target;
    public int NewPower { get; }
    public int NewToughness { get; }

    public KarnAnimatedShimPTEffect(Permanent target, int power, int toughness)
    {
        _target = target;
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;
    public override Permanent? Source => _target;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);
    public override bool IsActive() => _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 7b on a non-creature row is observationally a no-op in
        // the current pipeline. See class xmldoc.
    }
}
