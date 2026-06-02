using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ramunap Ruins (Hour of Devastation, Land — Desert).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add {R}.
///    {2}{R}{R}, {T}, Sacrifice a Desert: This land deals 2 damage to each
///    opponent."
///
/// The base shape (name, Land, Desert subtype, {T}: Add {C} mana ability) is
/// materialised from the embedded JSON definition (<c>ramunap-ruins.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the same JSON-backed posture as
/// <see cref="DesertOfTheFerventFactory"/>. The "Pay 1 life: Add {R}" mana
/// ability and the sacrifice/damage activated ability are layered on here
/// because the JSON <c>AbilityDefinition</c> schema expresses neither.
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> + <b>{T}: Add {C}</b> (from JSON; CR 605.1 — a mana
///   ability, no stack). {C} has no dedicated colourless bucket today, so it
///   is stored as +1 generic (same modelling as every other <c>produces: C</c>
///   land — e.g. <see cref="HostileDesertFactory"/>).
/// - <b>{T}, Pay 1 life: Add {R}</b> — a second <see cref="ManaAbility"/>
///   producing {R} via the cost-plus-payer overload (same "Pay 1 life" shape
///   as <see cref="SpireOfIndustryFactory"/>):
///   <list type="bullet">
///     <item><c>additionalCostPayer</c> = <c>p =&gt; p.LoseLife(1)</c> — the
///       printed "Pay 1 life" cost (CR 119.3).</item>
///     <item><c>canActivateCheck</c> gates on untapped AND life &gt; 1 (CR
///       119.4 — "a player can't pay more life than they have"). This differs
///       from the painland damage rider (<see cref="PainLandCycleFactory"/>),
///       which has NO life floor because dealing damage to yourself is not a
///       "Pay life" cost.</item>
///   </list>
/// - <b>{2}{R}{R}, {T}, Sacrifice a Desert: deal 2 damage to each opponent</b>
///   — an <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>
///   ({2}{R}{R}) plus <see cref="AdditionalCost.Tap"/>. On resolution the
///   effect:
///   <list type="number">
///     <item>Sacrifices a Desert — this land itself qualifies (CR 305 — it has
///       the Desert subtype). Performed inside the effect closure, the same
///       posture as <see cref="BarbarianRingFactory"/> (the generic
///       <see cref="AdditionalCost.Sacrifice"/> payment is a no-op stub; v1
///       sacrifices self rather than letting the controller choose an
///       arbitrary other Desert).</item>
///     <item>Deals 2 damage to each opponent (CR 800.4 — "opponent" means
///       every other player). Damage routes through <see cref="Fx.DealDamage"/>
///       (Player → <see cref="Player.LoseLife"/>, CR 119.8). The live
///       "each opponent" list is resolver-injected via
///       <paramref name="opponentResolver"/> — same pattern as
///       <see cref="ElectrostaticFieldFactory"/>; without a resolver the
///       damage half no-ops (no <c>Player.Opponents</c> accessor at v1).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>"Sacrifice a Desert" choice</b>: v1 sacrifices this land itself rather
///   than offering the controller a choice among all Deserts they control
///   (the generic <see cref="AdditionalCost.Sacrifice"/> is a no-op stub —
///   same simplification as Barbarian Ring / Pyrite Spellbomb).
/// - <b>Live "each opponent" enumeration</b>: no <c>Player.Opponents</c>
///   accessor at v1 — the damage half is resolver-injected (shared with
///   Electrostatic Field / Voldaren Epicure). The shape-only dispatcher path
///   no-ops the damage half but still performs the sacrifice.
/// - <b>Damage event routing</b>: the 2 damage goes through
///   <see cref="Player.LoseLife"/>, not a full damage event — damage-prevention
///   shields don't intercept it (same simplification as the painland cycle).
/// </summary>
[CardName("Ramunap Ruins")]
public static class RamunapRuinsFactory
{
    public const string CardName = "Ramunap Ruins";
    public const string Slug = "ramunap-ruins";
    public const int SacDamage = 2;

    /// <summary>
    /// Construct Ramunap Ruins with no opponent resolver. The sacrifice/damage
    /// ability is attached to the card shape; its damage half no-ops (no
    /// opponent resolver) while the sacrifice still resolves. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, opponentResolver: null);

    /// <summary>
    /// Construct Ramunap Ruins with an optional live "each opponent" resolver
    /// for the sacrifice ability's damage half.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent"
    /// (CR 800.4). Without a resolver the damage half no-ops; the sacrifice
    /// still resolves.</param>
    public static Land Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: nonbasic Land with the
        // Desert subtype + the {T}: Add {C} mana ability (CR 605.1). The
        // Pay-1-life {R} mana ability and the sacrifice/damage ability are
        // layered on below — neither is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}, Pay 1 life: Add {R}.
        // CR 605.1 — mana ability, no stack. Built via the cost-plus-payer
        // overload of ManaAbility:
        //   - additionalCostPayer pays the printed "Pay 1 life" cost (CR 119.3).
        //   - canActivateCheck gates on untapped AND life > 1 (CR 119.4 — a
        //     player can't pay more life than they have). Same shape as
        //     Spire of Industry's Pay-1-life mode.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("R"),
            canActivateCheck: () => !land.IsTapped && owner.LifeTotal > 1,
            additionalCostPayer: p => p.LoseLife(1)));

        // ----------------------------------------------------------------
        // {2}{R}{R}, {T}, Sacrifice a Desert:
        //   This land deals 2 damage to each opponent.
        // CR 602 — activated ability (non-mana). Mana cost {2}{R}{R} + {T}.
        // The "Sacrifice a Desert" cost is paid inside the effect closure by
        // sacrificing this land itself (it has the Desert subtype, so it is a
        // legal sacrifice) — same no-op-stub posture as Barbarian Ring. The
        // damage half iterates each opponent (CR 800.4) via the injected
        // resolver and routes through Fx.DealDamage (CR 119.8).
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice a Desert + deal {SacDamage} damage to each opponent",
            () =>
            {
                // Sacrifice a Desert (this land qualifies) — battlefield →
                // owner's graveyard. Performed before damage; mirrors the
                // Barbarian Ring closure.
                SacrificeSelf(land, owner);

                // CR 800.4 — deal 2 damage to each opponent. Without a resolver
                // the player aggregate exposes no opponents list at v1, so the
                // damage half no-ops (same posture as Electrostatic Field).
                var opponents = opponentResolver?.Invoke();
                if (opponents is null) return;

                var controller = land.Controller ?? owner;
                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, controller)) continue;
                    Fx.DealDamage(opp, SacDamage);
                }
            });

        var sacAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{R}{R}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { sacEffect });

        land.AddAbility(sacAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="land"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. Mirrors
    /// the closure used by Barbarian Ring / Pyrite Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Land land, Player owner)
    {
        if (land.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(land);
        owner.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
    }
}
