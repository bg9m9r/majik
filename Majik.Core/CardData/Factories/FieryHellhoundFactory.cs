using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fiery Hellhound (M10 and reprints).
///
/// Creature — Elemental Dog, mana cost {1}{R}{R}, 2/2.
/// Oracle text:
///   "{R}: This creature gets +1/+0 until end of turn."
///
/// ## Implemented (v1)
/// - Card identity: 2/2 Creature — Elemental Dog, mana cost {1}{R}{R}, mana value 3.
/// - <b>{R}: +1/+0 until end of turn</b> (firebreathing, CR 602 /
///   CR 613.1f Layer 7c). Wired as an <see cref="ActivatedAbility"/> with
///   a single <see cref="ManaCostCost"/> of <c>{R}</c> and no target
///   declarations — the ability pumps Fiery Hellhound itself. On resolution
///   the effect registers a <see cref="PumpUntilEndOfTurnEffect"/>(+1, 0)
///   against Fiery Hellhound's <see cref="Creature.ActiveEffects"/>. When
///   <c>ActiveEffects</c> is null (shape-only test path) the effect
///   silently no-ops — same posture as
///   <see cref="WallOfFireFactory"/>'s {R}: +1/+0 ability.
/// - <b>Repeatable</b>: no once-per-turn restriction is printed. Each {R}
///   payment stacks an additional +1/+0 for the turn (CR 613.1f — multiple
///   Layer 7c modifications stack additively for the duration of the turn).
/// - <b>No Defender</b>: unlike Wall of Fire, Fiery Hellhound is a normal
///   attacker/blocker with no keyword restrictions.
///
/// ## Source-of-truth — Layer 7c
/// Power increase is a Layer 7c modification (CR 613.1f). To observe it,
/// the creature must have a <see cref="ContinuousEffectsService"/> wired into
/// <see cref="Creature.ActiveEffects"/>. Without the service the printed
/// 2/2 surfaces unmodified. The effect expires at end of turn via
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mana cost payment</b>: the cost gate (CanPay on the
///   player's mana pool) is enforced at activation time by the
///   <see cref="Majik.Core.Services.AbilityActivator"/>'s cost-validation
///   path; the factory does not wire an <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   prompt. Same posture as <see cref="WallOfFireFactory"/>'s {R}: +1/+0 ability.
/// </summary>
[CardName("Fiery Hellhound")]
public static class FieryHellhoundFactory
{
    public const string CardName = "Fiery Hellhound";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const string FirebreathingCost = "{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Fiery Hellhound. The {R}: +1/+0 EOT activated ability is
    /// always attached. The pump effect is a silent no-op for callers that
    /// do not wire a <see cref="ContinuousEffectsService"/> into
    /// <see cref="Creature.ActiveEffects"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Dog });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {R}: Fiery Hellhound gets +1/+0 until end of turn. (CR 602)
        //
        // Plain activated ability (not a mana ability — produces no mana;
        // uses the stack per CR 605.1 / CR 602.2). The cost is one red
        // mana (ManaCostCost("{R}")). No target: the ability modifies Fiery
        // Hellhound itself. On resolution a PumpUntilEndOfTurnEffect(+1, 0)
        // is registered against card.ActiveEffects (Layer 7c). Multiple
        // activations stack: each {R} paid registers an independent +1/+0
        // for the turn (CR 613.1f). No sorcery-speed restriction printed
        // — instant speed (CR 602.5a default).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +1/+0 until end of turn ({{R}} firebreathing)",
            () =>
            {
                // CR 613.1f Layer 7c — power modification. null ActiveEffects
                // = shape-only test path; pump silently no-ops (same posture
                // as WallOfFireFactory's {R}: +1/+0 ability).
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, 1, 0));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(FirebreathingCost) },
            effects: new IEffect[] { pumpEffect }));

        return card;
    }
}
