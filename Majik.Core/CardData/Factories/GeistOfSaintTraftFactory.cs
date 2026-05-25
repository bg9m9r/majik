using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Geist of Saint Traft (Innistrad, {1}{W}{U}).
///
/// Legendary Creature — Spirit Cleric 2/2. Oracle text:
///   "Hexproof.
///    Whenever Geist of Saint Traft attacks, create a tapped and attacking
///    4/4 white Angel creature token with flying."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Spirit Cleric, mana cost {1}{W}{U}
///   (CR 205.4a — Legendary supertype; CR 205.3m Spirit + Cleric).
/// - <b>Hexproof</b> (CR 702.11) wired as a <see cref="KeywordAbility"/>
///   marker; the targeting validator denies opponent-controlled spells /
///   abilities from selecting Geist as a target (same shape as
///   <see cref="StripedRiverwinderFactory"/>).
/// - <b>Attack triggered ability</b> (CR 508.1f / CR 603.6a) wired via
///   <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="CreatureAttacksEvent"/>. On resolution creates one
///   4/4 white Angel creature token with Flying under Geist's
///   controller via <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Deferred — "tapped and attacking" token (v1 gap)
///
/// The printed oracle creates the Angel <i>already tapped and attacking</i>
/// the same defender (CR 509.7-ish — tokens created "tapped and
/// attacking" skip the declare-attackers step and don't trigger
/// "whenever a creature attacks" abilities). Majik's
/// <see cref="Majik.Core.Combat.CombatManager"/> currently has no
/// surface for splicing a creature into an in-progress combat —
/// <see cref="Majik.Core.Combat.CombatManager.DeclareAttackers"/> is a
/// one-shot declaration that creates the <see cref="Majik.Core.Combat.Combat"/>
/// instance from the supplied list. Without an "insert attacker mid-
/// combat" primitive, v1 ships the Angel as a normal 4/4 flier ETB onto
/// the battlefield — untapped, not attacking — and defers the
/// tapped/attacking shape to a future combat-manager extension.
///
/// Practical impact for current play:
///   - The Angel is still produced on Geist's attack trigger.
///   - It still has Flying, white colour, and 4/4 P/T.
///   - It DOESN'T deal combat damage this turn (no attacker slot).
///   - It DOES have summoning sickness this turn (TokenFactory default).
///     The printed token is exempt from sickness because it's already
///     attacking — once the combat-manager extension exists this should
///     flip to <c>HasSummoningSickness = false</c> alongside the
///     tapped + attacker-slot wiring.
///
/// CR rule references: 205.4a (Legendary), 205.3m (Spirit / Cleric /
/// Angel subtypes), 702.9 (Flying), 702.11 (Hexproof),
/// 508.1f (per-attacker trigger), 111.4 / 111.6 (token creation).
/// </summary>
[CardName("Geist of Saint Traft")]
public static class GeistOfSaintTraftFactory
{
    public const string CardName = "Geist of Saint Traft";
    public const string PrintedManaCost = "{1}{W}{U}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TokenPower = 4;
    public const int TokenToughness = 4;

    /// <summary>
    /// Construct Geist of Saint Traft with no live wiring. The attack
    /// trigger is attached to the card shape; token creation on
    /// resolution still goes through <see cref="TokenFactory"/> against
    /// the controller's battlefield. Suitable for dispatcher / shape
    /// tests + manual trigger.Effects.Execute() drives.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Geist of Saint Traft with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied the attack
    /// trigger is registered so a <see cref="CreatureAttacksEvent"/>
    /// matching Geist lands it on the stack automatically. May be null
    /// — the trigger is still attached to the card shape.</param>
    /// <param name="zoneService">Optional zone service so the token's
    /// ETB <see cref="CardMovedEvent"/> fires (Soul Warden etc.). Pass
    /// null for raw zone moves.</param>
    public static Creature Create(
        Player owner,
        Abilities.TriggerManager? triggers,
        Majik.Core.Services.ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.11 — Hexproof. KeywordAbility marker consumed by the
        // targeting validator (denies opponent-controlled spells /
        // abilities from selecting Geist as a target).
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        // ----------------------------------------------------------------
        // CR 508.1f — "Whenever Geist of Saint Traft attacks, create a
        // tapped and attacking 4/4 white Angel creature token with
        // flying." v1 omits the tapped+attacking shape — see class
        // xmldoc — and ships a normal 4/4 white flier ETB on Geist's
        // controller's battlefield.
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: create 4/4 white Angel token with Flying (CR 508.1f)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateAngelToken(controller, zoneService);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 4/4 white Angel creature token with
    /// Flying under <paramref name="controller"/>'s control. The
    /// printed "tapped and attacking" shape is deferred — see factory
    /// xmldoc.
    /// </summary>
    public static Creature CreateAngelToken(
        Player controller,
        Majik.Core.Services.ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Angel",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Angel },
            Keywords: new[] { "Flying" },
            // CR 105 / CR 111.4 — printed "4/4 white Angel creature token
            // with flying".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
