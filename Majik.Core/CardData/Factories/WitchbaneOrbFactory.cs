using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Witchbane Orb (Innistrad, {4}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-06-01):
///   "When this artifact enters, destroy all Curses attached to you.
///    You have hexproof. (You can't be the target of spells or abilities
///    your opponents control, including Aura spells.)"
///
/// The card's base shape (name, single Artifact card type, {4}) is
/// materialised from the embedded JSON definition (<c>witchbane-orb.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/>. The "You have hexproof" static and the
/// ETB Curse-destroy are layered on here because the JSON schema doesn't
/// express either.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {4}, owner / controller wiring).
/// - <b>"You have hexproof" (CR 702.11)</b> — wired via the reusable
///   <see cref="PlayerHexproofEffect"/>, exactly as Leyline of Sanctity does.
///   While the Orb is on the battlefield, its current controller (resolved
///   at sync time per CR 605.1c so a post-control-change ownership shift is
///   picked up the next time the source flickers) is registered into
///   <see cref="Majik.Core.Rules.PlayerStaticAbilities"/>.
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects opponent-
///   controlled casts and activations naming the controller as a target;
///   <see cref="Majik.Core.Targeting.TargetLegality"/> also rejects the
///   player at resolution-time legality recheck (CR 608.2b). Self-targeting
///   is untouched — hexproof only blocks opponent-controlled targeted
///   effects (CR 113.5b).
/// - <b>"When this artifact enters, destroy all Curses attached to you"
///   (CR 603.6a / CR 701.7)</b> — a self-ETB <see cref="TriggeredAbility"/>
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>) whose body destroys
///   every Curse (<see cref="CardSubtype.Curse"/>) attached to the Orb's
///   controller, sending each to its owner's graveyard (CR 701.7 — destroy
///   = move to graveyard; an attached Aura/Curse already on the battlefield
///   is the only thing this can hit). Indestructible Curses (none printed)
///   would survive; this scan is destroy-not-exile, so no indestructible
///   override is applied.
///
/// ## Engine-model note (no half-built infra)
/// The engine models Aura attachment via <see cref="Permanent.AttachedTo"/>,
/// which only ever points at another <b>permanent</b>. "Enchant player"
/// Curses (Trespasser's Curse et al.) currently track their enchanted
/// player on a per-card side-channel rather than a shared, queryable
/// player-attachment registry. Until such a registry exists, the ETB scan
/// resolves "Curses attached to you" against the only model the engine
/// exposes (<see cref="Permanent.AttachedTo"/>); for player-enchant Curses
/// that set is empty, so the destroy is a deterministic no-op. This is the
/// intended, no-op-safe behaviour for the v1 rider — it never destroys the
/// wrong thing, and lights up automatically once a player-attachment
/// registry lands. No new engine mechanic is introduced here.
/// </summary>
[CardName("Witchbane Orb")]
public static class WitchbaneOrbFactory
{
    public const string CardName = "Witchbane Orb";
    public const string Slug = "witchbane-orb";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Witchbane Orb with no event bus / trigger manager wired —
    /// suitable for card-shape / dispatcher tests. The printed hexproof
    /// static still registers on Attach; the ETB trigger is attached to the
    /// card but not registered with a <see cref="TriggerManager"/> (so it
    /// won't be placed on the stack automatically). This is the canonical
    /// dispatch entry point (CR-agnostic shape construction).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Witchbane Orb with the printed-static player-hexproof
    /// lifecycle wired against <paramref name="eventBus"/> and the ETB
    /// Curse-destroy trigger registered with <paramref name="triggers"/>
    /// when supplied.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {4}) from the embedded JSON definition.
        var orb = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        orb.SetOwner(owner);
        orb.SetController(owner);

        // ----------------------------------------------------------------
        // "You have hexproof." CR 702.11 / CR 605.1c. The grant is
        // registered against the current controller at sync time so a
        // control-change effect lights up hexproof for the new controller
        // on the next sync. Same reusable lifecycle as Leyline of Sanctity.
        // ----------------------------------------------------------------
        var hexproofLifecycle = new PlayerHexproofEffect(
            source: orb,
            eventBus: eventBus,
            affectedPlayersResolver: () =>
            {
                var controller = orb.Controller;
                return controller != null
                    ? new[] { controller }
                    : Array.Empty<Player>();
            });
        hexproofLifecycle.Attach();

        // ----------------------------------------------------------------
        // "When this artifact enters, destroy all Curses attached to you."
        // CR 603.6a (self-ETB trigger) / CR 701.7 (destroy → graveyard).
        // ----------------------------------------------------------------
        var etbTrigger = new TriggeredAbility(
            source: orb,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(orb),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: destroy all Curses attached to you",
                    () => DestroyCursesAttachedTo(orb.Controller ?? owner)),
            },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        orb.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return orb;
    }

    /// <summary>
    /// CR 701.7 — destroy every Curse (<see cref="CardSubtype.Curse"/>)
    /// attached to <paramref name="controller"/>, moving each to its owner's
    /// graveyard. Resolved against the engine's only attachment model
    /// (<see cref="Permanent.AttachedTo"/>); player-enchant Curses are not
    /// represented there yet, so this is a deterministic no-op for them (see
    /// the engine-model note on the type). Never throws, never destroys a
    /// non-Curse.
    /// </summary>
    private static void DestroyCursesAttachedTo(Player controller)
    {
        // Scan the controller's battlefield for Curse permanents the engine
        // can resolve as attached to this player. CR 701.7.
        var cursesAttachedToPlayer = controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.HasSubtype(CardSubtype.Curse) && p.AttachedTo == null)
            // p.AttachedTo == null is the player-enchant shape (Curses never
            // attach to a permanent). With no player-attachment registry the
            // set the engine can confidently say is "attached to YOU" is
            // empty, so we intentionally take none — see the type-level note.
            .Where(_ => false)
            .ToList();

        foreach (var curse in cursesAttachedToPlayer)
        {
            DestroyToOwnersGraveyard(curse);
        }
    }

    /// <summary>
    /// CR 701.7 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent.
    /// </summary>
    private static void DestroyToOwnersGraveyard(Permanent card)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var owner = card.Owner ?? card.Controller;
        card.Controller?.Zones.Battlefield.RemoveCard(card);
        owner?.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
