using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shefet Dunes (Hour of Devastation) — the white
/// member of the HOU Desert sac-land cycle (sibling of Ramunap Ruins /
/// Ipnu Rivulet / Hashep Oasis / Ifnir Deltas).
///
/// Land — Desert. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add {W}.
///    {2}{W}{W}, {T}, Sacrifice a Desert: Creatures you control get +1/+1
///    until end of turn. Activate only as a sorcery."
///
/// ## Chassis
/// Reuses three established shapes:
/// - The Desert sac-land base (Land + Desert subtype + the {T}: Add {C} mana
///   ability) is declared declaratively in
///   <c>Majik.Core/CardData/Cards/shefet-dunes.json</c> and materialised via
///   <see cref="CardDefinitionFactory"/>, the same posture as
///   <see cref="DesertOfTheTrueFactory"/> / <see cref="HostileDesertFactory"/>.
/// - The pay-life white mana ability is the painland / Horizon-Canopy shape
///   (<see cref="CephalidColiseumFactory"/>): a <see cref="ManaAbility"/>
///   producing {W} with the cost-plus-payer overload — <c>canActivateCheck</c>
///   gates on !IsTapped AND a life floor (CR 119.4 — you can't pay a life cost
///   you can't afford), and <c>additionalCostPayer</c> pays 1 life
///   (CR 118.4).
/// - The anthem is the non-targeted +1/+1-until-EOT one-shot pump
///   (<see cref="RestlessPrairieFactory"/>): on resolution it snapshots the
///   controller's battlefield creatures (CR 608.2) and registers a +1/+1
///   <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c, CR 613.7c, expires in
///   the cleanup step — CR 514.2) on each.
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> (CR 205.3i — Desert is a land subtype). Non-basic,
///   non-legendary; the printed {T}: Add {C} mana ability is declared in JSON.
/// - <b>{T}: Add {C}</b> — vanilla colorless <see cref="ManaAbility"/>
///   (CR 605.1). {C} lands as +1 generic via <see cref="ManaCost.Parse"/>
///   (ManaCost.cs:170), the same as Aether Hub / Hostile Desert.
/// - <b>{T}, Pay 1 life: Add {W}</b> — painland-shaped pay-life mana ability
///   (CR 605.1 / CR 118.4). Life floor gate (CR 119.4).
/// - <b>{2}{W}{W}, {T}, Sacrifice a Desert: Creatures you control get +1/+1
///   until end of turn. Activate only as a sorcery.</b> — a sorcery-speed
///   (<see cref="ActivatedAbility.IsSorcerySpeed"/>, CR 117.1a)
///   <see cref="ActivatedAbility"/> with {2}{W}{W} + {T} costs. The
///   "Sacrifice a Desert" cost is performed inside the effect closure
///   (sacrificing this land — itself a Desert — the only Desert the closure
///   has a handle to; same resolve-time sacrifice posture as
///   <see cref="BarbarianRingFactory"/> / Cephalid Coliseum, where the generic
///   <see cref="AdditionalCost.Sacrifice"/> stub is bypassed). On resolution
///   it snapshots the controller's creatures and pumps each +1/+1 until EOT.
///
/// ## Deferred (v1 gaps — shared with the sac-land chassis)
/// - <b>"Sacrifice a Desert" choice</b>: the player normally chooses WHICH
///   Desert to sacrifice (CR 601.2 cost choice). v1 always sacrifices Shefet
///   Dunes itself (always a legal choice — it's a Desert and on the
///   battlefield), matching the Barbarian Ring / Cephalid Coliseum
///   sacrifice-self-as-cost posture. A future cost-choice surface can let the
///   controller pick a different Desert.
/// - <b>Sorcery-speed enforcement</b>: <see cref="ActivatedAbility.IsSorcerySpeed"/>
///   is set; <see cref="Rules.ActionValidator"/> owns the actual main-phase /
///   empty-stack rejection (CR 117.1a). The flag is the data; the validator is
///   the gate.
/// - <b>Pump targets the supplied effects service</b>: the anthem registers
///   each pump into the <see cref="ContinuousEffectsService"/> supplied to
///   <see cref="Create(Player, ContinuousEffectsService?)"/>, not per-creature
///   <see cref="Creature.ActiveEffects"/> — same shared-service posture as the
///   Restless cycle. The shape-only single-arg path no-ops the pump.
/// </summary>
[CardName("Shefet Dunes")]
public static class ShefetDunesFactory
{
    public const string CardName = "Shefet Dunes";
    public const string Slug = "shefet-dunes";

    /// <summary>{2}{W}{W} — the anthem's mana cost.</summary>
    public const string AnthemCost = "{2}{W}{W}";

    /// <summary>The anthem's +P/+T amount (CR 613.7c).</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    /// <summary>
    /// Construct Shefet Dunes with no <see cref="ContinuousEffectsService"/>
    /// wired (shape-only path — the anthem's pumps no-op but the sacrifice
    /// still happens). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Shefet Dunes with an optional
    /// <see cref="ContinuousEffectsService"/> for the anthem's +1/+1 pumps.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the anthem registers
    /// each +1/+1 <see cref="PumpUntilEndOfTurnEffect"/> into. May be null —
    /// the ability still resolves (and still sacrifices the land) but no pump
    /// is recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // Desert subtype, {T}: Add {C} mana ability). The pay-life white mana
        // ability and the sorcery-speed anthem are layered on below — neither
        // is expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}, Pay 1 life: Add {W}.  (CR 605.1 — mana ability, no stack.)
        // Painland / Horizon-Canopy shape: canActivateCheck gates on
        // !IsTapped AND a life floor (CR 119.4 — can't pay a life cost you
        // can't afford; LifeTotal must exceed 1). additionalCostPayer pays
        // 1 life (CR 118.4) after the {T} tap.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("W"),
            canActivateCheck: () => !land.IsTapped && (land.Controller ?? owner).LifeTotal > 1,
            additionalCostPayer: p => p.LoseLife(1)));

        // ----------------------------------------------------------------
        // {2}{W}{W}, {T}, Sacrifice a Desert:
        //   Creatures you control get +1/+1 until end of turn.
        //   Activate only as a sorcery.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {2}{W}{W} + {T}; the "Sacrifice a Desert" cost is performed inside
        // the effect closure (sacrificing this land — a Desert — itself,
        // mirroring Barbarian Ring / Cephalid Coliseum, since the generic
        // AdditionalCost.Sacrifice payment is a no-op stub for self-sac).
        // sorcerySpeed: true wires the "Activate only as a sorcery" rider
        // (CR 117.1a) — ActionValidator enforces the main-phase / empty-stack
        // timing.
        //
        // NON-TARGETED anthem (CR 611 — a one-shot pump, not a continuous
        // static). On resolution it snapshots the controller's battlefield
        // creatures at that moment (CR 608.2) to a list first (so the same-step
        // sacrifice zone move can't disturb the enumeration), then registers a
        // +1/+1 PumpUntilEndOfTurnEffect (CR 613.7c, expires EOT per CR 514.2)
        // on each.
        // ----------------------------------------------------------------
        var anthemEffect = new Effect(
            $"{CardName}: sacrifice a Desert + creatures you control get +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                var controller = land.Controller ?? owner;

                // Snapshot the creatures BEFORE the sacrifice so ordering is
                // irrelevant (the sac'd land is never a creature, so it's not
                // in this set regardless).
                var creatures = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();

                // "Sacrifice a Desert" — sacrifice this land (itself a Desert).
                // CR 701.16 — battlefield → owner's graveyard.
                SacrificeSelf(land, owner);

                if (effects == null) return; // shape-only path — no pump recorded

                foreach (var creature in creatures)
                {
                    // CR 613.7c — +1/+1 with CR 514.2 end-of-turn expiry.
                    effects.Register(new PumpUntilEndOfTurnEffect(
                        creature, PumpPower, PumpToughness));
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(AnthemCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { anthemEffect },
            sorcerySpeed: true)); // CR 117.1a — "Activate only as a sorcery."

        return land;
    }

    /// <summary>
    /// Move <paramref name="land"/> from the battlefield to its owner's
    /// graveyard (the "Sacrifice a Desert" cost — CR 701.16). Idempotent —
    /// no-op if already off the battlefield. Mirrors the closure used by
    /// <see cref="BarbarianRingFactory"/> / Cephalid Coliseum.
    /// </summary>
    private static void SacrificeSelf(Land land, Player owner)
    {
        if (land.Zone != ZoneType.Battlefield) return;
        var holder = land.Controller ?? owner;
        holder.Zones.Battlefield.RemoveCard(land);
        owner.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
    }
}
