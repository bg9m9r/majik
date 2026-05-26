using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mogg War Marshal (Coldsnap / Modern Horizons 2,
/// Creature — Goblin Warrior {1}{R}).
///
/// Oracle text (Scryfall, verified):
///   "Echo {1}{R} (At the beginning of your upkeep, if this came under
///    your control since the beginning of your last upkeep, sacrifice it
///    unless you pay its echo cost.)
///    When Mogg War Marshal enters or dies, create a 1/1 red Goblin
///    creature token."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Goblin Warrior at printed cost {1}{R}; owner /
///   controller wired. Both <see cref="CardSubtype.Goblin"/> and
///   <see cref="CardSubtype.Warrior"/> are stamped so Goblin Chieftain /
///   Krenko / Goblin Warchief tribal scopes AND Warrior anchors (Munitions
///   Expert / Foundry Street Denizen / Goblin Piledriver buffs) pick it
///   up correctly.
/// - <b>Echo {1}{R}</b> (CR 702.49) wired as a description-only
///   <see cref="KeywordAbility"/> marker (echo cost = "{1}{R}" stored as
///   the keyword arg, mirroring how parameterised keywords like
///   "Protection" / "Ward" surface a marker before their enforcement layer
///   lands). The upkeep-sacrifice-unless-pay loop is NOT yet wired —
///   <see cref="Majik.Core.Abilities"/> / the upkeep step has no echo
///   trigger primitive yet (no other shipped factory wires echo). Same
///   posture as Suspend / Cumulative Upkeep before they shipped.
/// - <b>"When Mogg War Marshal enters or dies, create a 1/1 red Goblin
///   creature token"</b> wired as TWO <see cref="TriggeredAbility"/>s
///   sharing the same effect body:
///   <list type="number">
///     <item>ETB trigger via <see cref="Triggers.OnEnterBattlefieldSelf"/>
///       (CR 603.6a) active in <see cref="ZoneType.Battlefield"/>.</item>
///     <item>Dies trigger via <see cref="Triggers.OnDies"/> (CR 603.6c /
///       CR 700.4) active in <see cref="ZoneType.Battlefield"/> +
///       <see cref="ZoneType.Graveyard"/> (Wurmcoil / Matter Reshaper
///       posture so the trigger still matches after
///       <see cref="ZoneService"/> stamps the card's Zone = Graveyard
///       before publishing <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>).</item>
///   </list>
///   The shared effect creates one 1/1 red Goblin creature token under
///   the dying / entering card's controller via
///   <see cref="GoblinRabblemasterFactory.CreateGoblinToken"/> (same
///   <see cref="Majik.Core.Tokens.TokenFactory.TokenSpec"/> as Krenko /
///   Goblin Rabblemaster — 1/1, red, Goblin subtype, no keywords). Token
///   creation routes through <see cref="ZoneService"/> when supplied so
///   ETB triggers on the new token fire (Soul Warden / Impact Tremors /
///   Goblin Bombardment-fodder generators).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Triggers attached for
///   shape observability; not registered with any
///   <see cref="TriggerManager"/>; token creation uses raw zone
///   manipulation. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. Triggers register with <paramref name="triggers"/>; token
///   creation routes through <paramref name="zoneService"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Echo upkeep loop</b>: the printed Echo {1}{R} is shape-only — no
///   upkeep "sacrifice unless pay" cycle. Echo would need a new keyword
///   primitive (a delayed/conditional upkeep trigger that lifts on the
///   FIRST upkeep after the controller-change-or-cast event, distinct
///   from the standard delayed-trigger surface). Same posture as
///   Cumulative Upkeep / Vanishing / Fading.
/// - <b>Token ETB triggers fire only when zoneService supplied</b>: same
///   posture as Krenko / Rabblemaster — the raw fallback bypasses
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>.
/// - <b>Dies trigger control binding</b>: the dies-trigger creates the
///   token under the original <paramref name="owner"/>, not the
///   last-controller-before-death. Threaten / Act of Treason temp control
///   would in real MTG put the token under the new (Threatener's)
///   controller; v1 collapses to the original owner (same simplification
///   as Wurmcoil Engine / Matter Reshaper).
/// </summary>
[CardName("Mogg War Marshal")]
public static class MoggWarMarshalFactory
{
    public const string CardName = "Mogg War Marshal";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const string EchoCost = "{1}{R}";

    /// <summary>
    /// Construct Mogg War Marshal with no live runtime wiring. The ETB
    /// and dies triggers are attached to the card shape for observability
    /// but not registered with any <see cref="TriggerManager"/>; token
    /// creation on resolution uses raw zone manipulation. The Echo
    /// keyword marker is always attached. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Mogg War Marshal with optional runtime services. When
    /// <paramref name="triggers"/> is supplied both the ETB and dies
    /// triggers register so the matching events automatically queue them
    /// on the stack (CR 603.2). When <paramref name="zoneService"/> is
    /// supplied, token creation routes through
    /// <see cref="ZoneService"/> so the Goblin token's ETB CardMovedEvent
    /// publishes for downstream ETB triggers (Impact Tremors / Soul
    /// Warden / Purphoros, God of the Forge).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.49 — Echo {1}{R}. Description-only marker (no upkeep
        // sac-unless-pay loop yet; no echo primitive). Same posture as
        // Cumulative Upkeep / Vanishing markers before their enforcement
        // layers shipped.
        card.AddAbility(new KeywordAbility(
            keyword: "Echo",
            source: card,
            controller: owner));

        // ----------------------------------------------------------------
        // Shared "create a 1/1 red Goblin creature token" effect. Used by
        // both the ETB trigger and the dies trigger so the resolution
        // bodies are identical. CR 111 / 111.6 — token creation under the
        // dying / entering card's controller (defaults to owner for v1 —
        // same control-change caveat as Wurmcoil / Matter Reshaper).
        // ----------------------------------------------------------------
        IEffect MakeTokenEffect(string when) => new Effect(
            $"{CardName} ({when}): create 1/1 red Goblin token",
            () =>
            {
                var controller = card.Controller ?? owner;
                GoblinRabblemasterFactory.CreateGoblinToken(controller, zoneService);
            });

        // CR 603.6a — ETB trigger. "When Mogg War Marshal enters"
        // matches a CardMovedEvent with this card transitioning to
        // Battlefield. ActiveZones = Battlefield — standard ETB shape.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { MakeTokenEffect("ETB") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // CR 603.6c / CR 700.4 — dies trigger. ActiveZones =
        // Battlefield + Graveyard (Wurmcoil / Matter Reshaper posture)
        // so the trigger's zone-guard still matches after ZoneService
        // has stamped the card's Zone = Graveyard before publishing the
        // CardMovedEvent.
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { MakeTokenEffect("dies") },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
