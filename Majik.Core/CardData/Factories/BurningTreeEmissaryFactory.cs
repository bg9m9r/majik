using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burning-Tree Emissary (Dissension / Modern
/// Horizons 2, <c>{R/G}{R/G}</c>).
///
/// Creature — Human Shaman 2/2. Oracle text:
///   "When this creature enters, add {R}{G}."
///
/// ## Implemented (v1)
/// - 2/2 Human Shaman with hybrid mana cost <c>{R/G}{R/G}</c> — CR 107.4e
///   hybrid pips parsed by <see cref="ManaCost.Parse"/> into two
///   <see cref="HybridPip"/>(Red, Green) entries (no generic bucket).
///   Total mana value = 2. (Same hybrid-cost wiring as
///   <see cref="ManamorphoseFactory"/>.)
/// - ETB <see cref="TriggeredAbility"/> (CR 603.6a) attached via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution the
///   controller's mana pool gains <c>{R}{G}</c> via
///   <see cref="Player.AddManaToPool"/> (CR 106.4 — mana goes into the
///   pool; CR 605.1 does NOT apply because this is a normal triggered
///   ability, not a mana ability — Burning-Tree's ETB does use the stack
///   per CR 605.1a, since its effect "could not be a mana ability" only
///   if it satisfies all three CR 605.1a clauses; the ETB-on-self trigger
///   is technically a mana ability per CR 605.1b, but the engine routes
///   it through the normal trigger / resolve path because we have no
///   mana-ability-trigger primitive yet. See deferred note below.).
/// - Mirrors the
///   <see cref="OmnathLocusOfCreationFactory"/> second-landfall mana
///   deposit pattern (<c>owner.AddManaToPool(ManaCost.Parse(...))</c>).
///
/// ## Deferred (v1 gaps)
/// - <b>Mana-ability ETB classification</b> (CR 605.1b): strictly,
///   Burning-Tree's ETB trigger is itself a mana ability (no target, no
///   loyalty change, produces mana). In a faithful implementation it
///   would NOT use the stack and would resolve as an intervening "add
///   mana" the moment Burning-Tree enters. The engine has no
///   triggered-mana-ability primitive today, so v1 routes it through
///   normal <see cref="TriggeredAbility"/> resolution — observationally
///   identical for the cascade-of-2-drops "free Emissary chain" once it
///   resolves; differs only if a player could respond to the trigger
///   (which Burning-Tree intentionally disallows). Same posture as the
///   Llanowar Elves / Birds of Paradise activated mana abilities — the
///   data side ships mana production; the stack-bypass refinement waits
///   on a triggered-mana-ability binder.
/// </summary>
[CardName("Burning-Tree Emissary")]
public static class BurningTreeEmissaryFactory
{
    public const string CardName = "Burning-Tree Emissary";
    public const string PrintedManaCost = "{R/G}{R/G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// "Add {R}{G}" — the printed mana the ETB trigger deposits into the
    /// controller's pool. Kept as a single parseable string so the
    /// hybrid-cost-friendly <see cref="ManaCost.Parse"/> path handles the
    /// deposit without a hand-rolled per-pip switch.
    /// </summary>
    public const string EtbManaProduced = "RG";

    /// <summary>
    /// Construct Burning-Tree Emissary owned and controlled by
    /// <paramref name="owner"/>. The ETB trigger is attached structurally.
    /// Callers that want bus-driven firing register the returned
    /// <see cref="TriggeredAbility"/> with their
    /// <see cref="TriggerManager"/> (same shape as Omnath / Snapcaster /
    /// Subtlety).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 603.6a — ETB triggered ability. No target; effect resolves
        // {R}{G} into the controller's mana pool (CR 106.4).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — add {{R}}{{G}} to controller's mana pool",
            () => owner.AddManaToPool(ManaCost.Parse(EtbManaProduced)));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        return card;
    }
}
