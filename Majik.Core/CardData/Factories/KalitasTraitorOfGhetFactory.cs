using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kalitas, Traitor of Ghet (Oath of the Gatewatch,
/// {2}{B}{B}).
///
/// Legendary Creature — Vampire Knight 3/4. Oracle text:
///   "Lifelink.
///    If a nontoken creature an opponent controls would die, exile it
///    instead and you create a 2/2 black Zombie creature token."
///
/// ## Implemented (v1)
///
/// - 3/4 Legendary Creature — Vampire Knight, mana cost {2}{B}{B}.
/// - <b>Lifelink (CR 702.15)</b>: <see cref="KeywordAbility"/> marker;
///   combat helpers in <see cref="Majik.Core.Combat.CombatAbilities"/>
///   read the marker directly (same shape as
///   <see cref="VaultSkirgeFactory"/>).
/// - <b>Opponent-nontoken-dies trigger (CR 603.1 / CR 700.4)</b>:
///   ships as a triggered-after-death pair (exile from graveyard + spawn
///   token) rather than a true death-replacement effect. The engine does
///   not yet expose a "Battlefield → Graveyard, redirect to Exile +
///   side-effect" replacement primitive (see ZoneMoveIntent gap noted in
///   the planning doc), so we approximate via a triggered ability that
///   fires after the creature has already moved to its owner's graveyard
///   and re-routes it to exile. The visible outcome — the dying creature
///   ends up in exile and Kalitas's controller gets a 2/2 black Zombie —
///   matches the printed text in the common case (no Bloodghast /
///   Bloodsoaked Champion-style "from anywhere" graveyard triggers fire
///   in between because the creature passes through the graveyard for
///   only the triggered-ability evaluation window). Documented gap; the
///   replacement path is a follow-up once ZoneMoveIntent supports
///   redirect-with-side-effect.
///
///   Trigger gating:
///     * Battlefield → Graveyard of a Creature card,
///     * the dying card is NOT a token (CR 111.3 — "nontoken creature"),
///     * the dying card's last controller is NOT Kalitas's controller
///       (printed "an opponent controls"). Matches by Controller, not
///       Owner, so Threaten / Act of Treason-style temporary control
///       changes route correctly per CR 109.5.
///   ActiveZones = {Battlefield, Graveyard} so the trigger still matches
///   after <see cref="ZoneService"/> has stamped the card's Zone =
///   Graveyard before publishing the <see cref="CardMovedEvent"/>
///   (mirrors the Wurmcoil Engine / Matter Reshaper posture).
///
/// - <b>Resolution</b>: exile the just-died creature (move from its
///   owner's graveyard to its owner's exile zone) and create a 2/2
///   black Zombie creature token under Kalitas's controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. The exile step
///   gates on the dying card still being in its owner's graveyard at
///   resolution time (CR 608.2 — "do as much as you can"); if some
///   other effect already moved the card the trigger silently drops the
///   exile step and still spawns the Zombie (printed text uses "and",
///   not "if you do" — the token half is not conditional on the exile
///   succeeding in v1).
///
/// ## Deferred (v1 gaps)
///
/// - <b>True replacement effect</b>: the printed "would die, exile it
///   instead" is a replacement effect (CR 614.6 — "redirection effects").
///   v1 ships as a triggered-after-death approximation; this means
///   dies-trigger payoffs that look at the dying creature's
///   graveyard-state (Bloodghast, Bloodsoaked Champion) will see the
///   creature in the graveyard for one trigger-evaluation window before
///   Kalitas re-routes it to exile. Real Kalitas would prevent the
///   graveyard touch entirely.
/// - <b>"You create" attribution</b>: the printed text reads "you create
///   a 2/2 black Zombie" — token creation is attributed to Kalitas's
///   controller (the trigger's controller per CR 603.1), which v1
///   already routes correctly via the closure capturing
///   <paramref name="owner"/>.
/// </summary>
[CardName("Kalitas, Traitor of Ghet")]
public static class KalitasTraitorOfGhetFactory
{
    public const string CardName = "Kalitas, Traitor of Ghet";
    public const string PrintedManaCost = "{2}{B}{B}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Kalitas with no live runtime services. The triggered
    /// ability is attached to the card shape so dispatcher / structural
    /// tests can observe it; the exile + token effect mutates raw zones
    /// when no <see cref="ZoneService"/> is supplied. For end-to-end
    /// bus-driven firing pass the runtime overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Kalitas with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the dies trigger registers
    /// so a qualifying <see cref="CardMovedEvent"/> automatically queues
    /// the exile + token effect on the stack.
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
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink marker. Combat-side reads via
        // CombatAbilities; the marker keeps the keyword scan surface
        // uniform with Vault Skirge / Heliod's Sun-Crowned anointee.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // Opponent-nontoken-creature-dies trigger — CR 603.1 / CR 700.4.
        //   "If a nontoken creature an opponent controls would die,
        //    exile it instead and you create a 2/2 black Zombie creature
        //    token."
        //
        // v1 approximation: triggered-after-death (exile from graveyard
        // + spawn token) — see factory xmldoc for the replacement-effect
        // gap. The trigger predicate gates on the dying card's last
        // controller being an opponent of Kalitas's controller; the
        // resolve body re-routes the card to exile and creates the
        // Zombie under Kalitas's controller.
        //
        // ActiveZones = {Battlefield, Graveyard} — mirrors the
        // Matter Reshaper / Wurmcoil posture; the trigger's zone-guard
        // must still match after ZoneService has stamped Zone =
        // Graveyard before publishing the CardMovedEvent (CR 603.6d
        // "looks back").
        // ----------------------------------------------------------------
        // Capture the dying card for the resolve body so we can re-route
        // it from graveyard → exile after the trigger goes on the stack.
        // Boxed in a single-element array so the closure can rebind it
        // (mirrors EidolonOfTheGreatRevel's pending-caster pattern).
        var pendingDying = new ICard?[] { null };

        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // Battlefield → Graveyard.
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;

            // Creature, nontoken (CR 111.3).
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (e.Card is Permanent perm && perm.IsToken) return false;

            // "An opponent controls" — the dying card's last controller
            // is not Kalitas's controller. Reads .Controller so
            // Threaten / Act of Treason temporary control changes route
            // correctly per CR 109.5 (the new controller's creature
            // dying counts as theirs).
            if (ReferenceEquals(e.Card.Controller, owner)) return false;

            pendingDying[0] = e.Card;
            return true;
        });

        var diesEffect = new Effect(
            $"{CardName}: exile dying opponent's creature + create 2/2 black Zombie",
            () =>
            {
                var dying = pendingDying[0];
                pendingDying[0] = null;

                // Exile step — gate on the card still being in its
                // owner's graveyard (CR 608.2 — "do as much as you can").
                if (dying != null)
                {
                    var dyingOwner = dying.Owner;
                    if (dyingOwner != null
                        && dying.Zone == ZoneType.Graveyard
                        && dyingOwner.Zones.Graveyard.GetCards().Contains(dying))
                    {
                        if (zoneService != null)
                        {
                            zoneService.MoveCard(
                                dying, ZoneType.Graveyard, ZoneType.Exile);
                        }
                        else
                        {
                            dyingOwner.Zones.Graveyard.RemoveCard(dying);
                            dyingOwner.Zones.Exile.AddCard(dying);
                            dying.SetZone(ZoneType.Exile);
                        }
                    }
                }

                // Token half — "and you create a 2/2 black Zombie
                // creature token." Printed text uses "and", not "if you
                // do"; the token spawn is not conditional on the exile
                // succeeding in v1 (matches "do as much as you can"
                // semantics).
                var spec = new TokenFactory.TokenSpec(
                    Name: "Zombie",
                    Power: 2,
                    Toughness: 2,
                    Subtypes: new[] { CardSubtype.Zombie },
                    // CR 105 / CR 111.4 — printed "2/2 black Zombie".
                    Colors: new[] { ManaColor.Black });

                TokenFactory.CreateOnBattlefield(spec, owner, zoneService);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { diesEffect },
            // ActiveZones = Battlefield + Graveyard (Wurmcoil posture).
            // Kalitas itself lives on the battlefield; the trigger
            // observes opponent-creature deaths so the source's own
            // zone state isn't directly tested, but the wider zone set
            // keeps the registration consistent with the rest of the
            // dies-trigger family (Matter Reshaper, Wurmcoil Engine).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
