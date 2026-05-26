using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grand Abolisher (Magic 2012, {W}{W}).
///
/// Creature — Human Cleric 2/2. Oracle text:
///   "During your turn, your opponents can't cast spells or activate
///    abilities of artifacts, creatures, or enchantments."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Cleric at {W}{W}, owner / controller wired.
///
/// ## Deferred (v1 gaps)
/// - <b>"Opponents can't cast spells" during controller's turn</b>
///   (CR 117.1a / CR 601.3): no "total cast block, gated on active-player
///   == controller" primitive exists yet.
///   <see cref="Majik.Core.Rules.CastingRestrictions"/> currently models
///   sorcery-speed (Teferi, Time Raveler), cast-from-hand-only (Drannith
///   Magistrate), turn-scoped no-noncreature (Ranger-Captain of Eos), and
///   named-card / per-player named-card blocks (Meddling Mage / Reflector
///   Mage). None of those cover "no spells at all this player's turn."
///   When that primitive lands, this factory should pair it with a
///   <see cref="Majik.Core.Events.TurnStartedEvent"/>-driven lifecycle
///   that registers on the controller's turn and unregisters at end-of-
///   turn.
/// - <b>Activated-ability block scoped to artifact / creature / enchantment
///   sources during controller's turn</b> (CR 602.5c): the predicate-driven
///   suppression primitive
///   (<see cref="Majik.Core.Rules.ActivatedAbilityRestrictions.AddPredicateRestriction"/>)
///   already covers the "filter by source type + opponent control" shape
///   that <see cref="OpponentArtifactActivatedSuppressionEffect"/> uses for
///   Karn the Great Creator, but the active-player gating is the missing
///   piece — without it, wiring the predicate would over-suppress (block
///   on opponents' turns too). Deferring the wire until the
///   <see cref="TurnStartedEvent"/>-driven controller-turn gate lands,
///   shipped together with the cast-block gap above.
///
/// Until both gaps close, Grand Abolisher is a vanilla 2/2 Human Cleric in
/// the engine's eyes. The card shape, dispatch routing, and identity all
/// land via this factory so the seed flips <c>IsImplemented</c> and follow-
/// up PRs can layer the printed static on top without touching consumers.
/// </summary>
[CardName("Grand Abolisher")]
public static class GrandAbolisherFactory
{
    public const string CardName = "Grand Abolisher";
    public const string PrintedManaCost = "{W}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Grand Abolisher. The printed static is deferred (see
    /// class xmldoc) — this returns a vanilla 2/2 with correct identity.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
