using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cabal Ritual (Torment / Modern Horizons 2,
/// {B}).
///
/// Instant. Oracle text:
///   "Add {B}{B}{B}.
///    Threshold — Add {C}{C}{C}{C}{C} instead if seven or more cards
///    are in your graveyard."
///
/// ## Implementation
///
/// CR 702.50 — <b>Threshold</b>. Threshold is a state-based check on
/// the controller's graveyard at the moment the spell resolves. If the
/// controller has seven or more cards in their graveyard, the threshold
/// clause replaces the base effect: add five colourless mana instead of
/// three black. The "instead" wording (CR 702.50b) is a one-or-the-
/// other gate, not an additive bonus.
///
/// Note that the spell itself isn't in the graveyard yet when its
/// effect resolves — it's on the stack. Threshold is evaluated against
/// the controller's graveyard as the effect resolves, before the spell
/// moves to the graveyard as part of CR 608.2m. So a Cabal Ritual cast
/// with exactly six cards already in the graveyard does NOT meet
/// threshold; it needs seven existing graveyard cards.
///
/// Mana goes into <see cref="Player.ManaPool"/> via
/// <see cref="Player.AddManaToPool(ManaCost)"/>. The pool follows
/// CR 106.4 / CR 500.4 — produced mana lives until the end of the
/// current step/phase. {C} parses into the generic bucket per
/// <see cref="ManaCost.Parse(string)"/> (CR 107.4c); this is consistent
/// with how other colourless producers route through ManaPool today.
///
/// Card-shape only here; the resolve-time effect is built on-demand
/// via <see cref="BuildResolveEffect"/> so tests / integrations can
/// plug it into a <see cref="Majik.Core.Game.SpellDefinition"/> or
/// pass it directly to a <see cref="Majik.Core.Spells.Spell"/>.
/// </summary>
[CardName("Cabal Ritual")]
public static class CabalRitualFactory
{
    public const string CardName = "Cabal Ritual";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// CR 702.50 — graveyard-card count required to satisfy threshold.
    /// </summary>
    public const int ThresholdCount = 7;

    /// <summary>
    /// Base output when threshold is NOT active: add three black mana.
    /// </summary>
    public const string BaseManaProduced = "BBB";

    /// <summary>
    /// Output when threshold IS active: add five colourless mana
    /// ({C}{C}{C}{C}{C} → five generic via ManaCost.Parse).
    /// </summary>
    public const string ThresholdManaProduced = "CCCCC";

    /// <summary>
    /// Build a Cabal Ritual instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildResolveEffect"/> for the
    /// resolve-time mana production.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Cabal Ritual's resolve effect. On resolution, sample the
    /// controller's graveyard size; if ≥ <see cref="ThresholdCount"/>,
    /// add five colourless mana, otherwise add three black mana
    /// (CR 702.50).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new IEffect[]
        {
            new Effect("Cabal Ritual: add {B}{B}{B} (or {C}{C}{C}{C}{C} with threshold).", () =>
            {
                var produced = IsThresholdActive(controller)
                    ? ManaCost.Parse(ThresholdManaProduced)
                    : ManaCost.Parse(BaseManaProduced);
                controller.AddManaToPool(produced);
            }),
        };
    }

    /// <summary>
    /// CR 702.50 — true iff the controller has seven or more cards in
    /// their graveyard at the moment of the check.
    /// </summary>
    public static bool IsThresholdActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards().Count() >= ThresholdCount;
    }
}
