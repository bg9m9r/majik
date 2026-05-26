using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Signal Pest (Mirrodin Besieged, {R}).
///
/// Artifact Creature — Pest 1/1. Oracle text:
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+1 until end of turn.)
///    Signal Pest can't be blocked except by creatures with flying or reach."
///
/// ## Implementation
///
/// - 1/1 Pest with the Artifact card type, mana cost {R}.
/// - <b>Can't be blocked except by flying or reach (CR 509.1b)</b>: registered
///   as a <see cref="CantBeBlockedExceptByEffect"/> on the supplied
///   <see cref="ContinuousEffectsService"/>. The predicate accepts a blocker
///   iff it has Flying or Reach — both querying through the layer system via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>. <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/>
///   walks the attacker's <see cref="Creature.ActiveEffects"/> and rejects
///   any blocker the predicate excludes.
/// - <b>Battle cry (CR 702.92)</b>: not yet wired. The keyword marker is
///   attached so <c>ICard.Abilities</c> still reflects the printed line; the
///   per-attacker +1/+1 pump is a future PR.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Block restriction is
///   NOT wired (no effects service). Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — fully wired
///   block restriction. Effect is registered on construction and the
///   service is bound onto <see cref="Creature.ActiveEffects"/> so
///   <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> picks it up.
/// </summary>
[CardName("Signal Pest")]
public static class SignalPestFactory
{
    public const string CardName = "Signal Pest";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Signal Pest with no live wiring. Suitable for dispatcher /
    /// shape tests. The block-restriction is NOT registered — use the
    /// effects-aware overload to wire it.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Signal Pest with optional runtime services. Registers the
    /// "can't be blocked except by flying or reach" restriction on
    /// <paramref name="effects"/> when supplied (also binding it onto
    /// <see cref="Creature.ActiveEffects"/> so the combat validator picks
    /// the restriction up).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Pest });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.92 — Battle cry keyword marker. Per-attacker +1/+1 pump is
        // a future PR; the marker is still attached so ICard.Abilities
        // reflects the printed line and Scryfall keyword parsing matches.
        card.AddAbility(new KeywordAbility("Battle cry", card, owner));

        if (effects != null)
        {
            // Bind the service so BlockLegality / CombatAbilities reads
            // (including the can't-be-blocked-except-by walk) all flow
            // through the same layer pipeline.
            card.ActiveEffects = effects;

            // CR 509.1b — "Signal Pest can't be blocked except by creatures
            // with flying or reach." Predicate is creature-only (non-creature
            // blockers are already disallowed by 509.1a, but we narrow to
            // Creature for the keyword query).
            effects.Register(new CantBeBlockedExceptByEffect(
                source: card,
                predicate: blocker => blocker is Creature c
                    && (Majik.Core.Combat.CombatAbilities.HasFlying(c)
                        || Majik.Core.Combat.CombatAbilities.HasReach(c))));
        }

        return card;
    }
}
