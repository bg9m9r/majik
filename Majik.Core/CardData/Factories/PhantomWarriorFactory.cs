using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phantom Warrior (7th Edition and reprints, {1}{U}{U}).
///
/// Creature — Illusion Warrior 2/2. Oracle text:
///   "Phantom Warrior can't be blocked."
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> at {1}{U}{U} with subtypes Illusion, Warrior.
/// - <b>"Phantom Warrior can't be blocked." (CR 509.1c)</b>
///   — registered on the supplied <see cref="ContinuousEffectsService"/>
///   as a non-expiring <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> scoped to this
///   creature. <see cref="CombatValidator"/> consults the restriction
///   directly during the declare-blockers step (Apply is a no-op for
///   restrictions — see <see cref="CombatRestrictionEffect"/>). An
///   "Unblockable" <see cref="KeywordAbility"/> marker is also attached
///   so card-text inspection / keyword scans observe the rider when no
///   live continuous-effects service is wired (shape-only path).
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Can't be blocked" without a continuous-effects service</b>:
///   the shape-only <see cref="Create(Player)"/> path attaches the
///   Unblockable keyword marker but does NOT install the
///   <see cref="CombatRestrictionEffect"/> — <see cref="CombatValidator"/>
///   without an effects service still allows blocks. Production callers
///   thread the live service via the (owner, effects) overload.
/// </summary>
[CardName("Phantom Warrior")]
public static class PhantomWarriorFactory
{
    public const string CardName = "Phantom Warrior";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Phantom Warrior with no continuous-effects service. The
    /// Unblockable keyword marker is attached for card-text inspection but
    /// the live "can't be blocked" combat restriction is NOT registered.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Phantom Warrior with an optional
    /// <see cref="ContinuousEffectsService"/>. When the service is
    /// supplied the "can't be blocked" rider is registered as a
    /// non-expiring <see cref="CombatRestrictionEffect"/> bound to
    /// Phantom Warrior so <see cref="CombatValidator"/> rejects block
    /// declarations targeting it (CR 509.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null —
    /// the unblockable restriction is then skipped (keyword marker
    /// still attached for inspection).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Illusion,
                CardSubtype.Warrior,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Phantom Warrior can't be blocked." — CR 509.1c.
        //
        // Keyword marker covers the card-text / inspection surface; the
        // working combat restriction is registered on the supplied
        // ContinuousEffectsService (no-op on the shape-only path).
        // Restriction does not expire at end of turn — Phantom Warrior is
        // permanently unblockable while on the battlefield. The current
        // CombatRestrictionEffect has no zone-gate (no IsActive override
        // on the sealed type), so once registered the restriction persists
        // for the card's lifetime in the effects service.
        // Producers that need fine-grained zone gating subclass
        // ContinuousEffect directly; v1 of Phantom Warrior ships the
        // simpler shape — the restriction is benign off-battlefield (the
        // CombatValidator only consults it during attack/block declaration
        // on live battlefield creatures).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Unblockable", card, owner));

        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBeBlocked,
            target: card,
            expiresAtEndOfTurn: false));

        return card;
    }
}
