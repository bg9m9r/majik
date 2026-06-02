using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Myr Battlesphere (New Phyrexia / Magic 2011, {7}).
///
/// Artifact Creature — Myr Construct, 4/7. Oracle text (Scryfall verified
/// 2026-06-02):
///   "When this creature enters, create four 1/1 colorless Myr artifact
///    creature tokens.
///    Whenever this creature attacks, you may tap X untapped Myr you control.
///    If you do, this creature gets +X/+0 until end of turn and deals X damage
///    to the player or planeswalker it's attacking."
///
/// ## Shape source
/// Identity (name, {7}, 4/7, Creature + Artifact types, Myr + Construct
/// subtypes) is loaded from
/// <c>Majik.Core/CardData/Cards/myr-battlesphere.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Both triggered abilities are attached
/// in code below — the JSON ability schema does not express "create token",
/// variable-X tap costs, or pump-and-damage attack riders.
///
/// ## Implemented (v1)
/// - 4/7 Artifact Creature — Myr Construct at {7}; owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "create four 1/1 colorless Myr
///   artifact creature tokens." Keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; on resolution mints four
///   tokens via <see cref="TokenFactory.CreateOnBattlefield"/> (1/1, colorless,
///   Myr subtype) and additively stamps <see cref="CardType.Artifact"/> on each
///   (CR 111.4 — "artifact creature token"; same Artifact-stamp posture as
///   <see cref="EsikasChariotFactory"/>). Token creation routes through a
///   supplied <see cref="ZoneService"/> so each token's ETB CardMovedEvent
///   publishes for downstream listeners (Soul Warden etc.).
/// - <b>Attack triggered ability (CR 508.1f)</b>: "you may tap X untapped Myr
///   you control. If you do, this creature gets +X/+0 until end of turn and
///   deals X damage to the player or planeswalker it's attacking." The attacked
///   player/planeswalker is captured off the live
///   <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/> in the
///   condition closure (same per-fire capture pattern as
///   <see cref="HellriderFactory"/>). On resolution:
///   X = the number of untapped Myr the controller controls (the printed cost
///   "you may tap X untapped Myr" — v1 chooses the maximum X, tapping every
///   untapped Myr; see Deferred). The Battlesphere itself, having just been
///   declared as an attacker, is tapped (no Vigilance) and so is naturally not
///   counted. The chosen Myr are tapped (CR 701.21 — tap as cost), the
///   Battlesphere gains +X/+0 until end of turn via a
///   <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c, CR 613.1g; expiry
///   CR 514.2 — same pump primitive as Steppe Lynx / Giant Growth), and X
///   damage is dealt to the captured defender via
///   <see cref="Fx.DealDamageAny"/> (Player → life loss CR 119;
///   Planeswalker → loyalty removal CR 306.7). X=0 (no untapped Myr) is a
///   clean no-op.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both triggers attached for
///   inspection but not registered with any <see cref="TriggerManager"/>;
///   token creation uses raw zone manipulation. The pump no-ops when
///   <see cref="Creature.ActiveEffects"/> is null (mirrors Steppe Lynx).
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — registers
///   both triggers; token ETB events publish through the zone service.
///
/// ## Deferred (v1 gaps)
/// - <b>"you may" / choice of X</b>: the printed ability lets the controller
///   choose how many untapped Myr to tap (0..all). V1 always chooses the
///   maximum X (tap every untapped Myr) — the strictly-dominant line in
///   isolation. A future prompt would let the controller hold Myr back (e.g.
///   to keep blockers / mana). Same auto-maximise posture as other v1
///   "you may pay X" riders.
/// - <b>Token ETB triggers fire only when zoneService supplied</b>: the raw
///   fallback bypasses <see cref="CardMovedEvent"/> (same posture as
///   Grave Titan / Siege-Gang Commander).
/// - <b>Trigger-on-stack timing</b>: the tap/pump/damage runs immediately when
///   the effect executes rather than being queued on the stack with priority
///   in between (mirrors Hellrider / Grave Titan).
/// </summary>
[CardName("Myr Battlesphere")]
public static class MyrBattlesphereFactory
{
    public const string CardName = "Myr Battlesphere";
    public const string Slug = "myr-battlesphere";

    public const int TokenCount = 4;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Myr Battlesphere with both triggers attached for shape
    /// inspection but NOT registered with any <see cref="TriggerManager"/>;
    /// token creation uses raw zone manipulation. Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Myr Battlesphere with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional trigger manager so the ETB and attack
    /// triggers are bus-driven automatically (CR 603.3).</param>
    /// <param name="zoneService">Optional zone service so each spawned Myr
    /// token publishes <see cref="CardMovedEvent"/> on ETB.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this creature enters, create four 1/1 colorless Myr
        //    artifact creature tokens."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create four 1/1 colorless Myr artifact creature tokens",
            () => CreateMyrTokens(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f.
        //   "Whenever this creature attacks, you may tap X untapped Myr you
        //    control. If you do, this creature gets +X/+0 until end of turn
        //    and deals X damage to the player or planeswalker it's attacking."
        // The attacked player/planeswalker is captured off the live event in
        // the condition closure (Hellrider pattern).
        // ----------------------------------------------------------------
        object? capturedDefender = null;

        var attackEffect = new Effect(
            $"{CardName}: tap X Myr — +X/+0 and X damage to the attacked player or planeswalker",
            () =>
            {
                var controller = card.Controller ?? owner;

                // X = number of untapped Myr the controller controls
                // (CR 701.21 — tap as a cost). The Battlesphere itself was
                // tapped when declared as an attacker (no Vigilance) so it is
                // naturally excluded by the untapped filter; the guard below
                // also excludes it defensively.
                var untappedMyr = controller.Zones.Battlefield.GetCards()
                    .OfType<Permanent>()
                    .Where(p => !ReferenceEquals(p, card)
                                && p.HasSubtype(CardSubtype.Myr)
                                && !p.IsTapped)
                    .ToList();

                int x = untappedMyr.Count;
                if (x == 0) return; // "you may" → chose 0; clean no-op.

                // Pay the cost: tap the chosen Myr (CR 701.21).
                foreach (var myr in untappedMyr)
                {
                    Fx.Tap(myr);
                }

                // "this creature gets +X/+0 until end of turn" (CR 613.1g;
                // expiry CR 514.2). No-op when ActiveEffects is null
                // (shape-only tests) — mirrors Steppe Lynx / Giant Growth.
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, x, 0));

                // "deals X damage to the player or planeswalker it's
                // attacking" (CR 119 / CR 306.7).
                if (capturedDefender is not null)
                {
                    Fx.DealDamageAny(capturedDefender, x);
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) =>
                {
                    // CR 508.1f — "whenever THIS creature attacks" self-match.
                    if (!ReferenceEquals(e.Attacker, card)) return false;

                    // CR 506.2 — capture the attacked player/planeswalker for
                    // the resolved effect.
                    capturedDefender = e.DefendingPlayerOrPlaneswalker;
                    return true;
                }),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create four 1/1 colorless Myr artifact creature
    /// tokens under <paramref name="controller"/>'s control. Each token is a
    /// <see cref="Creature"/> with <see cref="CardType.Artifact"/> additively
    /// stamped (CR 301.1 — "artifact creature token") and an explicit empty
    /// colour identity (colorless). Routes through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> so each token publishes a
    /// <see cref="CardMovedEvent"/> when a live <see cref="ZoneService"/> is
    /// supplied.
    /// </summary>
    public static void CreateMyrTokens(Player controller, ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Myr",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Myr },
            Keywords: null,
            // CR 111.4 — explicit empty colour set = colorless.
            Colors: Array.Empty<Majik.Core.ValueObjects.ManaColor>());

        for (int i = 0; i < TokenCount; i++)
        {
            var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
            // CR 301.1 — additively flag the Artifact type so HasType lookups
            // see "artifact creature" (the base token is Creature-only).
            token.AddCardType(CardType.Artifact);
        }
    }
}
