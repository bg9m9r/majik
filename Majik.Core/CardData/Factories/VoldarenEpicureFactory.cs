using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voldaren Epicure (Innistrad: Crimson Vow, {R}).
///
/// Creature — Vampire Citizen 1/1. Oracle text:
///   "When Voldaren Epicure enters, it deals 1 damage to each opponent
///    and you create a Blood token."
///
/// ## Implemented (v1)
///
/// - 1/1 Vampire Citizen with mana cost {R}, owner / controller stamped.
/// - <b>ETB triggered ability (CR 603.6a)</b>: <see cref="TriggeredAbility"/>
///   wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution
///   the effect:
///   <list type="number">
///     <item>Deals 1 damage to each opponent supplied by the optional
///       <c>opponentResolver</c>. Damage routes through
///       <see cref="Fx.DealDamageAny"/> so any Player / Planeswalker shape
///       is consistent (same shape as Creeping Chill / Omnath, Locus of
///       Creation). Without a resolver the damage half no-ops — the
///       Player aggregate doesn't expose an opponents list at v1, so the
///       caller threads it through (mirrors the established pattern).</item>
///     <item>Creates one Blood token under the controller's control via
///       <see cref="TokenFactory.CreateBlood"/> (red artifact token with
///       {1}, {T}, Discard a card, Sacrifice this artifact: Draw a card).
///       When a <see cref="ZoneService"/> is wired the token is put onto
///       the battlefield via the service so any "whenever a token enters"
///       triggers fire (CR 603.6a); without a service the raw zone path
///       is taken (shape-only).</item>
///   </list>
///
/// ## Bot intent
///
/// The ETB damage is Burn + Reach; the Blood token is Ramp-adjacent (loot
/// fuel for graveyard / discard-matters decks). Voldaren Epicure is the
/// canonical Crimson Vow Blood enabler — paired with Falkenrath Pit Fighter
/// the activated-ability sacrifice cost gets a renewable fuel source.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live "each opponent" enumeration</b>: no <c>Player.Opponents</c>
///   accessor exists at v1; the resolver-injection pattern is shared with
///   <see cref="CreepingChillFactory"/> / <see cref="OmnathLocusOfCreationFactory"/>.
///   Without a resolver, the damage half silently no-ops. The Blood token
///   half always fires.
/// - <b>Triggered-ability stack ordering</b>: the ETB trigger is attached
///   structurally and registered with the supplied
///   <see cref="TriggerManager"/> (when provided). Resolution ordering vs.
///   other ETB triggers follows the standard APNAP stack ordering — same
///   posture as Soul Warden / Thalia's Lieutenant ETB triggers.
/// </summary>
[CardName("Voldaren Epicure")]
public static class VoldarenEpicureFactory
{
    public const string CardName = "Voldaren Epicure";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int EtbDamageAmount = 1;

    /// <summary>
    /// Construct Voldaren Epicure with no runtime service wiring. The card
    /// has the correct shape (name, type, P/T, mana cost, subtypes) and the
    /// ETB trigger is attached for structural / dispatcher inspection, but
    /// the trigger is not registered with a <see cref="TriggerManager"/>
    /// and the damage half no-ops at resolution (no opponent resolver).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null, opponentResolver: null);

    /// <summary>
    /// Construct Voldaren Epicure with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for ETB registration. May be
    /// null — trigger is attached structurally but not enrolled.</param>
    /// <param name="zoneService">Zone-service used by
    /// <see cref="TokenFactory.CreateBlood"/> when seating the token so any
    /// token-enters trigger fires. May be null — raw zone move performed
    /// instead.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent"
    /// for the burn half. Without a resolver the damage half no-ops; the
    /// Blood-creation half always fires.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Citizen });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When Voldaren Epicure enters, it deals 1 damage to each
        //    opponent and you create a Blood token."
        // Two clauses joined by "and" — single trigger, two ordered effects
        // in one closure (CR 603.3c — single triggered ability with
        // multiple events fires once and produces both effects on
        // resolution).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: deal {EtbDamageAmount} to each opponent + create a Blood token",
            () =>
            {
                // Clause 1 — "deals 1 damage to each opponent".
                // CR 119 — life loss / damage to each opponent. The Epicure
                // is the source (printed "it deals 1 damage"), so any
                // damage-source-watching trigger (e.g. lifelink on a future
                // grant) should observe the Epicure as the source — but the
                // current Fx.DealDamageAny target-side helper doesn't yet
                // thread the source through, matching Creeping Chill's
                // shape (deferred at the primitive level).
                var opponents = opponentResolver?.Invoke();
                if (opponents != null)
                {
                    foreach (var opp in opponents)
                    {
                        if (ReferenceEquals(opp, owner)) continue;
                        Fx.DealDamageAny(opp, EtbDamageAmount);
                    }
                }

                // Clause 2 — "you create a Blood token". CR 111.10 —
                // tokens enter the battlefield directly. TokenFactory.
                // CreateBlood handles the sentinel-library → battlefield
                // pattern so any "whenever a token enters" trigger
                // (Academy Manufactor, etc.) observes the move.
                TokenFactory.CreateBlood(owner, zoneService);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
