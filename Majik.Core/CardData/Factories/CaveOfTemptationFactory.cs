using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cave of Temptation (Tarkir: Dragonstorm, Land — Cave).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color.
///    {4}, {T}, Sacrifice this land: Put two +1/+1 counters on target
///    creature. Activate only as a sorcery."
///
/// The "filtered any-colour" fixing land — a {C}-or-pay-{1}-for-any-colour
/// source whose late-game mode sacrifices itself to grow a creature. The base
/// shape (name, Land, {T}: Add {C} mana ability) is materialised from the
/// embedded JSON definition (<c>cave-of-temptation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The any-colour mana abilities and the
/// sorcery-speed sacrifice/+1+1 ability are layered on here because the JSON
/// <c>ManaAbilityDefinition</c> schema expresses neither any-colour fan-out
/// nor an activated ability with a sacrifice cost + counter effect.
///
/// ## Implemented (v1)
/// - <b>Land — Cave</b> + <b>{T}: Add {C}</b> (from JSON; CR 605.1 — a mana
///   ability, no stack). {C} is tracked in the generic bucket — same
///   modelling as every other <c>produces: "C"</c> land
///   (<see cref="HoldoutSettlementFactory"/> / <see cref="MirrodinsCoreFactory"/>).
/// - <b>{1}, {T}: Add one mana of any color.</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG), same any-colour
///   fan-out as Mana Confluence / Spire of Industry. Each uses the
///   additional-cost overload of <see cref="ManaAbility"/> with the printed
///   <b>{1}</b> as the extra cost (same shape as the Signet cycle —
///   <see cref="BorosSignetFactory"/>):
///     - <c>canActivateCheck</c> gates on untapped AND
///       <see cref="ManaPool.CanPay"/>({1}) (CR 605.3a — the cost must be
///       payable, so we never tap only to no-op on payment).
///     - <c>additionalCostPayer</c> = <c>p =&gt; p.PayMana({1})</c> — the
///       printed {1}, deducted from the pool atomically with the self-tap
///       (CR 605.1). The activator picks the colour by picking the matching
///       ability slot, so no separate colour prompt is needed.
/// - <b>{4}, {T}, Sacrifice this land: Put two +1/+1 counters on target
///   creature. Activate only as a sorcery.</b> — an
///   <see cref="ActivatedAbility"/> with:
///     - <see cref="ManaCostCost"/> ({4}) + <see cref="AdditionalCost.Tap"/> +
///       <see cref="AdditionalCost.Sacrifice"/>.
///     - <c>sorcerySpeed: true</c> — the CR 117.1a / 307.5 "Activate only as a
///       sorcery" timing rider.
///     - A 1..1 <see cref="TargetRequest"/> "target creature" (any creature —
///       no controller restriction, unlike <see cref="IfnirDeadlandsFactory"/>).
///   On resolution the effect (1) sacrifices this land itself (battlefield →
///   owner's graveyard — same no-op-stub posture as Ifnir Deadlands / Ramunap
///   Ruins, since the generic <see cref="AdditionalCost.Sacrifice"/> payment is
///   a stub) and (2) puts two +1/+1 counters (CR 122) on the chosen creature,
///   gating the pick (CR 608.2b — an illegal/absent target → the counter half
///   does nothing).
///
/// ## Deferred (v1 gaps — shared with the filter-land + sac-land families)
/// - <b>{1} additional cost requires mana already in the pool.</b> The engine
///   does not auto-tap other sources to feed the {1} (no look-ahead mana
///   planner) — identical to the Signet cycle and every other
///   additional-mana-cost mana ability.
/// - <b>"any color" is five slots, not one modal pick</b> — same gap as
///   Glimmervoid / Mana Confluence / Spire of Industry.
/// </summary>
[CardName("Cave of Temptation")]
public static class CaveOfTemptationFactory
{
    public const string CardName = "Cave of Temptation";
    public const string Slug = "cave-of-temptation";

    /// <summary>Number of +1/+1 counters placed by the sacrifice ability
    /// (CR 122).</summary>
    public const int CounterCount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Cave of Temptation owned and controlled by
    /// <paramref name="owner"/>. Attaches the JSON {C} ability, the five
    /// {1}, {T}: any-colour abilities, and the {4}, {T}, Sacrifice: +1/+1
    /// sorcery-speed ability.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: nonbasic Land + the
        // {T}: Add {C} mana ability (CR 605.1 — a mana ability, no stack).
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {1}, {T}: Add one mana of any color. (CR 605.1 — mana ability.)
        //
        // Five ManaAbility instances (one per WUBRG). Each pays the printed
        // {1} as an additional cost (the Signet "{1}, {T}: Add …" shape):
        //   - canActivateCheck: on the battlefield, untapped, and the pool
        //     can pay {1} (CR 605.3a — the cost must be payable).
        //   - additionalCostPayer: PayMana({1}) — deducted atomically with
        //     the self-tap.
        // ----------------------------------------------------------------
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new CaveOfTemptationManaAbility(land, owner, pip));
        }

        // ----------------------------------------------------------------
        // {4}, {T}, Sacrifice this land:
        //   Put two +1/+1 counters on target creature.
        //   Activate only as a sorcery.
        // CR 602 — activated ability (non-mana). Mana cost {4} + {T} +
        // sacrifice. The "Sacrifice this land" cost is paid inside the effect
        // closure by sacrificing this land itself (same no-op-stub posture as
        // Ifnir Deadlands / Ramunap Ruins — the generic Sacrifice cost is a
        // stub). The counter half reads the chosen target and stamps two
        // +1/+1 counters (CR 122). sorcerySpeed:true carries the CR 117.1a /
        // 307.5 timing rider. "target creature" has NO controller restriction.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice this land + put two +1/+1 counters on target creature",
            () =>
            {
                if (sacAbility == null) return;

                // Sacrifice this land — battlefield → owner's graveyard.
                // Performed as part of the already-paid cost; runs regardless
                // of target legality.
                SacrificeSelf(land);

                // Counter half — gate the chosen target (CR 608.2b — illegal /
                // absent target → the counter half does nothing).
                if (sacAbility.ChosenTargets.Count == 0) return;
                if (sacAbility.ChosenTargets[0].Count == 0) return;
                if (sacAbility.ChosenTargets[0][0] is not Creature target) return;
                if (!target.HasType(CardType.Creature)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 122 — put two +1/+1 counters on the target creature.
                Fx.PlaceCounter(target, CounterType.PlusOnePlusOne, CounterCount);
            });

        sacAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{4}"),
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
                    Intent: BotIntent.Buff),
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

/// <summary>
/// Cave of Temptation's per-colour mana ability for "{1}, {T}: Add one mana of
/// any color". Subclasses <see cref="ManaAbility"/> so the colour pip is
/// inspectable from tests — same posture as
/// <see cref="HoldoutSettlementManaAbility"/>. The activation cost is the
/// printed {1} (paid from the pool) plus the implicit self-tap.
/// </summary>
public sealed class CaveOfTemptationManaAbility : ManaAbility
{
    /// <summary>Colour pip this ability produces (one of W / U / B / R / G).</summary>
    public string ColorPip { get; }

    internal CaveOfTemptationManaAbility(Land source, Player controller, string colorPip)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(colorPip),
            // CR 605.3a — the cost must be payable: on the battlefield,
            // untapped, and the pool can pay the printed {1}.
            canActivateCheck: () => source.Zone == ZoneType.Battlefield
                && !source.IsTapped
                && controller.ManaPool.CanPay(ManaCost.Parse("1")),
            // CR 605.1 — pay {1} atomically with the self-tap.
            additionalCostPayer: p => p.PayMana(ManaCost.Parse("1")))
    {
        ColorPip = colorPip;
    }
}
