using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grasping Dunes (Hour of Devastation, Land — Desert).
///
/// Oracle text (per task brief; matches Hour of Devastation printing):
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice this land: Put a -1/-1 counter on target creature.
///    Activate only as a sorcery."
///
/// Grasping Dunes is the colourless, single-counter cousin of
/// <see cref="IfnirDeadlandsFactory"/> — same animate-free "sacrifice a Desert,
/// stamp -1/-1 counters" shape, but cheaper ({1} vs {2}{B}{B}), only one
/// counter, sacrifices <b>this land</b> specifically (rather than "a Desert"),
/// and targets <b>any</b> creature (not just one an opponent controls).
///
/// The base shape (name, Land, Desert subtype, {T}: Add {C} mana ability) is
/// materialised from the embedded JSON definition (<c>grasping-dunes.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The sorcery-speed
/// sacrifice/-1-/-1 ability is layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither its compound cost nor its
/// targeted layer-free resolution (same posture as Ifnir Deadlands).
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> + <b>{T}: Add {C}</b> (from JSON; CR 605.1 — a mana
///   ability, no stack). {C} has no dedicated colourless bucket today, so it is
///   stored as +1 generic (same modelling as every other <c>produces: C</c>
///   land).
/// - <b>{1}, {T}, Sacrifice this land: Put a -1/-1 counter on target creature.
///   Activate only as a sorcery.</b> — an <see cref="ActivatedAbility"/> with:
///   <list type="bullet">
///     <item><see cref="ManaCostCost"/> ({1}) + <see cref="AdditionalCost.Tap"/>
///       + <see cref="AdditionalCost.Sacrifice"/>.</item>
///     <item><c>sorcerySpeed: true</c> — the CR 117.1a / 307.5 "Activate only as
///       a sorcery" timing rider.</item>
///     <item>A 1..1 <see cref="TargetRequest"/> "target creature"
///       (BotIntent.Removal). The resolution body reads
///       <see cref="ActivatedAbility.ChosenTargets"/> and gates the pick
///       (CR 608.2b — illegal target → the counter half does nothing).</item>
///   </list>
///   On resolution the effect:
///   <list type="number">
///     <item>Sacrifices this land — battlefield → owner's graveyard. Performed
///       inside the effect closure (the generic
///       <see cref="AdditionalCost.Sacrifice"/> payment is a no-op stub; v1
///       sacrifices self), same posture as Ifnir Deadlands / Ramunap Ruins.</item>
///     <item>Puts one -1/-1 counter (CR 122) on the chosen creature via
///       <see cref="Fx.PlaceCounter"/>. Subsequent SBAs (CR 704.5q — a creature
///       with toughness 0 or less is put into its owner's graveyard) are the
///       engine's responsibility once the counter lands.</item>
///   </list>
///
/// ## Deferred (v1 gaps — shared with Ifnir Deadlands)
/// - <b>"Sacrifice this land" choice</b>: trivially this card itself — but the
///   generic <see cref="AdditionalCost.Sacrifice"/> is still a no-op stub, so
///   the sacrifice is performed inside the resolution closure rather than as a
///   true cost payment.
/// - <b>Target legality filtering</b>: ActionValidator does not yet narrow the
///   candidate pool; the resolution-time guard enforces (a) Creature type and
///   (b) on the battlefield (CR 608.2b). Grasping Dunes targets <i>any</i>
///   creature, so there is no controller restriction (unlike Ifnir Deadlands).
/// </summary>
[CardName("Grasping Dunes")]
public static class GraspingDunesFactory
{
    public const string CardName = "Grasping Dunes";
    public const string Slug = "grasping-dunes";

    /// <summary>Number of -1/-1 counters placed by the sacrifice ability
    /// (CR 122).</summary>
    public const int CounterCount = 1;

    /// <summary>
    /// Construct Grasping Dunes. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: nonbasic Land with the
        // Desert subtype + the {T}: Add {C} mana ability (CR 605.1). The
        // sorcery-speed sacrifice ability is layered on below — it is not
        // expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this land:
        //   Put a -1/-1 counter on target creature.
        //   Activate only as a sorcery.
        // CR 602 — activated ability (non-mana). Mana cost {1} + {T} +
        // "Sacrifice this land". The sacrifice cost is paid inside the effect
        // closure by sacrificing this land itself — same no-op-stub posture as
        // Ifnir Deadlands. The counter half reads the chosen target and stamps
        // one -1/-1 counter (CR 122). sorcerySpeed:true carries the
        // CR 117.1a / 307.5 timing rider.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice this land + put a -1/-1 counter on target creature",
            () =>
            {
                if (sacAbility == null) return;

                // Sacrifice this land — battlefield → owner's graveyard.
                // Performed as part of the already-paid cost; runs regardless
                // of target legality. Mirrors the Ifnir Deadlands closure.
                SacrificeSelf(land);

                // Counter half — gate the chosen target (CR 608.2b — illegal
                // target → the counter half does nothing).
                if (sacAbility.ChosenTargets.Count == 0) return;
                if (sacAbility.ChosenTargets[0].Count == 0) return;

                if (sacAbility.ChosenTargets[0][0] is not Creature target) return;
                if (!target.HasType(CardType.Creature)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 122 — put one -1/-1 counter on the target creature.
                // Grasping Dunes targets ANY creature (no "an opponent
                // controls" restriction), so there is no controller guard.
                Fx.PlaceCounter(target, CounterType.MinusOneMinusOne, CounterCount);
            });

        sacAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            sorcerySpeed: true);

        land.AddAbility(sacAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="land"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. Mirrors
    /// the closure used by Ifnir Deadlands / Ramunap Ruins.
    /// </summary>
    private static void SacrificeSelf(Land land)
    {
        var ownerOfSelf = land.Owner;
        if (ownerOfSelf == null) return;
        if (land.Zone != ZoneType.Battlefield) return;

        var holder = land.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(land);
        ownerOfSelf.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
    }
}
