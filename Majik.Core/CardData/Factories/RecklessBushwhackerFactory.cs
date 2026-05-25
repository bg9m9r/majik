using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Bushwhacker (Oath of the Gatewatch,
/// Creature — Goblin Berserker {1}{R}).
///
/// Oracle text:
///   "Surge {R} (You may cast this spell for its surge cost if you or a
///    teammate has cast another spell this turn.)
///    When this creature enters, if its surge cost was paid, creatures
///    you control get +1/+0 and gain haste until end of turn."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Goblin Berserker, mana cost {1}{R}, owner/controller
///   wired.
/// - <b>Surge keyword marker</b> (CR 702.115) wired as a
///   <see cref="KeywordAbility"/>. The engine doesn't yet ship a Surge
///   alt-cost primitive (no <c>SurgeAdditionalCost</c> / probe registered
///   in <see cref="Players.Agents.AlternativeCostProbeRegistry"/>), so the
///   keyword is shape-only — Reckless Bushwhacker still casts at its
///   printed {1}{R} and the surge-paid ETB rider remains dormant until
///   the primitive lands.
/// - <b>ETB triggered ability (CR 603.6a)</b> wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. The intervening-if
///   gates on the per-cast surge flag (<c>WasCastForSurge</c>) — when
///   true at queue + resolve time, every creature the controller
///   controls receives +1/+0 EOT and a Haste grant until end of turn
///   (same shape as <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>).
///   Today the flag is never stamped (no Surge primitive), so the trigger
///   structurally exists but is observationally dormant; once
///   <c>SurgeAdditionalCost</c> ships and stamps the flag on cast, the
///   ETB will light up without further factory changes.
///
/// ## Flag-carry pattern
/// Mirrors <see cref="BurstLightningFactory"/>'s <see cref="Card.WasKicked"/>
/// posture: the factory reads a per-card boolean stamped by the alt-cost
/// payment, and the ETB intervening-if + effect body both consult it. The
/// expected flag is <c>WasCastForSurge</c> — when added to
/// <see cref="Card"/>, the Surge probe stamps it during
/// <see cref="Costs.IAdditionalCost.Pay"/>. Until that flag exists this
/// factory checks a static surge-paid stub (always <c>false</c>) so the
/// trigger compiles + is well-formed; replacing the stub with
/// <c>card.WasCastForSurge</c> is a one-line change.
///
/// ## Implemented v1 — pump body (always wired, gated by flag)
/// The pump-team body uses the same Layer 7c +P/+T + Layer 6 keyword-
/// grant rider as Violent Outburst (+1/+0 + Haste), snapshotting the
/// controller's battlefield creatures at resolution (CR 608.2) and
/// registering both effects on each. Newly-created creatures entering
/// AFTER the trigger resolves are not pumped — only the snapshot at
/// resolve time benefits, matching MTG semantics.
///
/// ## Deferred (v1 gaps)
/// - <b>Surge primitive</b>: needs <see cref="Card.WasCastForSurge"/> +
///   <c>SurgeAdditionalCost</c> + a <c>SurgeAltCostProbe</c> registered
///   in <see cref="Players.Agents.AlternativeCostProbeRegistry"/>. Until
///   then the alt-cast lane is unavailable and the surge-paid ETB body
///   doesn't fire. When the primitive lands, swap the <c>SurgePaid</c>
///   stub for <c>card.WasCastForSurge</c> in both the intervening-if and
///   the inline guard.
/// - <b>"teammate" multiplayer wiring</b>: CR 702.115a refers to
///   teammates in multiplayer; v1 will read only the caster's own "has
///   cast another spell this turn" stat (Storm-style) — the teammate
///   half is irrelevant for 1v1 and gated to multiplayer roll-out.
/// </summary>
[CardName("Reckless Bushwhacker")]
public static class RecklessBushwhackerFactory
{
    public const string CardName = "Reckless Bushwhacker";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const int PumpPower = 1;
    public const int PumpToughness = 0;
    public const string GrantedKeyword = "Haste";
    public const string SurgeCostText = "{R}";

    /// <summary>
    /// Construct Reckless Bushwhacker with no live TriggerManager.
    /// Suitable for card-shape / dispatcher tests — the ETB trigger is
    /// attached to the card shape but not registered.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Reckless Bushwhacker with optional trigger registration.
    /// When <paramref name="triggers"/> is supplied, the surge-gated ETB
    /// trigger is registered.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger is still attached to the card
    /// shape so <see cref="ICard.Abilities"/> includes it.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Berserker });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.115 — Surge keyword marker so data-side tools see it.
        // Engine alt-cost primitive (SurgeAdditionalCost +
        // SurgeAltCostProbe) hasn't shipped yet; today this is shape-only.
        card.AddAbility(new KeywordAbility("Surge", card, owner));

        // CR 603.6a — ETB triggered ability.
        //   "When this creature enters, if its surge cost was paid,
        //    creatures you control get +1/+0 and gain haste until end of
        //    turn."
        // The intervening-if gates on the per-cast surge-paid flag (today
        // a stub — see class xmldoc). When the Surge primitive lands and
        // stamps Card.WasCastForSurge, replace SurgePaid(card) with
        // card.WasCastForSurge in both call sites.
        var etbEffect = new Effect(
            $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} and gain {GrantedKeyword} until end of turn (if surge paid)",
            () =>
            {
                // CR 603.6e — re-check the intervening-if at resolve
                // time. Defensive: TriggeredAbility.CanBePutOnStack
                // already vetted this when the trigger was queued, but
                // the rule re-checks at resolution.
                if (!SurgePaid(card)) return;

                ApplyPumpAndHaste(card.Controller ?? owner);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.6e — intervening-if checks at both queue time
            // (CanBePutOnStack) and resolve time (the inline guard
            // above). Queue-time check uses the same SurgePaid stub.
            interveningIf: () => SurgePaid(card),
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Was this Bushwhacker cast for its surge cost? Today the engine
    /// has no Surge primitive (no <c>Card.WasCastForSurge</c>, no
    /// <c>SurgeAdditionalCost</c>), so this returns <c>false</c>
    /// unconditionally — the ETB pump-team body never fires. Once the
    /// primitive ships, swap this for a one-liner that reads
    /// <c>card.WasCastForSurge</c>.
    ///
    /// Exposed as <c>internal</c> so tests can stub-swap if they ever
    /// need to drive the surge-paid branch via reflection (none do
    /// today).
    /// </summary>
    internal static bool SurgePaid(Card card)
    {
        // Stub until Card.WasCastForSurge ships. See class xmldoc.
        _ = card;
        return false;
    }

    /// <summary>
    /// Apply Reckless Bushwhacker's surge-paid pump+haste rider to every
    /// creature <paramref name="controller"/> controls at the moment this
    /// effect runs. CR 608.2 — effects resolve against current game
    /// state, so the snapshot is taken at resolution. Same shape as
    /// <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>; only the
    /// pump magnitudes differ (+1/+0 vs Outburst's +1/+0 — coincidentally
    /// identical) and the keyword (Haste in both cases).
    /// </summary>
    public static void ApplyPumpAndHaste(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list before applying so any same-step zone-move
        // side effects don't disturb the enumeration. Mirrors Violent
        // Outburst / Pyroclasm posture.
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService
            // wired onto the creature, the pump/haste body silently
            // no-ops rather than NRE'ing. Mirrors Violent Outburst's
            // defensive guard.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 7c — +1/+0 pump.
            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));

            // CR 613.1c Layer 6 — keyword grant: Haste (CR 702.10).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
        }
    }
}
