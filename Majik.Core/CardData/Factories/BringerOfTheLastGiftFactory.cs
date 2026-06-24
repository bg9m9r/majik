using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bringer of the Last Gift (Modern Horizons 3,
/// {6}{B}{B}).
///
/// Creature — Vampire Demon {6}{B}{B} 6/6. Oracle text:
///   "Flying
///    When this creature enters, if you cast it, each player sacrifices
///    all other creatures they control. Then each player returns all
///    creature cards from their graveyard that weren't put there this way
///    to the battlefield."
///
/// ## Shape source
/// Card identity (name, {6}{B}{B}, 6/6, Creature — Vampire Demon, Flying)
/// is loaded from <c>Majik.Core/CardData/Cards/bringer-of-the-last-gift.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/> (the JSON <c>keywords</c>
/// array stamps the Flying <see cref="KeywordAbility"/> marker, CR 702.9,
/// read by <see cref="Combat.CombatAbilities.HasFlying"/>). The bespoke ETB
/// triggered ability is attached in code below — its three-phase
/// per-player body (gate + mass-sac-others + mass-return) is not expressible
/// in the JSON ability schema, so it is hand-rolled here, mirroring the
/// suggested analogue <see cref="LivingEndFactory"/> (mass sacrifice +
/// mass return-from-graveyard) with the "if you cast it" cast gate added.
///
/// ## ETB body (CR 603.6a / CR 603.7e)
/// The trigger fires on the self-ETB (<see cref="Triggers.OnEnterBattlefieldSelf"/>).
/// Its effect runs three sequential clauses (CR 608.2c — strict order):
///
/// <list type="number">
///   <item><b>"if you cast it" gate (CR 603.7e — reflexive intervening-if).</b>
///         The body short-circuits unless the permanent's persistent
///         <see cref="Majik.Core.Cards.Card.WasCast"/> stamp is set
///         (written by <see cref="Majik.Core.Game.SpellCastFlow"/> at stack
///         push, cleared by <see cref="ZoneService"/> on LTB). When Bringer
///         arrives via reanimation / Show and Tell / blink the trigger still
///         goes on the stack but does nothing on resolution — same posture as
///         <see cref="TheOneRingFactory"/>'s cast-gated ETB rider.</item>
///   <item><b>"each player sacrifices all other creatures they control"
///         (CR 701.16).</b> For every player, every creature they control
///         EXCEPT Bringer itself is sacrificed to its owner's graveyard. The
///         "all OTHER creatures" exclusion is by reference identity against
///         the Bringer permanent (CR 109.x — "other" excludes the source).
///         Snapshot-then-sacrifice avoids collection-mutation during
///         iteration (mirrors <see cref="AllIsDustFactory"/>).</item>
///   <item><b>"Then each player returns all creature cards from their
///         graveyard that weren't put there this way to the battlefield."</b>
///         The graveyards are snapshotted BEFORE the sacrifices (clause 2),
///         so the creatures sacrificed by this very effect are excluded by
///         construction ("that weren't put there this way") — exactly the
///         step-1-snapshot idiom of <see cref="LivingEndFactory"/>. Each
///         returned creature enters under the control of the player whose
///         graveyard it came from (CR 110.2 — "their battlefield"). Moves
///         route through <see cref="ZoneService.MoveCard"/> when supplied so
///         <see cref="Events.CardMovedEvent"/> publishes and ETB triggers
///         fire (CR 603.6a); absent a service the moves fall back to raw-zone
///         shuffles (matches <see cref="LivingEndFactory.BuildSpellDefinition"/>).</item>
/// </list>
///
/// ## APNAP order (CR 101.4)
/// The body honours the supplied player order (callers pass
/// <c>[apActive, nap1, …]</c>). Each clause runs over every player before the
/// next clause begins, matching the oracle's "each player … Then each player …".
///
/// ## Deferred (v1 gaps)
/// - <b>In-player sacrifice / return ordering prompt</b> (CR 701.16b /
///   CR 603.3d): "all other creatures" / "all creature cards" sweep every
///   eligible object, so the final game state is order-independent; v1 emits
///   moves in zone-iteration order without an agent prompt — same posture as
///   <see cref="AllIsDustFactory"/> and <see cref="LivingEndFactory"/>.
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches the
///   ETB trigger to the card but does NOT register it with a
///   <see cref="TriggerManager"/>; the (owner, triggers) overload registers it
///   so bus-driven firing works end-to-end.
/// </summary>
[CardName("Bringer of the Last Gift")]
public static class BringerOfTheLastGiftFactory
{
    public const string CardName = "Bringer of the Last Gift";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bringer-of-the-last-gift");

    /// <summary>
    /// Construct Bringer of the Last Gift with its ETB trigger attached to
    /// the card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Bringer of the Last Gift with optional
    /// <see cref="TriggerManager"/> wiring. When <paramref name="triggers"/>
    /// is supplied, the ETB trigger is registered so the relevant ETB event
    /// places it on the stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, if you cast it, each player
        //    sacrifices all other creatures they control. Then each player
        //    returns all creature cards from their graveyard that weren't
        //    put there this way to the battlefield."
        //
        // The body reads AllPlayers off the resolution context's GameContext
        // at resolve time (CR 608.2 — ctx.Game?.AllPlayers idiom, mirrors
        // BezaTheBoundingSpringFactory). Without a live GameContext (shape
        // tests) the body falls back to the single owner. Tests drive the
        // full per-player body with a ZoneService via BuildEtbEffect directly.
        // The live-wired path has no ZoneService on the resolution context, so
        // moves fall back to raw-zone shuffles — the visible game state is
        // identical; only downstream CardMovedEvent publication differs (same
        // contract as LivingEndFactory.BuildSpellDefinition's null-zones path).
        // ----------------------------------------------------------------
        var etbEffect = Fx.Inline(
            $"{CardName}: each player sacs all OTHER creatures, then returns creatures from grave (gated on WasCast)",
            rc =>
            {
                if (!card.WasCast) return ValueTask.CompletedTask; // CR 603.7e — "if you cast it" gate.

                var players = rc.Game?.AllPlayers is { Count: > 0 } all
                    ? all
                    : new[] { card.Controller ?? owner };

                ApplyEtb(card, players, zones: null);
                return ValueTask.CompletedTask;
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Build the ETB body as a standalone effect over an explicit player
    /// list — the unit-testable seam (mirrors
    /// <see cref="AllIsDustFactory.BuildResolveEffect"/>). Does NOT apply the
    /// "if you cast it" gate; callers that want the gate go through
    /// <see cref="Create(Player, TriggerManager?)"/>. Supplying
    /// <paramref name="zones"/> routes every move through
    /// <see cref="ZoneService.MoveCard"/> so <see cref="Events.CardMovedEvent"/>
    /// publishes; absent a service moves fall back to raw-zone shuffles.
    /// </summary>
    /// <param name="bringer">The Bringer permanent — excluded from the
    /// "all OTHER creatures" sacrifice by reference identity (CR 109.x).</param>
    /// <param name="players">All players, typically in APNAP order
    /// (CR 101.4).</param>
    /// <param name="zones">Optional live zone service for event publication.</param>
    public static IEffect BuildEtbEffect(
        ICard bringer,
        IReadOnlyList<Player> players,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(bringer);
        ArgumentNullException.ThrowIfNull(players);

        return new Effect(
            $"{CardName}: each player sacs all OTHER creatures, then returns creatures from grave",
            () => ApplyEtb(bringer, players, zones));
    }

    /// <summary>
    /// The three-phase per-player body (CR 608.2c). See class xmldoc.
    /// </summary>
    private static void ApplyEtb(
        ICard bringer,
        IReadOnlyList<Player> players,
        ZoneService? zones)
    {
        // -------------------------------------------------------------
        // Snapshot graveyards BEFORE any sacrifice so the creatures
        // sacrificed by clause 2 are excluded from the clause-3 return
        // ("that weren't put there this way" — CR 608.2c). Mirrors
        // LivingEndFactory's step-1 snapshot.
        // -------------------------------------------------------------
        var returnByPlayer = new Dictionary<Player, List<ICard>>(players.Count);
        foreach (var player in players)
        {
            returnByPlayer[player] = player.Zones.Graveyard.GetCards()
                .Where(c => c.HasType(CardType.Creature))
                .ToList();
        }

        // -------------------------------------------------------------
        // Clause 2 — each player sacrifices all OTHER creatures they
        // control (CR 701.16). "Other" excludes Bringer itself by
        // reference identity (CR 109.x). Snapshot-then-sacrifice avoids
        // collection mutation during iteration.
        // -------------------------------------------------------------
        foreach (var player in players)
        {
            var creatures = player.Zones.Battlefield.GetCards()
                .Where(c => c.HasType(CardType.Creature)
                         && !ReferenceEquals(c, bringer)
                         && ReferenceEquals(c.Controller, player))
                .ToList();

            foreach (var creature in creatures)
            {
                Sacrifice(creature, player, zones);
            }
        }

        // -------------------------------------------------------------
        // Clause 3 — each player returns the creature cards snapshotted
        // from their graveyard (pre-sac) to the battlefield under their
        // control (CR 110.2). ZoneService routing makes ETB triggers fire
        // (CR 603.6a).
        // -------------------------------------------------------------
        foreach (var player in players)
        {
            if (!returnByPlayer.TryGetValue(player, out var cards)) continue;
            foreach (var card in cards)
            {
                // A clause-2 sacrifice / replacement could have displaced
                // the card; only return cards still in the graveyard.
                if (card.Zone != ZoneType.Graveyard) continue;

                if (zones != null)
                {
                    zones.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, player);
                }
                else
                {
                    player.Zones.Graveyard.RemoveCard(card);
                    player.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                    card.SetController(player);
                }
            }
        }
    }

    /// <summary>
    /// CR 701.16 — sacrifice <paramref name="creature"/> to its owner's
    /// graveyard. Routes through <paramref name="zones"/> when supplied so
    /// <see cref="Events.CardMovedEvent"/> fires. Sacrifice bypasses
    /// indestructible (CR 702.12b), so this deliberately does not pass
    /// through the destroy gate.
    /// </summary>
    private static void Sacrifice(ICard creature, Player controller, ZoneService? zones)
    {
        var owner = creature.Owner ?? controller;
        if (zones != null)
        {
            zones.MoveCard(creature, ZoneType.Battlefield, ZoneType.Graveyard, owner);
            return;
        }

        controller.Zones.Battlefield.RemoveCard(creature);
        owner.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);
    }
}
