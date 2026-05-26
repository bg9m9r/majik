using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Supreme Phantom (Core Set 2019, {1}{U}).
///
/// Creature — Spirit 1/3. Oracle text:
///   "Flying
///    Other Spirit creatures you control get +1/+1."
///
/// ## Implemented (v1)
/// - 1/3 Creature — Spirit, mana cost {1}{U}, owner / controller wired.
/// - <b>Flying</b> keyword marker (CR 702.9) via <see cref="KeywordAbility"/>.
/// - <b>Static lord effect</b> "Other Spirit creatures you control get
///   +1/+1" wired via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: Spirit</c>, <c>power: 1, toughness: 1</c>,
///   <c>includeSelf: false</c>, <c>allPlayers: false</c>,
///   <c>opponentsOnly: false</c> — i.e. only Spirits the source's
///   controller controls, the source itself excluded (Layer 7c per
///   CR 613.7c, registered on the supplied
///   <see cref="ContinuousEffectsService"/>).
///
/// ## UW Spirits payoff
/// Half-mana of every UW Spirits lord — Supreme Phantom is the cheapest
/// 1U Spirit lord ever printed and lets Mausoleum Wanderer / Rattlechains
/// / Selfless Spirit / Spell Queller all attack for one more damage. The
/// shared anthem layer is the deck's clock.
///
/// ## Deferred (v1 gaps)
/// - LTB unregister — the registered <see cref="LordStaticEffect"/> stays
///   on the <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Supreme
///   Phantom isn't on the battlefield so the bonus lifts correctly (same
///   posture as Lord of Atlantis / Master of the Pearl Trident).
/// </summary>
[CardName("Supreme Phantom")]
public static class SupremePhantomFactory
{
    public const string CardName = "Supreme Phantom";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Supreme Phantom without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static effect is
    /// not registered. Other Spirits don't receive +1/+1 because there's
    /// no layers service to register against.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Supreme Phantom. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 to all OTHER Spirits
    /// controlled by <paramref name="owner"/> is registered against the
    /// layers service.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c — Layer 7c +1/+1 on other Spirits the controller
            // controls. includeSelf: false honours "Other". allPlayers
            // and opponentsOnly default false so only the controller's
            // own Spirits get the buff (the oracle's "you control"
            // qualifier).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Spirit,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }
}
