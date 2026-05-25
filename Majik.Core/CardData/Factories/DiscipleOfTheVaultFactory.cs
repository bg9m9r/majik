using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Disciple of the Vault (Mirrodin, {B}).
///
/// Creature — Human Cleric 1/1. Oracle text:
///   "Whenever an artifact is put into a graveyard from the battlefield,
///    target opponent loses 1 life."
///
/// ## Implemented (v1)
///
/// - 1/1 Human Cleric at {B}, owner/controller wired.
/// - <b>Artifact-dies trigger (CR 603.1 + CR 700.4)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   filtered to FromZone = Battlefield + ToZone = Graveyard +
///   moved card has type <see cref="CardType.Artifact"/>. The trigger
///   fires for ANY artifact moving to ANY graveyard (own or
///   opponent's) — same scope as Bridge from Below's "creature is
///   put into your graveyard" pattern. CR 603.10 — controller is read
///   off the moved card after the zone move (engine keeps
///   <see cref="Permanent.Controller"/> across the move, so the LKI
///   snapshot is effectively the live read).
///   <para/>
///   Note: this trigger fires for Vault Skirge (Artifact Creature)
///   dying, Disciple's own artifact-creature siblings, equipment going
///   to the graveyard, Mox Opal sacrificed, etc. — every event where
///   the dying card carries the Artifact type. This is the printed
///   wording (CR 700.4 — "put into a graveyard from the battlefield"
///   covers death, sacrifice, and bounce-to-graveyard).
/// - <b>Effect</b>: "target opponent loses 1 life" — the trigger's
///   <see cref="TriggeredAbility.TargetRequests"/> carries a single
///   "target opponent" request (MinTargets=1, MaxTargets=1) so the
///   stack-resolve path knows to prompt for a player pick. On
///   resolution the chosen player loses 1 life via
///   <see cref="Player.LoseLife"/>. CR 608.2b — if the chosen
///   opponent is no longer legal (e.g. already lost the game), the
///   effect is a no-op.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The trigger is attached
///   to the card via <see cref="Card.AddAbility"/> so structural /
///   dispatcher tests can observe it; it isn't registered with any
///   <see cref="TriggerManager"/>. Suitable for the dispatcher /
///   shape suite.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the
///   trigger with the supplied manager so the bus drives it
///   automatically when artifacts move to graveyards.
///
/// ## Deferred (v1 gaps)
/// - <b>"Target opponent" candidate gathering</b>: the trigger's
///   TargetRequest carries an empty LegalCandidates list; live games
///   should populate it with the controller's opponents (via the
///   resolver supplied to the cast / stack-resolve path). v1 leaves
///   the target unresolved when no candidates are wired, mirroring
///   the rest of the named-card factories.
/// - <b>Tokens</b>: a non-token-only filter is NOT applied — the
///   printed text is unqualified ("an artifact"), so artifact tokens
///   dying to the graveyard trigger Disciple as well. Same shape as
///   Bridge from Below's second trigger.
/// </summary>
[CardName("Disciple of the Vault")]
public static class DiscipleOfTheVaultFactory
{
    public const string CardName = "Disciple of the Vault";
    public const string PrintedManaCost = "{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Disciple of the Vault with no live TriggerManager
    /// wiring. The artifact-dies trigger is attached to the card so
    /// structural / shape tests can observe it; live bus dispatch
    /// requires the runtime overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Disciple of the Vault with optional runtime services.
    /// When <paramref name="triggers"/> is supplied, the artifact-dies
    /// trigger is registered with the manager so the bus drives it
    /// automatically on every Battlefield → Graveyard CardMovedEvent
    /// for an artifact.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
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

        // ----------------------------------------------------------------
        // Triggered ability — CR 603.1 + CR 700.4.
        //   "Whenever an artifact is put into a graveyard from the
        //    battlefield, target opponent loses 1 life."
        // Fires on CardMovedEvent Battlefield → Graveyard for any
        // card whose live HasType(Artifact) is true. The dying card
        // itself can be any zone of artifact (Equipment, Mox-shaped
        // Legendary Artifact, Artifact Creature) — printed wording is
        // unqualified.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            return e.Card.HasType(CardType.Artifact);
        });

        TriggeredAbility? abilityHandle = null;

        var loseLifeEffect = new Effect(
            $"{CardName}: target opponent loses 1 life",
            () =>
            {
                if (abilityHandle == null) return;
                if (abilityHandle.ChosenTargets.Count == 0
                    || abilityHandle.ChosenTargets[0].Count == 0) return;

                if (abilityHandle.ChosenTargets[0][0] is not Player chosen) return;
                // CR 608.2b — target legality recheck. Don't drain the
                // controller (the trigger reads "target opponent") and
                // skip players already at <= 0 life (they'll be removed
                // by SBAs anyway).
                if (ReferenceEquals(chosen, owner)) return;
                chosen.LoseLife(1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { loseLifeEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: Majik.Core.Cards.BotIntent.LoseLife),
            });

        abilityHandle = trigger;

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
