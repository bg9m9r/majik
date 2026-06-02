using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Apparition (Eventide, {W/B}{W/B}{W/B}).
/// Creature — Spirit 2/2.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{W/B}{W/B}{W/B}: This creature gets +3/+3 until end of turn.
///    Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Spirit, mana cost {W/B}{W/B}{W/B} (CR 107.4e hybrid pips —
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> decomposes each
///   <c>{W/B}</c> into a <c>HybridPip</c>, same shape as Kitchen Finks'
///   <c>{G/W}</c> pips). The base 2/2 Spirit body + {W/B}{W/B}{W/B} cost are
///   materialised from the embedded JSON definition
///   (<c>restless-apparition.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>; the activated pump and the
///   Persist keyword are layered on here because the JSON
///   <c>AbilityDefinition</c> schema expresses neither yet (same posture as
///   <see cref="GhituEncampmentFactory"/> / <see cref="KitchenFinksFactory"/>).
///
/// - <b>{W/B}{W/B}{W/B}: This creature gets +3/+3 until end of turn</b> —
///   CR 602 ordinary activated ability (uses the stack). The cost is a single
///   <see cref="ManaCostCost"/> over the three hybrid pips; the activator may
///   pay each pip with white or black mana (CR 601.2f). On resolution the
///   effect registers a <see cref="PumpUntilEndOfTurnEffect"/>(+3/+3) on this
///   creature, flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
///   step) lifts the pump. When no <see cref="ContinuousEffectsService"/> is
///   wired the ability still resolves cleanly — the +3/+3 simply isn't tracked
///   (same shape-only posture as <see cref="BlinkmothNexusFactory"/>'s pump).
///
/// - <b>Persist (CR 702.79)</b> — wired via the shared
///   <see cref="PersistFactory.Build(Creature)"/> primitive (the keyword marker
///   + the Battlefield → Graveyard death trigger with the "no -1/-1 counter"
///   interveningIf gate). Identical wiring to Kitchen Finks / Murderous Redcap.
/// </summary>
[CardName("Restless Apparition")]
public static class RestlessApparitionFactory
{
    public const string CardName = "Restless Apparition";
    public const string Slug = "restless-apparition";
    public const string PumpCost = "{W/B}{W/B}{W/B}";
    public const int PumpAmount = 3;

    /// <summary>
    /// Construct Restless Apparition with no <see cref="ContinuousEffectsService"/>
    /// wired. The activated pump + Persist trigger are attached so the card
    /// surface is complete; the pump's layer effect is not registered (the
    /// ability still resolves, the +3/+3 simply isn't tracked). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Restless Apparition with an optional continuous-effects service
    /// for Layer-7c registration of the +3/+3 pump.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the activated pump
    /// registers its <see cref="PumpUntilEndOfTurnEffect"/> with. May be null —
    /// the ability still resolves but the +3/+3 is not recorded.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature — Spirit
        // 2/2, {W/B}{W/B}{W/B} mana cost). The activated pump + Persist are
        // layered on below — neither is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {W/B}{W/B}{W/B}: This creature gets +3/+3 until end of turn.
        //
        // CR 602 — ordinary activated ability (uses the stack). Resolution
        // registers a Layer-7c +3/+3 continuous effect flagged
        // ExpiresAtEndOfTurn (CR 514.2 cleanup). Self-targeted — the pump
        // applies to this creature, no target selection (CR 602.2).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: this creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path
                effects.Register(new PumpUntilEndOfTurnEffect(card, PumpAmount, PumpAmount));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpCost) },
            effects: new IEffect[] { pumpEffect }));

        // ----------------------------------------------------------------
        // Persist (CR 702.79) — keyword marker + death trigger, all from
        // the shared primitive. Call TriggerManager.BindCard on the returned
        // creature to register it with the live trigger manager.
        // ----------------------------------------------------------------
        PersistFactory.Build(card);

        return card;
    }
}
