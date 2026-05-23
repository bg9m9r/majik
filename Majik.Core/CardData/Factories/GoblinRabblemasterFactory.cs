using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Rabblemaster (Magic 2015 / many reprints,
/// Creature — Goblin Warrior {2}{R}).
///
/// Oracle text:
///   "Other Goblin creatures you control have haste.
///    Whenever Goblin Rabblemaster attacks, create a 1/1 red Goblin
///    creature token, then it gets +1/+0 until end of turn for each
///    attacking Goblin you control."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Warrior, mana cost {2}{R}, owner/controller wired.
/// - <b>"Other Goblin creatures you control have haste"</b> wired via
///   <see cref="LordStaticEffect"/> — <c>matchingSubtype: Goblin</c>,
///   <c>power: 0, toughness: 0</c>, <c>grantedKeywords: ["Haste"]</c>,
///   <c>includeSelf: false</c>. Same shape as Goblin Chieftain's static
///   minus the +1/+1 stat bump. Scoped to the controller's battlefield
///   (CR 109.5 — "you"). Lifts on LTB via
///   <see cref="LordStaticEffect.IsActive"/>'s battlefield gate.
/// - <b>Attack triggered ability (CR 508.1f)</b> wired via
///   <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>.
///   On resolution:
///     1. CR 111 / 111.6 — create one 1/1 Goblin creature token under
///        Rabblemaster's controller via
///        <see cref="TokenFactory.CreateOnBattlefield"/>.
///     2. Read the live attackers list via the supplied
///        <c>attackingCreaturesSource</c> closure, count every attacking
///        Goblin the controller controls (INCLUDING Rabblemaster itself —
///        the oracle says "each attacking Goblin you control", no "other"
///        qualifier on this rider), and register a
///        <see cref="PumpUntilEndOfTurnEffect"/> on Rabblemaster for
///        +N/+0 EOT.
///   The newly-created token isn't attacking (it was just made — no
///   declare-attackers step in this trigger), so it doesn't contribute to
///   the count. This matches the printed semantics:
///   "create token, THEN it gets +1/+0 for each attacking Goblin" — the
///   "then" clause reads the snapshot after token creation but the token
///   itself isn't an attacker yet (CR 508 — attackers were declared in
///   the Declare Attackers step before this trigger resolves).
///
/// ## Source closure injection
/// Same shape as <see cref="GoblinPiledriverFactory"/> — the engine
/// doesn't yet expose a global "currently attacking creatures" view from
/// inside the effect closure, so the factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Creature&gt;&gt;</c> closure that callers
/// (Game / tests) populate with the live attacker list. When null, the
/// pump body still creates the token but the +N/+0 EOT is a no-op
/// (Rabblemaster stays at base 2/2). Suitable for shape / dispatcher
/// tests.
///
/// ## Deferred (v1 gaps)
/// - <b>Token colour identity (red)</b>: tokens are created as colourless
///   under the v1 token shape — same gap as Pact of the Titan's "red"
///   Giant and Crashing Footfalls' "green" Rhinos. Subtype + P/T + token
///   flag are correct; CardColors plumbing for tokens is the broader fix.
/// - <b>Live combat-attackers provider</b>: production callers must wire
///   the closure manually. Once <c>ICurrentCombatProvider</c> ships, this
///   factory will read attackers off the live provider directly. Same
///   caveat as <see cref="GoblinPiledriverFactory"/>.
/// - <b>LTB unregister for the lord static</b>: the registered
///   <see cref="LordStaticEffect"/> stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Rabblemaster isn't on the battlefield so the granted Haste lifts
///   correctly, but a future Prune pass could drop the entry. Same shape
///   as Goblin Chieftain.
/// - <b>Trigger-on-stack timing</b>: the token + pump are registered
///   immediately when the trigger effect runs. Real MTG semantics put the
///   trigger on the stack and resolve it before blockers are declared;
///   v1 collapses this to the trigger-resolves-now shape (observationally
///   equivalent for the +N/+0 read at damage step).
/// </summary>
public static class GoblinRabblemasterFactory
{
    public const string CardName = "Goblin Rabblemaster";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Goblin Rabblemaster with no live runtime services.
    /// Suitable for card-shape / dispatcher tests — the lord static effect
    /// is NOT registered (no layers service) and the attack-trigger pump
    /// body is a no-op (no attackers source). Token creation is also a
    /// no-op without a controller-battlefield wired up via the
    /// closure-driven path. The attack trigger is still attached to the
    /// card shape so <see cref="ICard.Abilities"/> includes it.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(
            owner,
            continuousEffects: null,
            triggers: null,
            attackingCreaturesSource: null,
            zoneService: null);

    /// <summary>
    /// Construct a fully-wired Goblin Rabblemaster.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// "Other Goblins you control have haste" <see cref="LordStaticEffect"/>
    /// against. May be null — no live grant.</param>
    /// <param name="triggers">TriggerManager to register the attack
    /// trigger against. May be null — trigger is still attached to the
    /// card shape.</param>
    /// <param name="attackingCreaturesSource">Closure returning the
    /// current attacker creature list. Called at trigger resolution. May
    /// be null — pump body is a no-op (token is still created).</param>
    /// <param name="zoneService">Optional zone service so token-ETB
    /// CardMovedEvent fires (Soul Warden etc.). Pass <c>null</c> for raw
    /// zone moves.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource,
        ZoneService? zoneService = null)
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

        // CR 613.1f — "Other Goblin creatures you control have haste."
        // includeSelf:false; power/toughness both 0 — keyword-only grant.
        // Same shape as Goblin Chieftain minus the +1/+1.
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Goblin,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "Haste" },
                includeSelf: false,
                opponentsOnly: false));
        }

        // CR 508.1f — "Whenever Goblin Rabblemaster attacks, create a 1/1
        // red Goblin creature token, then it gets +1/+0 until end of turn
        // for each attacking Goblin you control."
        var attackEffect = new Effect(
            $"{CardName}: create 1/1 Goblin token, then +1/+0 EOT per attacking Goblin you control",
            () =>
            {
                // 1. CR 111 / 111.6 — create the 1/1 Goblin token. Always
                // happens, regardless of whether the attackers source is
                // wired (the token half is unconditional).
                CreateGoblinToken(card.Controller ?? owner, zoneService);

                // 2. CR 508.1f pump rider — +1/+0 EOT for each attacking
                // Goblin you control. INCLUDES Rabblemaster itself (the
                // oracle has no "other" qualifier on this clause; contrast
                // Goblin Piledriver which says "each OTHER attacking
                // Goblin"). The newly-created token isn't attacking
                // (attackers were declared before this trigger resolved)
                // so it doesn't contribute. Same closure shape as
                // GoblinPiledriverFactory.
                if (attackingCreaturesSource == null) return;
                if (card.ActiveEffects == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                int attackingGoblins = 0;
                var controller = card.Controller ?? owner;
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!ReferenceEquals(atk.Controller, controller)) continue;
                    if (!atk.HasSubtype(CardSubtype.Goblin)) continue;
                    attackingGoblins++;
                }

                if (attackingGoblins == 0) return;
                card.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(card, attackingGoblins, 0));
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.6 — create one 1/1 Goblin creature token under
    /// <paramref name="controller"/>'s control.
    /// </summary>
    public static Creature CreateGoblinToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            Keywords: null);

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
