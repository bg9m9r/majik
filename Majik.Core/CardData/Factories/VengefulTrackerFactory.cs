using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vengeful Tracker (Murders at Karlov Manor,
/// {1}{R}).
///
/// Creature — Human Detective 2/2. Oracle text (Scryfall, verified):
///   "Whenever an opponent sacrifices an artifact, this creature deals 2
///    damage to them."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Detective {1}{R}; owner / controller wired.
/// - <b>Opponent-sacrifices-an-artifact trigger (CR 603.1 + CR 701.16 +
///   CR 109.5)</b>: a <see cref="TriggeredAbility"/> over the dedicated
///   <see cref="PermanentSacrificedEvent"/> via the declarative
///   <see cref="Triggers.OnOpponentSacrifices(Player, CardType?)"/>
///   predicate, scoped to (sacrificing player is an opponent of the
///   controller AND the sacrificed permanent is an Artifact). On
///   resolution the source deals 2 damage to <em>that opponent</em> ("them"
///   = the player who sacrificed the artifact, CR 109.5) via
///   <see cref="Fx.DealDamage(object, int)"/> (Player → life loss).
///
/// This is the first implemented card to consume the controller-gated
/// "whenever an opponent sacrifices …" producer-side primitive — the
/// <see cref="PermanentSacrificedEvent"/> already fires on every real
/// sacrifice path (cost / edict / land-binder / token self-sac); this
/// factory wires the CONSUMER side.
///
/// ## Deferred (v1 gaps)
/// - <b>No target prompt</b>: "deals 2 damage to them" has no chosen
///   target — "them" is the opponent who sacrificed the artifact, captured
///   off the live <see cref="PermanentSacrificedEvent.SacrificingPlayer"/>
///   at match time. No agent prompt is needed.
/// </summary>
[CardName("Vengeful Tracker")]
public static class VengefulTrackerFactory
{
    public const string CardName = "Vengeful Tracker";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int Damage = 2;

    /// <summary>
    /// Construct Vengeful Tracker with no live runtime services. The
    /// opponent-sacrifices trigger is attached to the card shape but not
    /// registered with a <see cref="TriggerManager"/>. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Vengeful Tracker with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the opponent-sacrifices
    /// trigger is registered so the bus drives it automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Detective });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Opponent-sacrifices-an-artifact trigger — CR 603.1 + CR 701.16 +
        // CR 109.5.
        //   "Whenever an opponent sacrifices an artifact, this creature
        //    deals 2 damage to them."
        // Fires on the dedicated PermanentSacrificedEvent via the
        // declarative Triggers.OnOpponentSacrifices predicate (sacrificing
        // player is an OPPONENT of the controller AND the sacrificed
        // permanent is an Artifact). "Them" is the opponent who sacrificed
        // it — captured off the live event at match time so a control
        // change is honoured (CR 109.5).
        // ----------------------------------------------------------------
        Player? capturedOpponent = null;

        // The declarative producer-side predicate: "an opponent sacrifices
        // an artifact" (CR 109.5 + CR 701.16). Re-bound to the LIVE
        // controller per match so a control change is honoured, then the
        // sacrificing opponent is captured for the effect ("them").
        var condition = new EventTriggerCondition<PermanentSacrificedEvent>((e, ab) =>
        {
            var controller = card.Controller ?? owner;
            if (!Triggers.OnOpponentSacrifices(controller, CardType.Artifact).Matches(e, ab))
            {
                return false;
            }

            capturedOpponent = e.SacrificingPlayer;
            return true;
        });

        var damageEffect = new Effect(
            $"{CardName}: deal {Damage} damage to the opponent who sacrificed the artifact",
            () =>
            {
                if (capturedOpponent is null) return;
                // CR 119 — "deals 2 damage to them". Player target routes
                // to life loss via Fx.DealDamage.
                Fx.DealDamage(capturedOpponent, Damage);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
