using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Hearth and Home (Modern Horizons 2,
/// {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from green and
///    from white."
///   "Whenever equipped creature deals combat damage to a player, exile
///    another target creature you control, then return that card to the
///    battlefield under its owner's control. Search your library for a
///    basic land card, put it onto the battlefield tapped, then
///    shuffle."
///   "Equip {2}."
///
/// Same shape as <see cref="SwordOfFireAndIceFactory"/> /
/// <see cref="SwordOfFeastAndFamineFactory"/>: AttachedBoostEffect at
/// Layer 7c for +2/+2, GrantAbilityEffect re-projecting
/// <see cref="ProtectionAbility"/>("green") /
/// ProtectionAbility("white") onto the equipped creature at Layer 6,
/// and a <see cref="CombatDamageDealtEvent"/>-keyed
/// <see cref="TriggeredAbility"/> gated on the equipped creature dealing
/// damage to a player (CR 510 / CR 603.1).
///
/// ## Combat-damage rider
///
/// "Exile another target creature you control, then return that card to
/// the battlefield under its owner's control" — a single 1..1 target
/// slot scoped to creatures the Sword's controller controls
/// (CR 115.5b — "another" excludes the equipped creature itself; the
/// rider is intentionally a "blink" effect that flickers a different
/// permanent than the equipped attacker). On resolution the chosen
/// creature is exiled then returned to the battlefield under its
/// owner's control. v1 routes the exile + return through the zone
/// services if available so ETB / LTB triggers fire; absent the
/// services the move falls back to raw-zone shuffles.
///
/// "Search your library for a basic land card, put it onto the
/// battlefield tapped, then shuffle." — agent-driven basic-land tutor,
/// same shape as <see cref="PathToExileFactory"/>'s tutor rider but
/// puts the basic in tapped (CR 305.4 / CR 701.20a).
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only. With the runtime
/// overload, +2/+2 / protection grants register against
/// <paramref name="continuousEffects"/> and the trigger registers
/// against <paramref name="triggers"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>Target prompts</b> for the combat trigger — v1 honours
///   pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; absent a chosen
///   target the exile/return half no-ops while the basic-land tutor
///   still resolves (CR 608.2b — do as much as possible; the tutor is
///   not a targeted rider).
/// - <b>"New object" rules</b> on return — CR 400.7 treats the
///   returned card as a new object. v1 retains the same
///   <see cref="Card"/> instance; identity-sensitive auras / counters
///   would diverge from paper here.
/// </summary>
[CardName("Sword of Hearth and Home")]
public static class SwordOfHearthAndHomeFactory
{
    public const string CardName = "Sword of Hearth and Home";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    private static readonly HashSet<string> BasicLandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes",
    };

    /// <summary>
    /// Constructs Sword of Hearth and Home with no live runtime wiring
    /// (shape / dispatcher path). Protection markers are present on the
    /// equipment card; the combat-damage trigger is attached for shape
    /// but not registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Sword of Hearth and Home. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) and protection grants (Layer 6, green + white)
    /// register against it. When <paramref name="triggers"/> is supplied
    /// the combat-damage trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from the equipped creature
    /// targeting a player automatically queues the ability.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Protection grants — "has protection from green and from white"
        // (CR 702.16 / CR 613.1f). Same wiring as Sword of Fire and Ice.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("green")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("white")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("green"));
            card.AddAbility(new ProtectionAbility("white"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    exile another target creature you control, then return
        //    that card to the battlefield under its owner's control.
        //    Search your library for a basic land card, put it onto the
        //    battlefield tapped, then shuffle."
        // --------------------------------------------------------------
        TriggeredAbility? combatTrigger = null;
        var combatEffect = new Effect(
            $"{CardName}: exile+return another your creature, then tutor basic land tapped",
            async ctx =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (combatTrigger == null) return;

                var controller = card.Controller ?? owner;

                // 1) Exile target creature you control, then return it.
                //    "Another" — must not be the equipped creature
                //    (CR 115.5b enforced at target-selection time; the
                //    resolve path additionally guards so a stale target
                //    no-ops).
                var slots = combatTrigger.ChosenTargets;
                if (slots.Count > 0 && slots[0].Count > 0
                    && slots[0][0] is Creature target
                    && !ReferenceEquals(target, card.AttachedTo)
                    && target.Controller == controller
                    && target.Zone == ZoneType.Battlefield)
                {
                    ExileThenReturn(target);
                }

                // 2) Tutor a basic land tapped. CR 305.4 — "tapped" rider
                //    enters the basic with its Tapped flag set; tutor is
                //    not a targeted rider so it always resolves
                //    (CR 608.2b).
                await TutorBasicLandTappedAsync(controller, ctx).ConfigureAwait(false);
            });

        combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Exile <paramref name="target"/> from the battlefield, then return
    /// it to the battlefield under its owner's control (CR 701.20).
    /// Same instance reused; v1 does not allocate a "new object" per
    /// CR 400.7 — identity-sensitive riders (auras-via-Animate-Dead,
    /// counter accounting) would diverge from paper here.
    /// </summary>
    private static void ExileThenReturn(Creature target)
    {
        var owner = target.Owner;
        if (owner == null) return;

        var controller = target.Controller ?? owner;
        var zones = ZoneServiceRegistry.Get(controller);

        // Exile half.
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile, owner);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Exile.AddCard(target);
            target.SetZone(ZoneType.Exile);
        }

        // Return half — under its owner's control (Sword's controller
        // might differ from owner when control-magic shenanigans are in
        // play; the rider explicitly says "under its owner's control",
        // not the Sword's controller). CR 614 ETB triggers fire on
        // ZoneService-routed moves.
        var ownerZones = ZoneServiceRegistry.Get(owner);
        if (ownerZones != null)
        {
            ownerZones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, owner);
        }
        else
        {
            owner.Zones.Exile.RemoveCard(target);
            owner.Zones.Battlefield.AddCard(target);
            target.SetZone(ZoneType.Battlefield);
            target.SetController(owner);
        }
    }

    /// <summary>
    /// Offer <paramref name="player"/>'s registered agent the basic-land
    /// tutor and put the chosen card onto the battlefield tapped
    /// (CR 305.4 / CR 701.20a). Mirrors
    /// <see cref="PathToExileFactory"/>'s tutor rider — agent picks
    /// among basic-land candidates (deterministic first-candidate
    /// fallback when no agent is registered); the result is tapped on
    /// arrival.
    /// </summary>
    private static async ValueTask TutorBasicLandTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search (see LibrarySearch xmldoc).
        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic land card").ConfigureAwait(false);

        if (pick != null)
        {
            player.Zones.Library.RemoveCard(pick);
            player.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(player);
            if (pick is Permanent perm)
            {
                perm.Tap();
            }
        }
        // CR 701.20a — shuffle whether or not a card was found.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "sword-of-hearth-and-home");
    }
}
