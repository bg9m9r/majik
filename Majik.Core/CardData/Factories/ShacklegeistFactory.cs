using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shacklegeist (Kaldheim, {1}{U}).
///
/// Creature — Spirit 2/2. Oracle text (verified against Scryfall 2026-06-02):
///   "Flying
///    This creature can block only creatures with flying.
///    Tap two untapped Spirits you control: Tap target creature you don't control."
///
/// The base shape (name / Creature / Spirit / {1}{U} / 2/2) is materialised
/// from the embedded JSON definition (<c>shacklegeist.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying + the activated ability
/// are layered on here — the JSON ability schema expresses neither a keyword
/// marker nor a "tap N Spirits you control" cost (same posture as
/// <see cref="HeritageDruidFactory"/>, whose tap-N-Elves cost likewise lives
/// in the factory).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker — same wiring as the other Spirit flyers, read combat-side via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>"Tap two untapped Spirits you control: Tap target creature you don't
///   control." (CR 602 + CR 701.21)</b>: an <see cref="ActivatedAbility"/>
///   whose sole cost is the <see cref="TapSpiritsYouControlCost"/> (count 2 —
///   the Spirit twin of Heritage Druid's tap-three-Elves cost; the printed
///   "Tap" word, NOT a {T} symbol, so CR 302.6 summoning sickness does not
///   apply and Shacklegeist itself is an eligible Spirit to tap). The 1..1
///   <see cref="TargetRequest"/> gathers only creatures controlled by a player
///   OTHER than Shacklegeist's controller (CR 109.5 — "you don't control" =
///   not you), mirroring <see cref="BrazenBorrowerFactory"/>'s opponent-scoped
///   gatherer. Resolution reads the chosen target off the
///   <see cref="ResolutionContext"/>, re-checks it is still a creature on the
///   battlefield controlled by an opponent (CR 608.2b), and taps it via
///   <see cref="Fx.Tap"/> (tapping an already-tapped permanent is a no-op,
///   CR 701.21b).
///
/// ## Deferred (v1 gaps)
/// - <b>"This creature can block only creatures with flying"</b>: the engine
///   has no combat-block-restriction primitive for "can only block X" yet —
///   <see cref="Majik.Core.Combat.CombatValidator.CanBlock"/> enforces the
///   reverse (a flyer can only be blocked by flyers/reach) but not this "this
///   blocker may only block flyers" rider. Documented as a known gap, identical
///   to the same printed clause on <see cref="BrazenBorrowerFactory"/>. Flying
///   is stamped as a keyword marker; the restriction picks up for free once the
///   "can only block X" primitive lands.
/// </summary>
[CardName("Shacklegeist")]
public static class ShacklegeistFactory
{
    public const string CardName = "Shacklegeist";
    public const string Slug = "shacklegeist";
    public const int SpiritsToTap = 2;

    private const string FlyingKeyword = "Flying";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Shacklegeist owned and controlled by <paramref name="owner"/>.
    /// The base shape is materialised from the embedded JSON definition; the
    /// Flying keyword marker and the "Tap two untapped Spirits you control: Tap
    /// target creature you don't control" activated ability are layered on
    /// here. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.9 — Flying. Combat blocking restriction (can only be blocked
        // by flyers / reach).
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // CR 602 — "Tap two untapped Spirits you control: Tap target creature
        // you don't control." Layered on here because the JSON ability schema
        // has no "tap N Spirits you control" cost.
        card.AddAbility(BuildTapAbility(card, owner));

        return card;
    }

    /// <summary>
    /// Build the "Tap two untapped Spirits you control: Tap target creature you
    /// don't control" activated ability. Exposed for tests that need to inspect
    /// or activate it.
    /// </summary>
    public static ShacklegeistTapAbility BuildTapAbility(Creature source, Player controller)
    {
        var tapCost = new TapSpiritsYouControlCost(SpiritsToTap);
        return new ShacklegeistTapAbility(source, controller, tapCost);
    }
}

/// <summary>
/// Shacklegeist's "Tap two untapped Spirits you control: Tap target creature
/// you don't control" activated ability. Subclasses <see cref="ActivatedAbility"/>
/// so the embedded <see cref="TapSpiritsYouControlCost"/> is reachable from
/// outside (agents / tests) for choosing which Spirits to tap — same exposure
/// posture as <see cref="HeritageDruidManaAbility"/>.
/// </summary>
public sealed class ShacklegeistTapAbility : ActivatedAbility
{
    /// <summary>
    /// The tap-two-Spirits cost paid as part of activating this ability. Set
    /// <see cref="TapSpiritsYouControlCost.Targets"/> before activation to pick
    /// specific Spirits; otherwise the cost falls back to its deterministic
    /// first-eligible pick.
    /// </summary>
    public TapSpiritsYouControlCost TapChoice { get; }

    internal ShacklegeistTapAbility(
        Creature source,
        Player controller,
        TapSpiritsYouControlCost tapCost)
        : base(
            source: source,
            controller: controller,
            // The entire activation cost is the tap-two-Spirits cost (no mana,
            // no {T} symbol on Shacklegeist itself).
            costs: new ICost[] { tapCost },
            effects: new IEffect[] { BuildTapTargetEffect(controller) },
            // CR 109.5 — "target creature you don't control": gather creatures
            // controlled by a player OTHER than the activating controller.
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, controller))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            })
    {
        TapChoice = tapCost;
    }

    /// <summary>
    /// CR 608.2b resolution-time legality re-check + CR 701.21 tap. Reads the
    /// chosen target off the live <see cref="ResolutionContext"/>; it must
    /// still be a creature on the battlefield controlled by someone other than
    /// this ability's controller, else the ability does nothing.
    /// </summary>
    private static IEffect BuildTapTargetEffect(Player controller) =>
        new Effect(
            "Shacklegeist: tap target creature you don't control",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0) return ValueTask.CompletedTask;
                if (ctx.ChosenTargets[0].Count == 0) return ValueTask.CompletedTask;
                if (ctx.ChosenTargets[0][0] is not Permanent target) return ValueTask.CompletedTask;

                // CR 608.2b — recheck legality at resolution.
                if (!target.HasType(CardType.Creature)) return ValueTask.CompletedTask;
                if (target.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;
                // CR 109.5 — must still be controlled by an opponent.
                if (ReferenceEquals(target.Controller, controller)) return ValueTask.CompletedTask;

                // CR 701.21 — tap. Idempotent on an already-tapped permanent.
                Fx.Tap(target);
                return ValueTask.CompletedTask;
            });
}
