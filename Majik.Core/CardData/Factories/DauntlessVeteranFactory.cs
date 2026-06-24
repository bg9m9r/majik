using System;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dauntless Veteran (Dominaria United, {1}{W}{W}).
/// Creature — Human Soldier 2/2. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Whenever this creature attacks, creatures you control get +1/+1 until
///    end of turn."
///
/// A near-vanilla mono-white attack-trigger anthem. The base shape (name,
/// Creature — Human Soldier, {1}{W}{W}, 2/2) is materialised from the embedded
/// JSON definition (<c>dauntless-veteran.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the lone non-keyword ability — the
/// attack trigger — is layered on in C# (the JSON <c>AbilityDefinition</c> schema
/// does not express a one-shot attack-trigger anthem). Same attack-trigger
/// posture as <see cref="RestlessPrairieFactory"/>, except the anthem here hits
/// <b>all</b> creatures you control (no "other" qualifier — Dauntless Veteran
/// pumps itself too).
///
/// ## "Whenever this creature attacks, creatures you control get +1/+1 until end of turn." (CR 508.1f)
/// A <see cref="TriggeredAbility"/> over <see cref="Triggers.OnAttackSelf"/> —
/// the self-trigger keyed on Dauntless Veteran itself, so it fires once, when the
/// Veteran is declared as an attacker (CR 508.1f). NON-targeted (CR 611 — a
/// one-shot pump, not a continuous static and not a targeted ability), so it
/// carries no <see cref="TargetRequest"/>. On resolution it snapshots the
/// controller's battlefield creatures at that moment (CR 608.2 — effects resolve
/// against current game state) — including the Veteran itself, since there is no
/// "other" qualifier — and registers a +1/+1
/// <see cref="PumpUntilEndOfTurnEffect"/> (CR 613.7c, ExpiresAtEndOfTurn —
/// CR 514.2 cleanup) on each. The snapshot is taken to a list first so any
/// same-step zone moves don't disturb the enumeration (same posture as
/// <see cref="RestlessPrairieFactory"/>). Creatures that enter after resolution
/// do not get the buff.
///
/// ## v1 posture
/// - <b>Pump targets the supplied effects service</b> — the trigger registers
///   each pump into the <see cref="ContinuousEffectsService"/> supplied to
///   <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>,
///   matching the shared-service test posture of
///   <see cref="RestlessPrairieFactory"/>. When the service is null the effect is
///   a clean no-op (the trigger still fires but records nothing).
/// </summary>
[CardName("Dauntless Veteran")]
public static class DauntlessVeteranFactory
{
    public const string CardName = "Dauntless Veteran";
    public const string Slug = "dauntless-veteran";
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    /// <summary>
    /// Construct Dauntless Veteran with no runtime services. Suitable for
    /// card-shape / dispatcher tests — the attack trigger is attached for
    /// inspection but not registered with a bus and (with no effects service) is
    /// a no-op on resolution. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Dauntless Veteran.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register each +1/+1
    /// <see cref="PumpUntilEndOfTurnEffect"/> against on attack. May be null — the
    /// trigger still resolves but records no continuous effect.</param>
    /// <param name="triggers">TriggerManager the attack trigger is registered
    /// with. May be null — the trigger is attached to the card's ability list but
    /// won't fire from the event bus.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Human Soldier,
        // {1}{W}{W}, 2/2). The attack trigger is layered on below — the JSON
        // AbilityDefinition schema does not express a one-shot attack anthem.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Whenever this creature attacks, creatures you control get +1/+1
        //  until end of turn." (CR 508.1f)
        //
        // OnAttackSelf keys on the Veteran itself, so the trigger fires once,
        // when it is declared as an attacker. NON-targeted (CR 611 — a one-shot
        // pump). On resolution it snapshots the controller's battlefield
        // creatures (CR 608.2) — INCLUDING the Veteran, no "other" qualifier —
        // and registers a +1/+1 PumpUntilEndOfTurnEffect (CR 613.7c, expires EOT
        // per CR 514.2) on each. Snapshot to a list first (same posture as
        // Restless Prairie).
        // ----------------------------------------------------------------
        var anthemEffect = new Effect(
            $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} until end of turn (CR 508.1f / 613.7c)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                var controller = card.Controller ?? owner;
                var creatures = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();

                foreach (var creature in creatures)
                {
                    // CR 613.7c — +1/+1 with CR 514.2 end-of-turn expiry.
                    effects.Register(new PumpUntilEndOfTurnEffect(
                        creature, PumpPower, PumpToughness));
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { anthemEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
