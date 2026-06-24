using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunset Strikemaster (Outlaws of Thunder Junction,
/// {1}{R}).
///
/// Creature — Human Monk 3/1. Oracle text (verified against the embedded
/// Modern seed):
///   "{T}: Add {R}.
///    {2}{R}, {T}, Sacrifice this creature: It deals 6 damage to target
///    creature with flying."
///
/// ## Shape source
/// The full card — identity (3/1 Human Monk at {1}{R}) plus BOTH abilities —
/// is loaded from <c>Majik.Core/CardData/Cards/sunset-strikemaster.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. No imperative ability attachment is
/// needed: every shape the card uses is already modelled by the JSON schema.
///
/// ## Implemented (v1)
/// - <b>{T}: Add {R}</b> — the JSON mana ability
///   (<c>{ "kind": "mana", "produces": "R" }</c>) materializes as a
///   <see cref="Abilities.ManaAbility"/> that taps the Strikemaster (its {T}
///   cost) and adds one {R} (CR 605.1). Summoning sickness (CR 302.6) gates
///   activation at the engine level.
/// - <b>{2}{R}, {T}, Sacrifice this creature: It deals 6 damage to target
///   creature with flying.</b> — an activated ability (CR 602.1) whose
///   composite cost is the {2}{R} mana cost (<c>mana</c>), the {T} tap
///   (<c>tap_self</c>), and sacrificing itself (<c>sacrifice_self</c>,
///   CR 701.16). Both tap costs ({T} on the mana ability and {T} here) compete
///   for the single tap — the engine enforces that a tapped creature cannot
///   pay a second {T} (CR 602.2a). On resolution it deals 6 damage
///   (<c>deal_damage</c>) to the chosen target via <c>Fx.DealDamageAny</c>.
/// - <b>"target creature with flying"</b> — the <c>creature_with_flying</c>
///   <see cref="TargetFilters"/> filter (added alongside this card): a
///   battlefield creature whose effective keywords include Flying
///   (CR 702.9), checked through the canonical
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> predicate so a
///   creature that gained flying via a continuous effect counts. CR 608.2b — an
///   illegal pick (a non-flyer or a target that has left the battlefield)
///   fizzles cleanly when the candidate pool is constrained.
///
/// ## Deferred (matches the shared declarative damage posture)
/// - <b>Resolution-time filter re-check</b>: <c>BuildDealDamageEffect</c> routes
///   damage to whatever ChosenTargets carries (the same posture every
///   <c>deal_damage</c> JSON card uses); the "with flying" constraint is enforced
///   at targeting via the candidate gatherer, not re-applied at resolve.
/// </summary>
[CardName("Sunset Strikemaster")]
public static class SunsetStrikemasterFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sunset-strikemaster");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
