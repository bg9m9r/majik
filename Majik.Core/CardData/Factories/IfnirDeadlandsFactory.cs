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
/// Named-card factory for Ifnir Deadlands (Amonkhet, Land — Desert).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add {B}.
///    {2}{B}{B}, {T}, Sacrifice a Desert: Put two -1/-1 counters on target
///    creature an opponent controls. Activate only as a sorcery."
///
/// Ifnir Deadlands is the black member of the Amonkhet "pay-life Desert"
/// cycle — the structural twin of <see cref="RamunapRuinsFactory"/> (the red
/// member). The base shape (name, Land, Desert subtype, {T}: Add {C} mana
/// ability) is materialised from the embedded JSON definition
/// (<c>ifnir-deadlands.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The "Pay 1 life: Add {B}" mana
/// ability and the sorcery-speed sacrifice/-1-/-1 ability are layered on here
/// because the JSON <c>AbilityDefinition</c> schema expresses neither.
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> + <b>{T}: Add {C}</b> (from JSON; CR 605.1 — a mana
///   ability, no stack). {C} has no dedicated colourless bucket today, so it
///   is stored as +1 generic (same modelling as Ramunap Ruins / every other
///   <c>produces: C</c> land).
/// - <b>{T}, Pay 1 life: Add {B}</b> — a second <see cref="ManaAbility"/>
///   producing {B} via the cost-plus-payer overload (same "Pay 1 life" shape
///   as Ramunap Ruins' red mode):
///   <list type="bullet">
///     <item><c>additionalCostPayer</c> = <c>p =&gt; p.LoseLife(1)</c> — the
///       printed "Pay 1 life" cost (CR 119.3).</item>
///     <item><c>canActivateCheck</c> gates on untapped AND life &gt; 1
///       (CR 119.4 — a player can't pay more life than they have).</item>
///   </list>
/// - <b>{2}{B}{B}, {T}, Sacrifice a Desert: Put two -1/-1 counters on target
///   creature an opponent controls. Activate only as a sorcery.</b> — an
///   <see cref="ActivatedAbility"/> with:
///   <list type="bullet">
///     <item><see cref="ManaCostCost"/> ({2}{B}{B}) + <see cref="AdditionalCost.Tap"/>.</item>
///     <item><c>sorcerySpeed: true</c> — the CR 117.1a / 307.5 "Activate only
///       as a sorcery" timing rider.</item>
///     <item>A 1..1 <see cref="TargetRequest"/> "target creature an opponent
///       controls" (BotIntent.Removal). The resolution body reads
///       <see cref="ActivatedAbility.ChosenTargets"/> and gates the pick
///       (CR 608.2b — illegal target → the counter half does nothing).</item>
///   </list>
///   On resolution the effect:
///   <list type="number">
///     <item>Sacrifices a Desert — this land itself qualifies (CR 305 — it has
///       the Desert subtype). Performed inside the effect closure, the same
///       posture as <see cref="RamunapRuinsFactory"/> / Barbarian Ring (the
///       generic <see cref="AdditionalCost.Sacrifice"/> payment is a no-op
///       stub; v1 sacrifices self rather than letting the controller choose
///       an arbitrary other Desert).</item>
///     <item>Puts two -1/-1 counters (CR 122) on the chosen creature via
///       <see cref="Fx.PlaceCounter"/>. Subsequent SBAs (CR 704.5q — a
///       creature with toughness 0 or less is put into its owner's graveyard;
///       CR 704.5g — lethal damage) are the engine's responsibility once the
///       counters land.</item>
///   </list>
///
/// ## Deferred (v1 gaps — shared with Ramunap Ruins / the Tectonic Edge family)
/// - <b>"Sacrifice a Desert" choice</b>: v1 sacrifices this land itself rather
///   than offering the controller a choice among all Deserts they control
///   (the generic <see cref="AdditionalCost.Sacrifice"/> is a no-op stub).
/// - <b>"creature an opponent controls" target legality filtering</b>:
///   ActionValidator does not yet narrow the candidate pool to opponent-
///   controlled creatures; the resolution-time guard enforces (a) Creature
///   type, (b) on the battlefield, (c) controller is NOT this land's
///   controller (CR 608.2b). Same posture as the Tectonic Edge target guard.
/// </summary>
[CardName("Ifnir Deadlands")]
public static class IfnirDeadlandsFactory
{
    public const string CardName = "Ifnir Deadlands";
    public const string Slug = "ifnir-deadlands";

    /// <summary>Number of -1/-1 counters placed by the sacrifice ability
    /// (CR 122).</summary>
    public const int CounterCount = 2;

    /// <summary>
    /// Construct Ifnir Deadlands. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: nonbasic Land with the
        // Desert subtype + the {T}: Add {C} mana ability (CR 605.1). The
        // Pay-1-life {B} mana ability and the sorcery-speed sacrifice ability
        // are layered on below — neither is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}, Pay 1 life: Add {B}.
        // CR 605.1 — mana ability, no stack. Built via the cost-plus-payer
        // overload of ManaAbility:
        //   - additionalCostPayer pays the printed "Pay 1 life" cost (CR 119.3).
        //   - canActivateCheck gates on untapped AND life > 1 (CR 119.4 — a
        //     player can't pay more life than they have). Same shape as
        //     Ramunap Ruins' Pay-1-life red mode.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("B"),
            canActivateCheck: () => !land.IsTapped && owner.LifeTotal > 1,
            additionalCostPayer: p => p.LoseLife(1)));

        // ----------------------------------------------------------------
        // {2}{B}{B}, {T}, Sacrifice a Desert:
        //   Put two -1/-1 counters on target creature an opponent controls.
        //   Activate only as a sorcery.
        // CR 602 — activated ability (non-mana). Mana cost {2}{B}{B} + {T}.
        // The "Sacrifice a Desert" cost is paid inside the effect closure by
        // sacrificing this land itself (it has the Desert subtype, so it is a
        // legal sacrifice) — same no-op-stub posture as Ramunap Ruins. The
        // counter half reads the chosen target and stamps two -1/-1 counters
        // (CR 122). sorcerySpeed:true carries the CR 117.1a / 307.5 timing
        // rider.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice a Desert + put two -1/-1 counters on target creature an opponent controls",
            () =>
            {
                if (sacAbility == null) return;

                // Sacrifice a Desert (this land qualifies) — battlefield →
                // owner's graveyard. Performed as part of the already-paid
                // cost; runs regardless of target legality. Mirrors the
                // Ramunap Ruins / Barbarian Ring closure.
                SacrificeSelf(land);

                // Counter half — gate the chosen target (CR 608.2b — illegal
                // target → the counter half does nothing).
                if (sacAbility.ChosenTargets.Count == 0) return;
                if (sacAbility.ChosenTargets[0].Count == 0) return;

                if (sacAbility.ChosenTargets[0][0] is not Creature target) return;
                if (!target.HasType(CardType.Creature)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                // "an opponent controls" — the controller of the target must
                // not be this land's controller (CR 608.2b).
                var controller = land.Controller ?? owner;
                if (ReferenceEquals(target.Controller, controller)) return;

                // CR 122 — put two -1/-1 counters on the target creature.
                Fx.PlaceCounter(target, CounterType.MinusOneMinusOne, CounterCount);
            });

        sacAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{B}{B}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
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
    /// the closure used by Ramunap Ruins / Barbarian Ring.
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
