using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crawling Barrens (Zendikar Rising).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {2}{C}: Put two +1/+1 counters on Crawling Barrens.
///    {3}{C}: Until end of turn, Crawling Barrens becomes a 0/0
///    colorless Construct artifact creature with reach. It's still a land."
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack).
/// - <b>{2}{C}: Put two +1/+1 counters on Crawling Barrens</b> — wired as
///   an <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/>. Resolution routes through
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling
///   Season replacements bump the count correctly (CR 614 + CR 121.2).
/// - <b>{3}{C}: Until EOT becomes a 0/0 colorless Construct artifact
///   creature with reach; still a land</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   {3}{C}. Resolution registers a
///   <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — adds Creature +
///   Artifact types, Construct subtype, Reach keyword) and a
///   <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — base 0/0).
///   Both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
///   cleanup (CR 514.2) lifts the animation.
///
/// ## Deferred (v1 gaps)
/// - <b>Colour identity</b>: "colorless" rider is recorded only in the
///   factory effect name; Layer 5 colour-setting isn't yet in the pipe.
///   Same posture as the rest of the manland cycle.
/// - <b>Land-becomes-creature P/T pipeline</b>: same shim posture as
///   <see cref="MutavaultFactory"/> / <see cref="InkmothNexusFactory"/> —
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
///   plain <see cref="PermanentCharacteristics"/> for a Land runtime
///   instance, so the 0/0 + counter math is inspectable on the effect
///   but doesn't surface through Compute. SBA lethal-toughness checks
///   on a 0/0 animated land are deferred until that upgrade lands —
///   real games will rarely activate the animate at 0 counters anyway
///   (the {2}{C} counter pump is the prerequisite).
/// - <b>Summoning sickness</b>: see <see cref="MutavaultFactory"/> notes.
/// </summary>
[CardName("Crawling Barrens")]
public static class CrawlingBarrensFactory
{
    public const string CardName = "Crawling Barrens";
    public const int CounterPumpAmount = 2;
    public const int AnimatedPower = 0;
    public const int AnimatedToughness = 0;

    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct a fully-wired Crawling Barrens.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for animate
    /// registration. May be null — the animate ability still resolves
    /// and pays its mana, but no continuous effect is recorded.</param>
    /// <param name="replacements">ReplacementBus for CountersService.Add
    /// routing of the +1/+1 counter pump. May be null — counters are
    /// applied directly without replacement-bus interaction.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {2}{C}: Put two +1/+1 counters on Crawling Barrens.
        // CR 602 — ordinary activated ability. Routes through
        // CountersService.Add so Hardened Scales / Doubling Season can
        // rewrite the placement (CR 614 + CR 121.2).
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put two +1/+1 counters on self",
            () => CountersService.Add(
                land, CounterType.PlusOnePlusOne, CounterPumpAmount, replacements));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{C}") },
            effects: new IEffect[] { counterEffect }));

        // ----------------------------------------------------------------
        // {3}{C}: Until EOT, becomes a 0/0 colorless Construct artifact
        // creature with reach. It's still a land.
        // Layer 4 adds Creature + Artifact types and Construct subtype
        // (CR 613.1c); Layer 7b records base 0/0 (CR 613.7b). Counters
        // applied by the prior pump survive and modify P/T via the
        // standard CounterCollection bookkeeping.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 0/0 colorless Construct artifact creature with reach until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Reach" },
                    subtypes: new[] { CardSubtype.Construct },
                    extraTypes: new[] { CardType.Artifact }));

                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{C}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
