using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cauldron of Souls (Shadowmoor, {4}).
///
/// Artifact. Oracle text (Scryfall, verified):
///   "{T}: Choose any number of target creatures. Each of them gains
///    persist until end of turn."
///
/// CR 702.78 — Persist: "When this creature dies, if it had no -1/-1
/// counters on it, return it to the battlefield under its owner's
/// control with a -1/-1 counter on it." See
/// <see cref="Majik.Core.Keywords.PersistFactory"/> for the death-trigger
/// primitive.
///
/// ## Implemented (v1)
/// - Artifact {4} with owner / controller wired.
/// - <b>Tap activated ability (CR 602.1)</b> with a single
///   <see cref="TargetRequest"/> for any number of target creatures
///   (<c>MinTargets = 0</c>, <c>MaxTargets = int.MaxValue</c>, mirrors
///   Indomitable Creativity's open-cardinality target shape). The
///   activation cost is <see cref="AdditionalCost.Tap"/> on Cauldron
///   itself.
/// - <b>Resolution</b>: for each chosen creature still on the
///   battlefield (CR 608.2b — illegal-target filter), register a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> for
///   <see cref="GrantedKeyword"/> against the creature's
///   <see cref="Creature.ActiveEffects"/> service. The grant places
///   only the keyword <em>marker</em> in Layer 6 — see gap note below
///   for the missing death-trigger side.
///
/// ## Deferred (v1 gaps)
/// - <b>Approximation: Indestructible-until-EOT in place of true
///   Persist</b>. The current
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> primitive grants a
///   bare keyword string at Layer 6 — it does NOT attach the actual
///   CR 702.78 death-trigger. The standalone
///   <see cref="Majik.Core.Keywords.PersistFactory"/> requires a fixed
///   <see cref="Creature"/> at construction time and registers a
///   <see cref="TriggeredAbility"/> directly on the creature, which
///   does not have a clean end-of-turn unregister path. Until a
///   <c>TriggeredAbilityUntilEndOfTurnEffect</c> primitive lands, the
///   v1 grant ships <see cref="GrantedKeyword"/> = "Indestructible" —
///   the approximation keeps the targeted creatures alive through
///   lethal damage / destroy effects for the turn (CR 702.12b),
///   matching Persist's most common practical use (combat-trade /
///   board-wipe protection). Side effects of true Persist (return
///   with a -1/-1 counter, value loops with Modular / undying-style
///   anaphors, Persist-from-graveyard re-entry) are deferred.
///   <b>Tracking</b>: when the engine grows
///   <c>GrantTriggeredAbilityUntilEndOfTurnEffect</c>, swap
///   <see cref="GrantedKeyword"/> to "Persist" and additionally
///   register <see cref="Majik.Core.Keywords.PersistFactory.Build"/>'s
///   death-trigger with that EOT-scoped wrapper. Behaviour delta is
///   isolated to the resolution closure.
/// - <b>Target prompting</b>: activated-ability flow does not yet
///   prompt for targets via the v1 dispatcher — callers set
///   <see cref="ActivatedAbility.ChosenTargets"/> before activation
///   (mirrors Guide of Souls' pump-target pattern). A future
///   agent-prompt MVP will close this.
/// - <b>"Any number of target creatures" requires at least one legal
///   target?</b> CR 601.2c — "any number of" includes zero, so the
///   ability has no minimum target requirement. v1's
///   <c>MinTargets = 0</c> mirrors this; the resolution closure
///   silently no-ops when the chosen set is empty.
/// </summary>
[CardName("Cauldron of Souls")]
public static class CauldronOfSoulsFactory
{
    public const string CardName = "Cauldron of Souls";
    public const string PrintedManaCost = "{4}";

    /// <summary>
    /// Keyword string registered on each chosen creature's
    /// <see cref="Creature.ActiveEffects"/> until end of turn (CR 514).
    /// See class xmldoc for the Persist → Indestructible
    /// approximation gap.
    /// </summary>
    public const string GrantedKeyword = "Indestructible";

    /// <summary>
    /// Construct Cauldron of Souls owned and controlled by
    /// <paramref name="owner"/>. The tap activated ability with the
    /// any-number-of-target-creatures shape is attached to the card.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Choose any number of target creatures. Each of them gains
        // persist until end of turn.
        // CR 602.1 — activated ability with the tap symbol as cost +
        // any-number-of-target-creatures request (CR 601.2c).
        // Resolution iterates ChosenTargets[0], registers the keyword
        // grant on each Creature still on the battlefield (CR 608.2b
        // illegal-target filter) via its ActiveEffects service. See
        // class xmldoc for the Persist → Indestructible approximation.
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;
        var tapEffect = new Effect(
            $"{CardName}: each chosen creature gains {GrantedKeyword} (Persist approx.) until end of turn",
            () =>
            {
                if (tapAbility == null) return;
                if (tapAbility.ChosenTargets.Count == 0) return;

                foreach (var raw in tapAbility.ChosenTargets[0])
                {
                    if (raw is not Creature target) continue;
                    if (target.Zone != ZoneType.Battlefield) continue; // CR 608.2b
                    if (target.ActiveEffects == null) continue; // shape-only no-op

                    target.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
                }
            });

        tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { tapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target creatures",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(tapAbility);

        return card;
    }
}
