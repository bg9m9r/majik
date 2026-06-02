using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Azusa, Lost but Seeking (Champions of Kamigawa,
/// {2}{G}).
///
/// Type line: <c>Legendary Creature — Human Monk</c> (1/2).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "You may play two additional lands on each of your turns."
///
/// ## Implementation
///
/// A single controller-scoped, battlefield-gated static permission
/// (CR 305.2 / 720 / 603.6e — a static functions only while its source is
/// on the battlefield). Azusa raises her controller's per-turn land-play
/// cap by 2 while she is on the battlefield.
///
/// The grant is modeled as the integer
/// <see cref="Permanent.AdditionalLandPlaysGranted"/> stamped on the card.
/// <see cref="Majik.Core.Game.LandDropTracker"/> sums this value live over
/// the permanents the active player controls, so:
/// <list type="bullet">
///   <item>the +2 appears the instant Azusa enters and vanishes the instant
///   she leaves (no per-turn re-application — the tracker recomputes on
///   every land-play validation);</item>
///   <item>the bonus is correct every turn (CR 505.5b — the land-play
///   permission resets each turn; the static is independent of the
///   reset);</item>
///   <item>multiple sources stack additively (two Azusas = +4) because the
///   tracker sums every source.</item>
/// </list>
///
/// No <see cref="Majik.Core.Effects.ContinuousEffectsService"/> wiring is
/// needed: the land-play cap is a player/turn quantity, not a
/// characteristic, so it lives outside the Layer system. Stamping the
/// property on the single-argument <see cref="Create(Player)"/> path means
/// the grant is live in real matches too — GameFacade's instance-swap
/// rebuild calls exactly this overload for non-land permanents with a real
/// factory.
///
/// "Lost but Seeking" has no other rules text, so there is nothing else to
/// model (legendary supertype is materialised from the type line).
/// </summary>
[CardName("Azusa, Lost but Seeking")]
public static class AzusaLostButSeekingFactory
{
    public const string CardName = "Azusa, Lost but Seeking";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>CR 720 — Azusa grants two additional land plays each turn.</summary>
    public const int AdditionalLandPlays = 2;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 305.2 / 720 — "You may play two additional lands on each of your
        // turns." Battlefield-gated, controller-scoped, summed live by
        // LandDropTracker.AdditionalLandPlaysFromBattlefield.
        card.AdditionalLandPlaysGranted = AdditionalLandPlays;

        return card;
    }
}
