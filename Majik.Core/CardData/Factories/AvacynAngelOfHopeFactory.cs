using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avacyn, Angel of Hope (Avacyn Restored,
/// {5}{W}{W}{W}).
///
/// Legendary Creature — Angel. 8/8 with Flying, Vigilance, Indestructible.
/// Oracle text:
///   "Flying, vigilance, indestructible.
///    Other permanents you control have indestructible."
///
/// ## Implemented (v1)
/// - 8/8 Legendary Creature — Angel, mana cost {5}{W}{W}{W}.
/// - Printed evergreens (CR 702.9 / 702.20 / 702.12) wired as
///   <see cref="KeywordAbility"/> markers: Flying, Vigilance,
///   Indestructible.
/// - <b>"Other permanents you control have indestructible"</b> wired via
///   <see cref="ControllerPermanentAnthemEffect"/> with
///   <c>grantedKeywords: ["Indestructible"]</c>, <c>includeSelf: false</c>.
///   Registered against the supplied
///   <see cref="ContinuousEffectsService"/>; the effect's
///   <see cref="ContinuousEffect.IsActive"/> gate short-circuits when
///   Avacyn isn't on the battlefield so the bonus lifts on LTB. Pruning
///   the entry off the service is a follow-up (matches the LTB-cleanup
///   shape of every other lord-shaped factory — Goblin Chieftain, Plague
///   Engineer, etc.).
///
/// Other creatures she controls are covered by the destroy-gate path in
/// <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/> (the
/// creature path consults the layer-system computed keywords). Non-creature
/// permanents she controls are covered by the lifted
/// <see cref="Permanent.ActiveEffects"/> field plus the layer-aware branch
/// added to <see cref="OracleSpellBinder"/>'s non-creature
/// <c>HasIndestructible</c> probe.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered effect stays on the service
///   across zone changes; <see cref="ContinuousEffect.IsActive"/> gates
///   it off correctly, but a future <see cref="ContinuousEffectsService.Prune"/>
///   sweep could drop the entry to reclaim memory.
/// - <b>Control transfer</b>: the predicate keys on
///   <c>permanent.Controller == source.Controller</c> at every recompute,
///   so a Threaten-style control swap on Avacyn does flow through —
///   permanents controlled by the new controller of Avacyn pick up the
///   grant. Mass-anthem timing edge cases under layered control changes
///   are out of v1 scope.
/// </summary>
[CardName("Avacyn, Angel of Hope")]
public static class AvacynAngelOfHopeFactory
{
    public const string CardName = "Avacyn, Angel of Hope";
    public const string PrintedManaCost = "{5}{W}{W}{W}";
    public const int Power = 8;
    public const int Toughness = 8;

    /// <summary>
    /// Construct Avacyn with printed evergreens (Flying, Vigilance,
    /// Indestructible) wired as <see cref="KeywordAbility"/> markers but
    /// no live continuous-effects service — suitable for shape /
    /// dispatcher tests. The "other permanents you control have
    /// indestructible" static is not registered because there's no layers
    /// service to register against.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Avacyn. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="ControllerPermanentAnthemEffect"/> granting Indestructible
    /// to other permanents the controller controls is registered against
    /// the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service. May be null — no
    /// live grant.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.20 / 702.12 — printed evergreens on Avacyn herself.
        // Marker abilities; the destroy gate (OracleSpellBinder) and combat
        // ability lookups (CombatAbilities) read these.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        if (continuousEffects != null)
        {
            // Wire the creature to the layer system so granted-keyword
            // lookups from CombatAbilities / the destroy gate flow through.
            card.ActiveEffects = continuousEffects;

            // CR 613.1f — keyword grant on every other permanent the
            // controller controls. includeSelf:false honours "OTHER
            // permanents"; Avacyn's own Indestructible comes from the
            // printed keyword above. No extra predicate — covers creatures,
            // artifacts, enchantments, lands, planeswalkers, battles.
            continuousEffects.Register(new ControllerPermanentAnthemEffect(
                source: card,
                powerBonus: 0,
                toughnessBonus: 0,
                grantedKeywords: new[] { "Indestructible" },
                includeSelf: false,
                extraPredicate: null));
        }

        return card;
    }
}
