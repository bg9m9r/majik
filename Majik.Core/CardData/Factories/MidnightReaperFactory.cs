using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Midnight Reaper (Guilds of Ravnica, {2}{B}).
///
/// Creature — Zombie Knight 3/2. Oracle text (Scryfall, verified):
///   "Whenever a nontoken creature you control dies, this creature deals
///    1 damage to you and you draw a card."
///
/// ## Pure-JSON factory (declarative trigger + effects)
/// Materialised from <c>midnight-reaper.json</c> via the declarative
/// <c>whenever_another_creature_dies</c> trigger (the aristocrat-death mirror
/// of <c>whenever_another_creature_enters</c>) with both the
/// <c>youControlOnly</c> (CR 109.5) and <c>nontokenOnly</c> (CR 111.7) filters
/// set, paired with <c>lose_life_self</c> + <c>draw_card</c> effects. This is
/// the first card to consume the <c>whenever_another_creature_dies</c> trigger
/// shape (the "other-permanent dies" / dies-of-another deferral pay-down).
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
///
/// ## Deferred (v1 gaps)
/// - <b>"deals 1 damage to you" modelled as life loss</b>: the oracle says
///   Midnight Reaper deals 1 DAMAGE to its controller, but the JSON effect
///   schema's untargeted self verb is <c>lose_life_self</c> (CR 119.3 life
///   loss), not self-damage. The two differ only when damage prevention /
///   redirection or "damage dealt to you" payoffs are in play (CR 120 vs
///   119.3) — irrelevant in nearly every game state. Faithful self-damage
///   would need an untargeted <c>deal_damage_self</c> verb; deferred until a
///   card needs the distinction. Net life swing (−1) is exact.
/// </summary>
[CardName("Midnight Reaper")]
public static class MidnightReaperFactory
{
    public const string CardName = "Midnight Reaper";
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "midnight-reaper";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Midnight Reaper with no live <see cref="TriggerManager"/>
    /// wiring (shape / dispatcher tests). The death trigger is attached to the
    /// card but not bus-registered.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Midnight Reaper, registering its
    /// <c>whenever_another_creature_dies</c> trigger with
    /// <paramref name="triggers"/> when supplied so the bus drives it.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        return card;
    }
}
