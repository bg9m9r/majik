using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conifer Wurm (Modern Horizons 3, {4}{G}).
///
/// Snow Creature — Wurm 4/4. Oracle text (verified Scryfall 2026-06-14):
///   "Trample
///    {3}{G}: This creature gets +X/+X until end of turn, where X is the
///    number of snow permanents you control."
///
/// ## Implemented (v1)
/// - <b>Identity</b> — 4/4 Snow Creature — Wurm, mana cost {4}{G}, green.
///   Base shape (name, Creature type, Snow supertype CR 205.4d, Wurm subtype,
///   P/T, mana cost) + the <b>Trample</b> keyword marker (CR 702.19) are all
///   materialised from the embedded JSON definition
///   (<c>conifer-wurm.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="LairOfTheHydraFactory"/>. The JSON keyword line becomes a
///   plain <see cref="KeywordAbility"/> marker (CardDefRuntime keyword path).
/// - <b>{3}{G}: +X/+X until end of turn, X = snow permanents you control.</b>
///   Wired as an <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>
///   of <c>{3}{G}</c> (CR 602 — ordinary activated ability, uses the stack,
///   no tap). At resolution (CR 608.2) the effect samples X = the number of
///   snow permanents the controller currently controls — every battlefield
///   permanent carrying the <see cref="CardSupertype.Snow"/> supertype,
///   regardless of card type (CR 205.4d). The Wurm itself IS a snow permanent
///   and the oracle says "snow permanents you control" (NOT "other"), so the
///   Wurm counts itself toward X when it is on the battlefield — this is the
///   key difference from <see cref="IceFangCoatlFactory"/> (whose deathtouch
///   clause reads "OTHER snow permanents"). The count reuses
///   <see cref="SkredFactory.CountSnowPermanents"/> — same snow-supertype scan
///   Skred uses for its N-damage read.
///   The pump is applied as a <see cref="PumpUntilEndOfTurnEffect"/>(X, X)
///   registered on the bound <see cref="ContinuousEffectsService"/>
///   (CR 613.1c Layer 7c, end-of-turn cleanup per CR 514.2).
///
/// ## Notes
/// - The pump effect lambda reads <c>card.Controller</c> live at resolution
///   so a control-change effect counts the new controller's snow permanents
///   (same posture as <see cref="CastleEmberethFactory"/>).
/// - Shape-only path (no <see cref="ContinuousEffectsService"/> wired):
///   <see cref="Creature.ActiveEffects"/> is left null and the pump body
///   silently no-ops rather than NRE'ing — mirrors
///   <see cref="CastleEmberethFactory"/>'s shape-only safety. The ability is
///   still attached so the card surface is complete.
/// </summary>
[CardName("Conifer Wurm")]
public static class ConiferWurmFactory
{
    public const string CardName = "Conifer Wurm";
    public const string Slug = "conifer-wurm";

    /// <summary>Printed activation cost of the self-pump ability.</summary>
    public const string PumpActivationCost = "{3}{G}";

    /// <summary>
    /// Construct Conifer Wurm with no <see cref="ContinuousEffectsService"/>
    /// wired. The pump ability is attached (so the card surface is complete)
    /// but its body silently no-ops on execution because
    /// <see cref="Creature.ActiveEffects"/> is null. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Conifer Wurm.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the Layer 7c
    /// +X/+X pump registration. When supplied it is bound to
    /// <see cref="Creature.ActiveEffects"/> so the pump surfaces on
    /// <see cref="ContinuousEffectsService.Compute"/>. May be null — the
    /// ability still resolves but no pump is recorded (shape-only posture).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: 4/4 Snow Creature —
        // Wurm, {4}{G}, with the Trample keyword marker. The activated
        // self-pump is layered on below — the JSON AbilityDefinition schema
        // does not express a count-driven +X/+X self pump yet.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Bind the continuous-effects service so the Layer 7c pump surfaces on
        // Compute (CR 613). Shape-only callers leave this null; the pump body
        // then no-ops rather than NRE'ing.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // {3}{G}: This creature gets +X/+X until end of turn, where X is the
        // number of snow permanents you control.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost = {3}{G},
        // no tap. At resolution (CR 608.2) X = number of snow permanents the
        // CURRENT controller controls (CR 205.4d — any permanent with the Snow
        // supertype, all card types). The Wurm counts ITSELF (oracle:
        // "snow permanents you control", not "other"). The +X/+X is recorded
        // as a PumpUntilEndOfTurnEffect (CR 613.1c Layer 7c, cleanup CR 514.2).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: gets +X/+X until end of turn, X = snow permanents you control",
            () =>
            {
                // Shape-only safety — without a live ContinuousEffectsService
                // the pump silently no-ops rather than NRE'ing.
                if (card.ActiveEffects == null) return;

                var controller = card.Controller ?? owner;

                // X = snow permanents the controller controls (includes this
                // Wurm — CR 205.4d, oracle reads "snow permanents you control",
                // not "other"). Reuses the Skred snow-supertype scan.
                var x = SkredFactory.CountSnowPermanents(controller);
                if (x <= 0) return; // X = 0 → +0/+0 = clean no-op

                // CR 613.1c Layer 7c — +X/+X until end of turn.
                card.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(card, x, x));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpActivationCost) },
            effects: new IEffect[] { pumpEffect }));

        return card;
    }
}
