using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Castle Embereth (Throne of Eldraine / reprints).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped unless you control a Mountain.
///    {T}: Add {R}.
///    {1}{R}{R}, {T}: Creatures you control get +1/+0 until end of turn."
///
/// Scryfall type line: Land (no basic supertype, no subtypes).
/// Castle Embereth is NOT itself a Mountain.
///
/// Mirrors <see cref="CastleArdenvaleFactory"/> / <see cref="CastleLocthwainFactory"/>
/// (the white / black twins of the Eldraine Castle cycle) — the only
/// differences are the gating subtype (Mountain vs Plains/Swamp), the
/// produced colour ({R}), and the second activated ability (team-wide
/// +1/+0 pump vs token creation / draw-and-lose-life).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype.
/// - <b>ETB tapped unless you control a Mountain (CR 614.1c)</b> — registered
///   as a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. The predicate checks whether the
///   controller controls at least one other permanent with the
///   <see cref="CardSubtype.Mountain"/> subtype (dual lands with the Mountain
///   subtype, snow-covered Mountains, etc. all qualify). The card itself is
///   excluded via reference equality (same shape as
///   <see cref="CastleArdenvaleFactory"/>). Single-arg dispatcher path omits
///   the replacement (shape-only posture).
/// - <b>{T}: Add {R}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{1}{R}{R}, {T}: Creatures you control get +1/+0 until end of turn.</b>
///   Modelled as an <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{1}{R}{R}"), AdditionalCost.Tap(self)]</c>.
///   Resolution snapshots the controller's battlefield creatures at
///   resolution time (CR 608.2) and registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) on each — CR 613.1c
///   Layer 7c, end-of-turn cleanup per CR 514.2. This is the same
///   primitive Reckless Bushwhacker / Violent Outburst use for their team
///   +1/+0 rider (minus the Haste keyword grant, which Castle Embereth does
///   not have).
///
/// ## Notes
/// - The pump effect lambda captures <c>land</c> (not <c>owner</c>) so live
///   controller tracking via <see cref="Card.Controller"/> picks up
///   control-change effects at resolution time (same posture as
///   <see cref="CastleLocthwainFactory"/>).
/// - Creatures without a wired <see cref="ContinuousEffectsService"/>
///   (<see cref="Creature.ActiveEffects"/> null in shape-only tests) silently
///   no-op rather than NRE'ing — mirrors
///   <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>.
/// </summary>
[CardName("Castle Embereth")]
public static class CastleEmberethFactory
{
    public const string CardName = "Castle Embereth";

    /// <summary>+P pump magnitude. Castle Embereth prints +1/+0.</summary>
    public const int PumpPower = 1;
    /// <summary>+T pump magnitude. Castle Embereth prints +1/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Castle Embereth without a <see cref="ReplacementBus"/>
    /// wired. The ETB-tapped-unless-Mountain predicate is omitted (shape-only
    /// posture); the mana ability and pump ability are still attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Castle Embereth.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control a Mountain" replacement is registered
    /// (CR 614.1c). May be null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic Land — no supertype, no subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control a Mountain (CR 614.1c).
        //
        // Predicate: entersUntappedIf returns true ⟺ the controller
        // controls at least one land (other than this card) with the
        // CardSubtype.Mountain subtype. Reference-equality exclusion of self
        // mirrors CastleArdenvaleFactory's single-type predicate shape.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Mountain))));
        }

        // ----------------------------------------------------------------
        // {T}: Add {R} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {1}{R}{R}, {T}: Creatures you control get +1/+0 until end of turn.
        //
        // CR 602 — ordinary activated ability. Cost = {1}{R}{R} mana + tap
        // self. Resolution snapshots the controller's battlefield creatures
        // at resolution time (CR 608.2) and registers a +1/+0 EOT pump
        // (CR 613.1c Layer 7c, cleanup CR 514.2) on each.
        //
        // The effect lambda captures `land` (not `owner`) so live controller
        // tracking via land.Controller picks up control-change effects at
        // resolution time.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                var controller = land.Controller ?? owner;

                // Snapshot to a list before applying so any same-step
                // side effects don't disturb the enumeration (same posture
                // as ViolentOutburstFactory.ApplyPumpAndHaste / Pyroclasm).
                var creatures = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();

                foreach (var creature in creatures)
                {
                    // Shape-only safety — without a live ContinuousEffectsService
                    // the pump body silently no-ops rather than NRE'ing.
                    if (creature.ActiveEffects == null) continue;

                    // CR 613.1c Layer 7c — +1/+0 until end of turn.
                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{R}{R}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { pumpEffect }));

        return land;
    }
}
