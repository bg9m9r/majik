using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vault of the Archangel (Dark Ascension).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    {2}{W}{B}, {T}: Creatures you control gain deathtouch and lifelink
///    until end of turn."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed supertypes/subtypes — Vault of the
///   Archangel is just a "Land", no basic-land subtype). Same shape as the
///   colourless-utility-land analogue
///   <see cref="KarnsBastionFactory"/> ({T}: Add {C} + a colour-costed
///   activated ability) and the manland cycle's
///   <see cref="CelestialColonnadeFactory"/>.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack). {C} buckets as Generic +1 via <see cref="ManaCost.Parse"/>,
///   same as Karn's Bastion / Mutavault.
/// - <b>{2}{W}{B}, {T}: Creatures you control gain deathtouch and lifelink
///   until end of turn</b> — an ordinary <see cref="ActivatedAbility"/>
///   (CR 602, uses the stack). Cost stack:
///   <see cref="ManaCostCost"/>("{2}{W}{B}") + <see cref="AdditionalCost.Tap"/>.
///   On resolution it walks the controller's battlefield and, for every
///   <see cref="Creature"/> the controller controls, registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> for "Deathtouch"
///   (CR 702.2) and one for "Lifelink" (CR 702.15) against that creature's
///   own <see cref="Permanent.ActiveEffects"/> layer service (CR 613.1c,
///   Layer 6 — ability addition). Both grants expire in the cleanup step
///   (CR 514.2) via <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>.
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasLifelink"/> read the
///   granted keyword off the creature's computed characteristics, so the
///   grant is honoured in combat. This mirrors the shared
///   "creatures you control gain &lt;keyword&gt; until end of turn"
///   primitive used by
///   <see cref="SpellTemplates.Templates.Counters.CountersSpellFactory"/>.
///
/// ## v1 simplifications
/// - <b>Snapshot at resolution</b>: the set of "creatures you control" is
///   read once when the ability resolves (CR 611.2c — a one-shot grant);
///   creatures that enter later this turn are not retroactively granted,
///   matching the printed wording.
/// - Creatures whose <see cref="Permanent.ActiveEffects"/> service is not
///   wired are skipped (shape-only path); in a live game every battlefield
///   permanent carries a layer service, so the grant applies.
/// </summary>
[CardName("Vault of the Archangel")]
public static class VaultOfTheArchangelFactory
{
    public const string CardName = "Vault of the Archangel";
    public const string GrantCost = "{2}{W}{B}";

    /// <summary>
    /// Construct Vault of the Archangel as a plain Land. The mana ability
    /// and the {2}{W}{B}, {T} deathtouch/lifelink grant are both attached;
    /// the grant's resolution registers the until-end-of-turn keyword
    /// effects against each controlled creature's own layer service.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {2}{W}{B}, {T}: Creatures you control gain deathtouch and
        // lifelink until end of turn.
        // CR 602 — ordinary activated ability (uses the stack); cost is
        // {2}{W}{B} mana plus the tap symbol. Resolution grants Deathtouch
        // (CR 702.2) + Lifelink (CR 702.15) to every creature the
        // controller controls, expiring at cleanup (CR 514.2 / CR 613
        // Layer 6).
        // ----------------------------------------------------------------
        var grantEffect = new Effect(
            $"{CardName}: creatures you control gain deathtouch and lifelink EOT",
            () =>
            {
                foreach (var creature in owner.Zones.Battlefield
                             .GetCards()
                             .OfType<Creature>())
                {
                    // Only the activating player's creatures (CR — "you
                    // control"); the controller's battlefield zone already
                    // scopes this.
                    if (creature.ActiveEffects == null) continue;

                    creature.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(creature, "Deathtouch"));
                    creature.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(creature, "Lifelink"));
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(GrantCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { grantEffect }));

        return land;
    }
}
