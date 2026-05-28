using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Weapons Manufacturing (Aetherdrift, {1}{R}).
///
/// Enchantment. Oracle text (Scryfall):
///   "Whenever a nontoken artifact you control enters, create a colorless
///    artifact token named Munitions with 'When this token leaves the
///    battlefield, it deals 2 damage to any target.'"
///
/// ## Implemented (v1)
///
/// - <b>Enchantment {1}{R}</b> — printed mana cost, owner / controller wired.
///   No subtypes; no supertypes. Mana value 2.
///
/// - <b>"Whenever a nontoken artifact you control enters" trigger
///   (CR 603.1)</b>: fires off <see cref="CardMovedEvent"/> with
///   predicate:
///   <list type="bullet">
///     <item><c>e.ToZone == Battlefield</c> (ETB).</item>
///     <item><c>e.Card.HasType(Artifact)</c> — includes Artifact Creatures
///       per CR 301.1 / CR 205.3.</item>
///     <item><c>!(e.Card is Permanent p &amp;&amp; p.IsToken)</c> — nontoken
///       filter (CR 111.5). Non-Permanent cards trivially pass (tokens
///       are always Permanents).</item>
///     <item><c>e.Card.Controller == Weapons Manufacturing's controller</c>
///       — "you control" (CR 109.5).</item>
///   </list>
///   Active only while Weapons Manufacturing is on the battlefield
///   (<c>activeZones</c> gate — CR 603.10c).
///
///   On resolution: create one colorless Munitions artifact token under
///   Weapons Manufacturing's controller (see § Munitions token below).
///
/// - <b>Munitions token (CR 111.1 / CR 111.4)</b>: a colorless artifact
///   token named "Munitions" with a single LTB triggered ability —
///   "When this token leaves the battlefield, it deals 2 damage to any
///   target." The LTB trigger fires off <see cref="CardMovedEvent"/> when
///   <c>e.Card == token</c> AND <c>e.FromZone == Battlefield</c>
///   (covers dying / exile / bounce — CR 603.6c). On resolution the
///   chosen target (<see cref="TriggeredAbility.ChosenTargets"/>[0][0],
///   pre-supplied for tests / bot-driven resolution) receives 2 damage
///   via <see cref="Fx.DealDamageAny"/>. No chosen target → clean no-op
///   (CR 608.2b posture shared with Geralf's Messenger / Skyclave
///   Apparition).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape-only path. The ETB trigger is
///   attached but NOT registered with a <see cref="TriggerManager"/>.
///   Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. The ETB trigger is registered with <paramref name="triggers"/>
///   when supplied; <paramref name="zoneService"/> is threaded into
///   <see cref="CreateMunitionsToken"/> so the token's ETB publishes a
///   <see cref="CardMovedEvent"/> (enabling downstream triggers like Soul
///   Warden).
///
/// ## Affinity-deck role
///
/// Weapons Manufacturing is the payoff engine in the Affinity / artifact
/// storm shell: every nontoken artifact entering — Ornithopter, Memnite,
/// Signal Pest, Springleaf Drum, Arcbound Worker, Cranial Plating — drops
/// a Munitions token. Each Munitions leaving (blocks, dies, is sacrificed,
/// is bounced) converts into 2 damage — the shell has both reach (burn the
/// opponent) and combat bonus (chump-blocks ping back 2).
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Any target" agent prompt</b>: v1 honours pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; no chosen target at
///   resolution → the 2-damage effect no-ops (mirrors Geralf's Messenger /
///   Valakut posture). An agent-driven "choose any target" prompt is the
///   natural follow-up (same gap as Jitte's mode-1 activation).
/// - <b>LTB trigger registration per token</b>: each Munitions token
///   receives its own <see cref="TriggerManager"/> registration when
///   <paramref name="triggers"/> is supplied. The token exits the
///   battlefield (via graveyard / exile / library) and the LTB trigger
///   fires. The <c>activeZones = Battlefield</c> plus the "looks back"
///   semantics of CR 603.6d ensure the trigger resolves with the token as
///   it last existed on the battlefield.
/// </summary>
[CardName("Weapons Manufacturing")]
public static class WeaponsManufacturingFactory
{
    public const string CardName = "Weapons Manufacturing";
    public const string PrintedManaCost = "{1}{R}";
    public const string MunitionsTokenName = "Munitions";
    public const int MunitionsDamageAmount = 2;

    /// <summary>
    /// Construct Weapons Manufacturing with no live wiring. The ETB-artifact
    /// trigger is attached to the card for shape observability but NOT
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Weapons Manufacturing with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the "nontoken artifact enters →
    /// create Munitions" trigger is registered for bus-driven firing. Also
    /// threaded into <see cref="CreateMunitionsToken"/> so each minted
    /// Munitions token's LTB trigger is registered.</param>
    /// <param name="zoneService">When supplied, the Munitions token's ETB
    /// and subsequent zone moves go through the zone service so
    /// <see cref="CardMovedEvent"/> fires (enabling downstream triggers and
    /// the Munitions token's own LTB registration path).</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Triggered ability — CR 603.1.
        //   "Whenever a nontoken artifact you control enters, create a
        //    colorless artifact token named Munitions with 'When this token
        //    leaves the battlefield, it deals 2 damage to any target.'"
        //
        // Fires on CardMovedEvent → Battlefield; filters:
        //   - HasType(Artifact)          — includes Artifact Creatures
        //   - NOT a token                — nontoken filter (CR 111.5)
        //   - Controller == owner        — "you control" (CR 109.5)
        //
        // Active only while Weapons Manufacturing is on the battlefield
        // (CR 603.10c — loses watchers when it leaves play).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Artifact)
            && !(e.Card is Permanent perm && perm.IsToken)
            && ReferenceEquals(e.Card.Controller, card.Controller ?? owner));

        var etbEffect = new Effect(
            $"{CardName}: create a Munitions token (nontoken artifact entered under controller)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var controller = card.Controller ?? owner;
                CreateMunitionsToken(controller, triggers, zoneService);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Mint a Munitions token for <paramref name="controller"/> and put it
    /// onto the battlefield. Optionally wires the token's LTB trigger with
    /// the supplied <see cref="TriggerManager"/> so the 2-damage burn fires
    /// when the token leaves play.
    ///
    /// <para>Shape: colorless artifact token named "Munitions", no subtypes,
    /// no creature type, no power/toughness — it is a pure Artifact token
    /// (CR 111.4 — printed oracle "colorless artifact token").</para>
    ///
    /// <para>LTB trigger (CR 603.6c): <see cref="CardMovedEvent"/> where
    /// <c>e.Card == token</c> AND <c>e.FromZone == Battlefield</c>.
    /// Effect: deal 2 damage to the chosen any-target via
    /// <see cref="Fx.DealDamageAny"/>. Target is pre-supplied in
    /// <see cref="TriggeredAbility.ChosenTargets"/>; no chosen target
    /// → no-op (CR 608.2b).</para>
    /// </summary>
    public static Artifact CreateMunitionsToken(
        Player controller,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var token = new Artifact(MunitionsTokenName, "")
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.4 — colorless artifact token; explicit empty color override
        // so CardColors.GetColors returns the authoritative colorless set.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c.
        //   "When this token leaves the battlefield, it deals 2 damage to
        //    any target."
        //
        // Condition: CardMovedEvent where the moving card IS this token
        // AND the from-zone is Battlefield (covers dies / exile / bounce).
        // ActiveZones = Battlefield + "looks back" semantics (CR 603.6d)
        // — the trigger fires when the token moves OUT of the battlefield.
        // ----------------------------------------------------------------
        TriggeredAbility? ltbTrigger = null;

        var ltbEffect = new Effect(
            $"{MunitionsTokenName}: deal {MunitionsDamageAmount} damage to any target (LTB)",
            () =>
            {
                if (ltbTrigger == null) return;
                if (ltbTrigger.ChosenTargets.Count == 0) return;
                if (ltbTrigger.ChosenTargets[0].Count == 0) return;

                var target = ltbTrigger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, MunitionsDamageAmount);
            });

        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, token)
                      && e.FromZone == ZoneType.Battlefield);

        ltbTrigger = new TriggeredAbility(
            source: token,
            controller: controller,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        token.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        // Put the token onto the battlefield via the sentinel-library pattern
        // shared by TokenFactory.CreateTreasure / CreateFood / CreateClue.
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);

        if (zoneService != null)
        {
            zoneService.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        return token;
    }
}
