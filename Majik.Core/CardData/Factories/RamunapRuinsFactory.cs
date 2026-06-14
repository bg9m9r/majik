using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

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
///   — an <see cref="ActivatedAbility"/> whose costs are a
///   <see cref="ManaCostCost"/> ({2}{R}{R}) + <see cref="AdditionalCost.Tap"/> +
///   the real <b>"Sacrifice a Desert"</b> typed non-self filtered cost
///   (<see cref="SacrificeFilteredCost"/> via
///   <see cref="Primitives.Costs.SacrificeASubtype"/>, CR 701.16 — same primitive
///   the production <see cref="LandActivatedAbilityBinder"/> path binds). The
///   sacrifice is a genuine battlefield → graveyard COST over ANY Desert the
///   controller controls; this land itself qualifies (CR 305 — it has the Desert
///   subtype) and is the deterministic v1 pick when it is the only Desert, but
///   the controller may pre-pick another Desert via
///   <see cref="SacrificeFilteredCost.Target"/>. On resolution the effect deals
///   2 damage to each opponent (CR 800.4 — "opponent" means every other player).
///   Damage routes through <see cref="Fx.DealDamage"/> (Player →
///   <see cref="Player.LoseLife"/>, CR 119.8); the live "each opponent" list is
///   read off the resolution context (<see cref="ContextOpponents"/>) so it is
///   correct on the routed prod build.
///
/// ## Deferred (v1 gaps)
/// - <b>"Sacrifice a Desert" agent prompt</b>: the filtered sacrifice cost
///   deterministically picks the first eligible Desert when the agent has not
///   pre-set a <see cref="SacrificeFilteredCost.Target"/> — the same prompting
///   MVP every sibling sacrifice-picker cost waits on.
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
    /// <summary>
    /// Construct Ramunap Ruins. The sacrifice ability deals 2 damage to "each
    /// opponent" read from the live resolution context at resolution
    /// (<see cref="ContextOpponents"/>), so it is correct on the production
    /// routed build (which dispatches this single-arg overload).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
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
        // CR 602 — activated ability (non-mana). Costs: {2}{R}{R} + {T} + the
        // real "Sacrifice a Desert" typed non-self filtered cost (CR 701.16 —
        // SacrificeFilteredCost, the same primitive the production
        // LandActivatedAbilityBinder path binds). The sacrifice is a genuine
        // COST over ANY Desert the controller controls; this land itself
        // qualifies (CR 305 — Desert subtype) and is the deterministic v1 pick.
        // The damage half iterates each opponent (CR 800.4) read off the live
        // resolution context and routes through Fx.DealDamage (CR 119.8).
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: deal {SacDamage} damage to each opponent",
            ctx =>
            {
                // CR 800.4 — deal 2 damage to each opponent, read from the LIVE
                // resolution context — NOT a captured resolver, which was null
                // on the routed prod build and made the damage INERT in real
                // games (resolver-null bug class; mirrors Stormbreath #2540 /
                // Grist #2549).
                var controller = land.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    Fx.DealDamage(opp, SacDamage);
                }
                return ValueTask.CompletedTask;
            });

        var sacAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{R}{R}"),
                AdditionalCost.Tap(land),
                Primitives.Costs.SacrificeASubtype(CardSubtype.Desert),
            },
            effects: new IEffect[] { sacEffect });

        land.AddAbility(sacAbility);

        return land;
    }
}
