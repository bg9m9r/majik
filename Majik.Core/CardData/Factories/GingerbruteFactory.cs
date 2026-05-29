using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gingerbrute (Throne of Eldraine, {1}).
///
/// Artifact Creature — Food Golem 1/1. Oracle text (verified against
/// Scryfall):
///   "Haste (This creature can attack and {T} as soon as it comes under
///    your control.)
///    {1}: This creature can't be blocked this turn except by creatures
///    with haste.
///    {2}, {T}, Sacrifice this creature: You gain 3 life."
///
/// The card's base shape (name, Artifact + Creature types, Food + Golem
/// subtypes, {1}, 1/1) AND the sacrifice-for-life activated ability are
/// materialised from the embedded JSON definition (<c>gingerbrute.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <see cref="ActivatedAbilityDefinition"/> schema already expresses
/// <c>{2}</c> mana + <c>{T}</c> + sacrifice-self costs and the
/// <c>gain_life_self</c> effect, so that ability needs no hand-rolled C#.
///
/// Two printed behaviours outgrow the JSON schema and are layered on here:
/// <list type="bullet">
///   <item><b>Haste (CR 702.10)</b> — wired as a
///   <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> surfaces it.
///   Same posture as the Flying marker on
///   <see cref="StormscaleScionFactory"/>.</item>
///   <item><b>{1}: can't be blocked this turn except by creatures with
///   haste (CR 509.1b)</b> — a one-cost ({1} mana, no tap) activated
///   ability whose resolution registers an end-of-turn-expiring
///   <see cref="CantBeBlockedExceptByEffect"/> against the supplied
///   <see cref="ContinuousEffectsService"/>. The predicate admits only
///   creature blockers with Haste. Identical block-restriction shape to
///   <see cref="SignalPestFactory"/>'s "flying or reach" rider, except
///   Gingerbrute's is granted by an activated ability and is bounded to
///   the turn (CR 514.2) rather than being a permanent static.</item>
/// </list>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. The Haste marker and
///   the sacrifice ability (from JSON) are attached; the {1} evasion
///   ability is attached too, but its resolution no-ops without a
///   continuous-effects service (no restriction registered). Suitable for
///   shape / dispatcher tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — fully wired.
///   The {1} evasion ability resolves into a real EOT block restriction on
///   the supplied service (also bound onto
///   <see cref="Creature.ActiveEffects"/> so
///   <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> reads it).
///
/// ## Deferred (v1 gaps)
/// - <b>Repeated {1} activation</b>: re-activating registers an additional
///   identical restriction. Multiple <see cref="CantBeBlockedExceptByEffect"/>
///   intersect (every active predicate must allow the blocker — CR 509.1b),
///   and they're all the same haste predicate, so the observable outcome is
///   unchanged; the redundant registration is harmless and cleared at EOT.
/// </summary>
[CardName("Gingerbrute")]
public static class GingerbruteFactory
{
    public const string CardName = "Gingerbrute";
    public const string Slug = "gingerbrute";

    /// <summary>CR 509.1b — printed activation cost of the evasion ability.</summary>
    public const string EvasionCost = "{1}";

    /// <summary>
    /// Construct Gingerbrute with no live block-restriction wiring. The
    /// Haste marker, the {1} evasion ability, and the JSON-built sacrifice
    /// ability are all attached; activating the {1} ability no-ops (no
    /// restriction registered) without a continuous-effects service.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Gingerbrute with an optional
    /// <see cref="ContinuousEffectsService"/>. When supplied, the {1}
    /// evasion ability resolves into an EOT-expiring "can't be blocked
    /// except by haste" restriction (CR 509.1b / 514.2) on the service,
    /// which is also bound onto <see cref="Creature.ActiveEffects"/> so the
    /// combat validator reads it.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Artifact Creature — Food Golem 1/1, {1}) AND the
        // "{2}, {T}, Sacrifice this creature: You gain 3 life." activated
        // ability are materialised from the embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.10 — Haste. KeywordAbility marker so CombatAbilities
        // surfaces the attack / tap-as-soon-as-it-enters property.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        if (effects != null)
        {
            // Bind the service so BlockLegality reads through the same
            // layer pipeline (mirrors SignalPestFactory).
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // {1}: This creature can't be blocked this turn except by creatures
        // with haste. CR 602.1 — activated ability; CR 509.1b — block
        // restriction; CR 514.2 — "this turn" lifts in the cleanup step.
        // The restriction is registered at resolution against the supplied
        // continuous-effects service; without one the resolution no-ops
        // (shape-only path).
        // ----------------------------------------------------------------
        var evasionEffect = new Effect(
            $"{CardName}: can't be blocked this turn except by creatures with haste (CR 509.1b)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new HasteOnlyBlockableUntilEndOfTurnEffect(card));
            });

        var evasionAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ManaCost.Parse(EvasionCost)) },
            effects: new IEffect[] { evasionEffect });

        card.AddAbility(evasionAbility);

        return card;
    }

    /// <summary>
    /// CR 509.1b / 514.2 — "can't be blocked this turn except by creatures
    /// with haste" restriction that expires at end of turn. A would-be
    /// blocker is legal iff it is a <see cref="Creature"/> with Haste
    /// (queried through the layer system via
    /// <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>). Subclasses
    /// <see cref="CantBeBlockedExceptByEffect"/> solely to flip
    /// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> on, so
    /// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> drops it in
    /// the cleanup step (the printed "this turn" duration). The base
    /// CantBeBlockedExceptByEffect is a permanent static; Gingerbrute's is
    /// turn-bounded.
    /// </summary>
    private sealed class HasteOnlyBlockableUntilEndOfTurnEffect : CantBeBlockedExceptByEffect
    {
        public HasteOnlyBlockableUntilEndOfTurnEffect(ICard source)
            : base(source, predicate: blocker =>
                blocker is Creature c && Majik.Core.Combat.CombatAbilities.HasHaste(c))
        {
        }

        public override bool ExpiresAtEndOfTurn => true;
    }
}
