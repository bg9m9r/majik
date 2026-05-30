using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Whirler Rogue (Kaladesh, {2}{U}{U}).
///
/// Creature — Human Rogue Artificer 2/2. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, create two 1/1 colorless Thopter artifact
///    creature tokens with flying.
///    Tap two untapped artifacts you control: Target creature can't be
///    blocked this turn."
///
/// Modern Affinity / artifact-aggro evasion enabler — a 2/2 body that
/// immediately doubles as two flyers AND turns any creature unblockable by
/// tapping a pair of artifacts (the Thopters it just made qualify). Pairs
/// with the same Thopter token shape printed by
/// <see cref="WhirlerVirtuosoFactory"/>.
///
/// ## Build path
///
/// Identity (2/2 Human Rogue Artificer, {2}{U}{U}) is authored in the
/// embedded JSON definition (<c>Majik.Core/CardData/Cards/whirler-rogue.json</c>)
/// and materialized through <see cref="CardDefinitionFactory"/> — the same
/// vanilla-creature shape used across the JSON-backed factories. The ETB
/// "create two Thopters" trigger and the targeted "tap two artifacts:
/// unblockable" activated ability are hand-attached on top, because the
/// data-driven <see cref="CardDefinitionFactory"/> does not yet express
/// token-minting triggers or targeted combat-restriction grants (mirrors
/// <see cref="RoguesPassageFactory"/> for the unblockable shape and
/// <see cref="WhirlerVirtuosoFactory"/> for the Thopter token shape).
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> — Human Rogue Artificer, mana cost
///   {2}{U}{U}. NOTE: Whirler Rogue is a plain blue creature, NOT an
///   artifact — so it cannot pay its own "tap two artifacts" cost (contrast
///   the Thopters it mints, which ARE artifact creatures).
/// - <b>ETB triggered ability</b> (CR 603.6a): "When this creature enters,
///   create two 1/1 colorless Thopter artifact creature tokens with
///   flying." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>; on
///   resolution the factory mints TWO 1/1 colourless
///   <see cref="CardSubtype.Thopter"/> creature tokens with Flying (CR
///   702.9) through <see cref="TokenFactory.CreateOnBattlefield"/>, then
///   additively stamps <see cref="CardType.Artifact"/> on each (CR 111.1 —
///   Thopter tokens are artifact creatures; the Token shell is Creature-only,
///   same multi-type stamp as Whirler Virtuoso / Animation Module's Servo).
/// - <b>"Tap two untapped artifacts you control" activated ability</b> (CR
///   602.1): "Target creature can't be blocked this turn." Cost is a single
///   <see cref="TapTwoUntappedArtifactsCost"/> for two artifacts (CR 602.2b /
///   118.12 — printed-word tap-as-cost, not a {T} symbol). On resolution the
///   factory reads <see cref="ActivatedAbility.ChosenTargets"/> and, when the
///   choice is a battlefield <see cref="Creature"/>, registers a single-target
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> (CR 509.1c) against the
///   supplied <see cref="ContinuousEffectsService"/>. The restriction carries
///   the default <c>expiresAtEndOfTurn = true</c> — "this turn" (CR 514.2
///   cleanup-step expiry). Untargeted, non-creature, or off-battlefield
///   choices resolve as a no-op (CR 608.2b — illegal target → effect does
///   nothing).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live TriggerManager / ZoneService wiring</b>: single-arg dispatcher
///   path attaches the ETB trigger structurally (no
///   <see cref="TriggerManager"/> registration), same posture as
///   <see cref="WhirlerVirtuosoFactory.Create(Player)"/>. The minted
///   Thopters enter via the raw zone branch and do NOT publish
///   <see cref="Majik.Core.Events.CardMovedEvent"/> — Soul-Warden-style
///   downstream triggers won't fire (same gap as Animation Module's Servo
///   without zones).
/// - <b>No live continuous-effects service</b>: when <paramref name="effects"/>
///   is null the unblockable grant no-ops (the tap-two-artifacts cost is
///   still part of the cost surface). Matches the Rogue's Passage shape-only
///   path.
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter targets to "any creature" — the resolution-time guard handles
///   illegal targets (CR 608.2b), same posture as Rogue's Passage.
/// </summary>
[CardName("Whirler Rogue")]
public static class WhirlerRogueFactory
{
    public const string CardName = "Whirler Rogue";
    public const int ThopterCount = 2;
    public const int ArtifactsToTap = 2;
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;
    public const string ThopterTokenName = "Thopter";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("whirler-rogue");

    /// <summary>
    /// Construct Whirler Rogue with no live continuous-effects service. The
    /// "tap two artifacts" ability is attached for shape observability but
    /// its "can't be blocked" grant no-ops on resolution. The ETB
    /// two-Thopter trigger is fully functional. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Whirler Rogue. When <paramref name="effects"/> is supplied,
    /// activating the "tap two untapped artifacts" ability and resolving
    /// against a battlefield <see cref="Creature"/> target registers a
    /// single-target CR 509.1c "can't be blocked" restriction on that
    /// creature until end of turn (CR 514.2).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the
    /// can't-be-blocked grant. May be null — the grant is then skipped
    /// (shape-only path).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity (2/2 Human Rogue Artificer, {2}{U}{U}) from the embedded
        // JSON definition.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, create two 1/1 colorless Thopter
        //    artifact creature tokens with flying."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create two 1/1 colourless Thopter tokens (flying)",
            () =>
            {
                var controller = card.Controller ?? owner;
                if (card.Zone != ZoneType.Battlefield) return; // CR 603.6c

                for (var i = 0; i < ThopterCount; i++)
                {
                    var spec = new TokenFactory.TokenSpec(
                        Name: ThopterTokenName,
                        Power: ThopterPower,
                        Toughness: ThopterToughness,
                        Subtypes: new[] { CardSubtype.Thopter },
                        Keywords: new[] { "Flying" },
                        Colors: Array.Empty<ManaColor>());

                    var token = TokenFactory.CreateOnBattlefield(spec, controller);

                    // CR 111.1 — Thopter tokens are artifact creatures. The
                    // TokenFactory shell only stamps Creature; layer Artifact
                    // on additively (mirrors Whirler Virtuoso's Thopter).
                    token.AddCardType(CardType.Artifact);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "Tap two untapped artifacts you control: Target creature can't
        //    be blocked this turn."
        // CR 602.2b / 118.12 — printed "Tap …" word is a tap-as-cost.
        // CR 509.1c — "can't be blocked" combat restriction.
        // CR 514.2 — "this turn" wears off at the cleanup step.
        // ----------------------------------------------------------------
        ActivatedAbility? unblockableAbility = null;
        var unblockableEffect = new Effect(
            $"{CardName}: target creature can't be blocked this turn",
            () =>
            {
                if (effects == null) return; // shape-only path

                if (unblockableAbility == null) return;
                if (unblockableAbility.ChosenTargets.Count == 0) return;
                if (unblockableAbility.ChosenTargets[0].Count == 0) return;

                if (unblockableAbility.ChosenTargets[0][0] is not Creature target)
                    return; // CR 608.2b — illegal / non-creature target → no-op
                if (target.Zone != ZoneType.Battlefield)
                    return; // target left the battlefield in response

                // expiresAtEndOfTurn defaults to true → "this turn".
                effects.Register(new CombatRestrictionEffect(
                    CombatRestriction.CannotBeBlocked,
                    target: target));
            });

        unblockableAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new TapTwoUntappedArtifactsCost(ArtifactsToTap),
            },
            effects: new IEffect[] { unblockableEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(unblockableAbility);

        return card;
    }
}
