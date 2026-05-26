using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Null Rod (Weatherlight, {2}).
///
/// Artifact — {2}.
/// Oracle text:
///   "Activated abilities of artifacts can't be activated unless they're
///    mana abilities."
///
/// ## Implemented (v1)
/// - Artifact with mana cost {2} and correct identity / owner / controller.
/// - <b>Printed static</b> (CR 602.5c / 605): functional copy of
///   <see cref="StonySilenceFactory"/>. Reuses
///   <see cref="StonySilenceStaticEffect"/> as the lifecycle binder —
///   the predicate registered into
///   <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/> matches
///   any activated ability whose source is an on-battlefield artifact.
///   Both players' artifacts are gated (symmetric). Null Rod itself is
///   an artifact, but its static ability is not an activated ability, so
///   the printed static remains in effect while Null Rod is on the
///   battlefield (CR 113.6).
/// - <b>CR 605 mana-ability exemption</b>: same as Stony Silence — the
///   registry short-circuits on <see cref="Majik.Core.Abilities.IManaAbility"/>;
///   mana abilities are routed through
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> which bypasses
///   <see cref="Majik.Core.Rules.ActionValidator"/>, so {T}: Add {C} on a
///   Mox / Sol Ring / etc. still works under Null Rod.
///
/// ## Functional copy of Stony Silence
/// Null Rod and Stony Silence have identical printed text. They differ
/// only in card type (Null Rod is an Artifact, Stony Silence is an
/// Enchantment) and mana cost ({2} vs {1}{W}). The named factory + tests
/// here lock the artifact-card-shape end of that pair; the static-effect
/// behaviour is shared.
/// </summary>
[CardName("Null Rod")]
public static class NullRodFactory
{
    public const string CardName = "Null Rod";
    public const string Cost = "{2}";

    /// <summary>
    /// Construct a Null Rod with no live wiring. The printed static is
    /// not registered (no event bus). Suitable for card-shape / dispatcher
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct a Null Rod whose printed static is wired against
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            var lifecycle = new StonySilenceStaticEffect(source: card, eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
