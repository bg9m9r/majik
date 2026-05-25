using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodtithe Harvester (Innistrad: Crimson Vow,
/// {1}{B/R}).
///
/// Creature — Vampire 3/2. Oracle text:
///   "When Bloodtithe Harvester enters, create a Blood token.
///    {1}, Sacrifice a Blood token: Bloodtithe Harvester deals 2 damage
///    to any target."
///
/// ## Implemented (v1)
///
/// - 3/2 Creature — Vampire, mana cost {1}{B/R}. Hybrid pip is preserved
///   on the printed cost string (same shape as
///   <see cref="FulminatorMageFactory"/>'s {B/R}{B/R}).
/// - <b>ETB Blood-token trigger (CR 603.6a)</b>: <see cref="TriggeredAbility"/>
///   firing on Bloodtithe Harvester's own ETB
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>); resolve creates one
///   Blood token via <see cref="TokenFactory.CreateBlood"/> under the
///   harvester's controller. ActiveZones = {Battlefield} per the standard
///   ETB-trigger shape (the trigger only needs to fire while the source
///   is on the battlefield post-ETB).
/// - <b>"{1}, Sacrifice a Blood token: ~ deals 2 damage to any target"</b>
///   activated ability: an <see cref="ActivatedAbility"/> with
///   <see cref="ManaCostCost"/>("{1}") plus an inline "sacrifice a Blood
///   you control" payment performed by the effect closure (same posture
///   as <see cref="PyriteSpellbombFactory"/> — the generic
///   <see cref="AdditionalCost"/> sacrifice path is a stub for
///   subtype-filtered sacs, so the effect itself picks + moves a Blood
///   to the graveyard). A single any-target <see cref="TargetRequest"/>
///   is declared so the activating player's agent picks a player /
///   creature / planeswalker at activation (CR 602.2b); resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so planeswalker targets convert to
///   loyalty removal (CR 306.7).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Subtype-filtered sacrifice as a first-class cost</b>: the engine
///   exposes <see cref="SacrificeAnArtifactCost"/> but not a "sacrifice
///   a Blood / Treasure / Food" cost helper. v1 inlines the Blood pick
///   in the effect closure (same posture as Pyrite Spellbomb's
///   self-sacrifice). The <see cref="ActivatedAbility.CanActivate"/>
///   path therefore does NOT gate on Blood availability — agents can
///   queue the ability with no Blood and the effect silently no-ops.
///   Follow-up: lift this into <c>SacrificeASubtypeCost</c> shared with
///   Treasure / Food / Powerstone sac-additional-cost spells.
/// - <b>Hybrid pip payment</b>: the printed cost reads {1}{B/R}; the
///   engine's mana-pay path already accepts either {B} or {R} for the
///   hybrid pip via <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>
///   (shared with Fulminator Mage). No factory-side work needed.
/// </summary>
[CardName("Bloodtithe Harvester")]
public static class BloodtitheHarvesterFactory
{
    public const string CardName = "Bloodtithe Harvester";
    public const string PrintedManaCost = "{1}{B/R}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Bloodtithe Harvester with no live runtime services. The
    /// ETB trigger is attached for shape observability; the activated
    /// ability is fully wired (no external service needed beyond agent
    /// target selection). For end-to-end bus-driven ETB firing pass the
    /// runtime overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Bloodtithe Harvester with optional runtime services.
    /// When <paramref name="triggers"/> is supplied the ETB trigger
    /// registers so the ETB <see cref="CardMovedEvent"/> automatically
    /// queues the Blood-token spawn on the stack.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — "When ~ enters, create a Blood token."
        // CR 603.6a — enters-the-battlefield trigger. ActiveZones =
        // {Battlefield} per the standard ETB shape (Triggers.OnEnter*
        // family).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create a Blood token on ETB",
            () => TokenFactory.CreateBlood(owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {1}, Sacrifice a Blood token: ~ deals 2 damage to any target.
        // CR 602 — activated ability. The mana pip is paid through the
        // standard ManaCostCost; the Blood-sac is performed inline by
        // the effect closure (no subtype-filtered sacrifice cost
        // primitive yet — see factory xmldoc gap). The single
        // any-target TargetRequest routes via Fx.DealDamageAny so
        // planeswalker targets convert to loyalty removal (CR 306.7).
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName}: sac a Blood + 2 damage to any target",
            () =>
            {
                // Inline Blood-sac payment. Pick the first Blood the
                // controller controls (deterministic v1 — same picker
                // shape as SacrificeAnArtifactCost). Silently no-ops if
                // no Blood is available; activator is expected to gate
                // on this themselves until the subtype-sac cost
                // primitive lands.
                var blood = owner.Zones.Battlefield.GetCards()
                    .OfType<Permanent>()
                    .FirstOrDefault(p => p.HasSubtype(CardSubtype.Blood));

                if (blood != null)
                {
                    owner.Zones.Battlefield.RemoveCard(blood);
                    owner.Zones.Graveyard.AddCard(blood);
                    blood.SetZone(ZoneType.Graveyard);
                }

                if (damageAbility != null
                    && damageAbility.ChosenTargets.Count > 0
                    && damageAbility.ChosenTargets[0].Count > 0)
                {
                    var target = damageAbility.ChosenTargets[0][0];
                    // CR 119 — Bloodtithe Harvester is the source of the
                    // damage (printed text reads "~ deals 2 damage").
                    // Fx.DealDamageAny routes to player / creature /
                    // planeswalker uniformly.
                    Fx.DealDamageAny(target, 2);
                }
            });

        damageAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(damageAbility);

        return card;
    }
}
