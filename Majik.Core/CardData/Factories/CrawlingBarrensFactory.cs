using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crawling Barrens (Zendikar Rising).
///
/// Land.
/// Oracle text (exact, Scryfall):
///   "{T}: Add {C}.
///    {4}: Put two +1/+1 counters on this land. Then you may have it become a
///    0/0 Elemental creature until end of turn. It's still a land."
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack).
/// - <b>{4}: Put two +1/+1 counters on this land. Then you may have it become a
///   0/0 Elemental creature until end of turn. It's still a land.</b> — wired as
///   a SINGLE <see cref="ActivatedAbility"/> with a {4}
///   <see cref="ManaCostCost"/> (no {T} cost). Resolution is a two-step compound
///   effect:
///   <list type="number">
///     <item>Counter-accumulation (CR 122, mandatory): place two +1/+1 counters
///       on the land. Counters are permanent objects (CR 121.5) — they
///       accumulate across activations and survive cleanup.</item>
///     <item>Conditional animate (CR 613.1c, "you may"): the controller chooses
///       (<see cref="IPlayerAgent.ChooseYesNoAsync"/>) whether to animate. On
///       "yes", register a <see cref="ManlandCycleAnimateEffect"/> (Layer 4 —
///       adds Creature type + Elemental subtype; printed Land stays) and a
///       <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — base 0/0). The
///       animated body's effective P/T is 0/0 base plus the accumulated +1/+1
///       counters (CR 613.7b + CR 122). Both flagged
///       <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so cleanup
///       (CR 514.2) lifts the animation while the counters persist.</item>
///   </list>
///
/// <para><b>Prod path.</b> Lands are never routed through their [CardName]
/// factory (the factory instance-swap is gated on
/// <c>!shell.HasType(CardType.Land)</c>), so this factory is test-only dispatch.
/// The live counter-accumulate conditional-animate body binds in prod via
/// <see cref="LandActivatedAbilityBinder"/> (recognised by the compound "Put …
/// counters on this land. Then you may have it become …" wording).</para>
///
/// ## Deferred (v1 gaps)
/// - <b>Colour identity</b>: the "colorless" body is the engine default (a
///   plain Elemental creature with no colour); no Layer-5 colour-set needed.
/// </summary>
[CardName("Crawling Barrens")]
public static class CrawlingBarrensFactory
{
    public const string CardName = "Crawling Barrens";
    public const int CounterPumpAmount = 2;
    public const int AnimatedPower = 0;
    public const int AnimatedToughness = 0;

    public static Land Create(Player owner) =>
        Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Crawling Barrens.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for animate
    /// registration. May be null — the ability still resolves and places the
    /// counters, but no animate continuous effect is recorded.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects)
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
        // {4}: Put two +1/+1 counters on this land. Then you may have it
        // become a 0/0 Elemental creature until end of turn. It's still a
        // land. CR 122 counter step (mandatory) + CR 613.1c conditional
        // animate ("you may"). The animated body's P/T = 0/0 base plus the
        // accumulated counters.
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: put two +1/+1 counters on it, then you may have it become a " +
            "0/0 Elemental creature until EOT (still a land)",
            async ctx =>
            {
                Fx.PlaceCounter(land, CounterType.PlusOnePlusOne, CounterPumpAmount);

                if (effects == null) return; // no service wired — counters-only path

                var ctrl = land.Controller ?? owner;
                var agent = ctx.Agent ?? AgentRegistry.Get(ctrl);
                var animate = agent == null
                    || await agent.ChooseYesNoAsync(
                        $"Have {CardName} become a 0/0 Elemental creature until end of turn?",
                        BotIntent.Buff).ConfigureAwait(false);
                if (!animate) return;

                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Elemental },
                    extraTypes: null));
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{4}") },
            effects: new IEffect[] { effect }));

        return land;
    }
}
