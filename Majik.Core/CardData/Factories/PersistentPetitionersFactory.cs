using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Persistent Petitioners (Ravnica Allegiance, {1}{U}).
///
/// Creature — Human Advisor 1/3. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "{1}, {T}: Target player mills a card. (They put the top card of their
///    library into their graveyard.)
///    Tap four untapped Advisors you control: Target player mills twelve cards.
///    A deck can have any number of cards named Persistent Petitioners."
///
/// The pillar of the dedicated "mill with an army of identical Advisors" deck:
/// a single Petitioners grinds one card per turn, while four of them tap
/// together to mill twelve at once.
///
/// The base shape (name / Creature / Human+Advisor / {1}{U} / 1/3) is
/// materialised from the embedded JSON definition (<c>persistent-petitioners.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="ShacklegeistFactory"/>). Both activated abilities are layered on
/// here — the JSON ability schema expresses neither a {1},{T} mill nor a
/// "tap four Advisors you control" cost.
///
/// ## Implemented (v1)
///
/// - 1/3 Creature — Human Advisor at printed cost {1}{U}; owner / controller
///   wired. Both <see cref="CardSubtype.Human"/> and
///   <see cref="CardSubtype.Advisor"/> are stamped so Human-tribal and
///   Advisor-tribal anchors (and the tap-four-Advisors cost itself) see it.
///
/// - <b>"{1}, {T}: Target player mills a card." (CR 602 + CR 701.13)</b>:
///   an <see cref="ActivatedAbility"/> whose costs are a
///   <see cref="ManaCostCost"/> for the printed {1} (CR 602.1b) plus
///   <see cref="AdditionalCost.Tap"/> on Petitioners itself (the {T} symbol —
///   summoning-sickness / tapped-state legality handled by the cost layer,
///   CR 302.6). A single 1..1 "target player" <see cref="TargetRequest"/> is
///   declared; on resolve the chosen player mills 1 via
///   <see cref="MillAction.Apply"/>.
///
/// - <b>"Tap four untapped Advisors you control: Target player mills twelve
///   cards." (CR 602 + CR 701.13)</b>: a
///   <see cref="PersistentPetitionersMillTwelveAbility"/> whose sole cost is the
///   <see cref="TapAdvisorsYouControlCost"/> (count 4 — the Advisor twin of
///   Shacklegeist's tap-two-Spirits cost; the printed word "Tap", NOT a {T}
///   symbol, so CR 302.6 summoning sickness does not apply and Petitioners
///   itself is an eligible Advisor to tap). A single 1..1 "target player"
///   <see cref="TargetRequest"/> is declared; on resolve the chosen player
///   mills 12.
///
/// Both mill effects read the chosen target off
/// <see cref="ActivatedAbility.ChosenTargets"/>; when the token does not resolve
/// to a <see cref="Player"/> the effect no-ops per CR 608.2b (illegal target at
/// resolution) — same posture as Glimpse the Unthinkable / Brain Freeze.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"A deck can have any number of cards named Persistent Petitioners."</b>
///   is a deck-construction rule (CR 100.2a exception), not an in-game effect —
///   there is no gameplay behaviour to model. Deck legality / singleton-format
///   limits are outside the engine's per-card scope, so this clause is N/A here.
/// - <b>Which four Advisors are tapped</b>: the cost falls back to the first
///   four eligible Advisors in battlefield order when the agent does not set an
///   explicit pick (deterministic v1, same posture as
///   <see cref="TapSpiritsYouControlCost"/> / <see cref="TapElvesYouControlCost"/>).
/// </summary>
[CardName("Persistent Petitioners")]
public static class PersistentPetitionersFactory
{
    public const string CardName = "Persistent Petitioners";
    public const string Slug = "persistent-petitioners";

    /// <summary>CR 602 — printed {1} portion of the first ability's cost.</summary>
    public const string MillOneManaCost = "{1}";

    /// <summary>Cards milled by the {1}, {T} ability.</summary>
    public const int MillOneCount = 1;

    /// <summary>Untapped Advisors tapped by the second ability's cost.</summary>
    public const int AdvisorsToTap = 4;

    /// <summary>Cards milled by the tap-four-Advisors ability.</summary>
    public const int MillTwelveCount = 12;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Persistent Petitioners owned and controlled by
    /// <paramref name="owner"/>. The base shape is materialised from the
    /// embedded JSON definition; both activated abilities are layered on here.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 602 — "{1}, {T}: Target player mills a card."
        card.AddAbility(BuildMillOneAbility(card, owner));

        // CR 602 — "Tap four untapped Advisors you control: Target player mills
        // twelve cards."
        card.AddAbility(BuildMillTwelveAbility(card, owner));

        return card;
    }

    /// <summary>
    /// Build the "{1}, {T}: Target player mills a card." activated ability.
    /// Exposed for tests that need to inspect or activate it.
    /// </summary>
    public static ActivatedAbility BuildMillOneAbility(Creature source, Player controller)
    {
        ActivatedAbility? ability = null;

        var millEffect = new Effect(
            $"{CardName}: target player mills {MillOneCount} card",
            () =>
            {
                if (ability == null) return;

                // CR 608.2b — resolve the chosen target player; no-op on an
                // illegal / unset target.
                if (ability.ChosenTargets.Count > 0
                    && ability.ChosenTargets[0].Count > 0
                    && ability.ChosenTargets[0][0] is Player target)
                {
                    MillAction.Apply(target, MillOneCount);
                }
            });

        ability = new ActivatedAbility(
            source: source,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost(MillOneManaCost),
                AdditionalCost.Tap(source),
            },
            effects: new IEffect[] { millEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        return ability;
    }

    /// <summary>
    /// Build the "Tap four untapped Advisors you control: Target player mills
    /// twelve cards." activated ability. Exposed for tests that need to inspect
    /// or activate it.
    /// </summary>
    public static PersistentPetitionersMillTwelveAbility BuildMillTwelveAbility(
        Creature source, Player controller)
    {
        var tapCost = new TapAdvisorsYouControlCost(AdvisorsToTap);
        return new PersistentPetitionersMillTwelveAbility(source, controller, tapCost);
    }
}

/// <summary>
/// Persistent Petitioners' "Tap four untapped Advisors you control: Target
/// player mills twelve cards." activated ability. Subclasses
/// <see cref="ActivatedAbility"/> so the embedded
/// <see cref="TapAdvisorsYouControlCost"/> is reachable from outside
/// (agents / tests) for choosing which Advisors to tap — same exposure posture
/// as <see cref="ShacklegeistTapAbility"/>.
/// </summary>
public sealed class PersistentPetitionersMillTwelveAbility : ActivatedAbility
{
    /// <summary>
    /// The tap-four-Advisors cost paid as part of activating this ability. Set
    /// <see cref="TapAdvisorsYouControlCost.Targets"/> before activation to pick
    /// specific Advisors; otherwise the cost falls back to its deterministic
    /// first-eligible pick.
    /// </summary>
    public TapAdvisorsYouControlCost TapChoice { get; }

    internal PersistentPetitionersMillTwelveAbility(
        Creature source,
        Player controller,
        TapAdvisorsYouControlCost tapCost)
        : base(
            source: source,
            controller: controller,
            // The entire activation cost is the tap-four-Advisors cost (no mana,
            // no {T} symbol on Petitioners itself).
            costs: new ICost[] { tapCost },
            effects: new IEffect[]
            {
                new Effect(
                    $"{PersistentPetitionersFactory.CardName}: target player mills "
                    + $"{PersistentPetitionersFactory.MillTwelveCount} cards",
                    ctx =>
                    {
                        // CR 608.2b — resolve the chosen target player; no-op on
                        // an illegal / unset target.
                        if (ctx.ChosenTargets.Count > 0
                            && ctx.ChosenTargets[0].Count > 0
                            && ctx.ChosenTargets[0][0] is Player target)
                        {
                            MillAction.Apply(
                                target, PersistentPetitionersFactory.MillTwelveCount);
                        }

                        return ValueTask.CompletedTask;
                    }),
            },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            })
    {
        TapChoice = tapCost;
    }
}
