using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Warleader's Call (Outlaws of Thunder Junction,
/// {1}{R}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Creatures you control get +1/+1.
///    Whenever a creature you control enters, this enchantment deals 1
///    damage to each opponent."
///
/// The base shape (name, single Enchantment card type, {1}{R}{W}) is
/// materialised from the embedded JSON definition
/// (<c>warleaders-call.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="HonorOfThePureFactory"/> / <see cref="IntangibleVirtueFactory"/>.
/// The continuous anthem and the ETB-damage trigger are layered on here
/// because the JSON schema doesn't express continuous static effects or
/// ETB triggers.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {1}{R}{W}, owner / controller
///   wiring.
/// - <b>Anthem (+1/+1) — CR 613.7c</b>: "Creatures you control get +1/+1."
///   Registered as a <see cref="ControllerCreatureAnthemEffect"/> with no
///   colour gate (all creatures the controller controls), Layer 7c P/T
///   modification. <see cref="ControllerCreatureAnthemEffect.IsActive"/>
///   short-circuits when Warleader's Call isn't on the battlefield so the
///   bonus lifts on LTB (CR 614). Warleader's Call is an Enchantment (not a
///   Creature), so the includeSelf question is moot. Same anthem shape as
///   Glorious Anthem / Mirari's Wake.
/// - <b>ETB-damage trigger — CR 603.1 + CR 603.6a</b>: "Whenever a creature
///   you control enters, this enchantment deals 1 damage to each opponent."
///   Modeled with <see cref="Triggers.OnAnotherCreatureYouControlEnters"/>
///   (battlefield entry of a creature OTHER than this card under this
///   controller). Because Warleader's Call is an Enchantment — never the
///   moved creature — the "another"-shaped predicate is functionally
///   identical to "a creature you control enters" here. On resolution the
///   enchantment deals 1 damage to each opponent (CR 119 — damage to a
///   player), routed through <see cref="Fx.DealDamageAny"/> against the
///   injected <c>opponentResolver</c> (the Player aggregate exposes no
///   opponents list at v1; same resolver-injection pattern as
///   <see cref="GlaringFleshrakerFactory"/> / Voldaren Epicure).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered anthem stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffectsService"/>-side <c>IsActive</c> gates it off
///   when the source isn't on the battlefield, but a future Prune pass could
///   drop the entry. Same shape as Honor of the Pure / Intangible Virtue.
/// - <b>Live "each opponent" enumeration</b>: no <c>Player.Opponents</c>
///   accessor at v1; resolver-injection shared with Glaring Fleshraker /
///   Voldaren Epicure. Single-arg <see cref="Create(Player)"/> no-ops the
///   burn half.
/// - <b>Control-change re-evaluation</b>: controller is captured via
///   <see cref="Permanent.Controller"/> on the source at AppliesTo time, so
///   a control change of Warleader's Call is reflected lazily; same caveat
///   posture as the other anthem factories.
/// </summary>
[CardName("Warleader's Call")]
public static class WarleadersCallFactory
{
    public const string CardName = "Warleader's Call";
    public const string Slug = "warleaders-call";
    public const int EtbDamageAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Warleader's Call with no live runtime services. Suitable for
    /// shape / dispatcher tests — the anthem is not registered (no creatures
    /// receive +1/+1), the ETB trigger is attached structurally but not
    /// enrolled with a <see cref="TriggerManager"/>, and the burn half no-ops
    /// (no opponent resolver). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct a fully-wired Warleader's Call.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// all-creatures anthem (+1/+1) against. May be null — no live bonus.</param>
    /// <param name="triggers">Trigger manager for registration. May be null —
    /// the ETB trigger attaches structurally but is not enrolled.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent" for
    /// the ETB-damage trigger. Without a resolver the burn half no-ops.</param>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {1}{R}{W}) from the embedded JSON def.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c — "Creatures you control get +1/+1." Layer 7c P/T
            // modification scoped to the controller's battlefield, no colour
            // gate (all creatures). Same shape as Glorious Anthem.
            continuousEffects.Register(new ControllerCreatureAnthemEffect(
                source: card,
                power: 1,
                toughness: 1,
                includeSelf: false));
        }

        // ----------------------------------------------------------------
        // ETB-damage trigger — CR 603.1 + CR 603.6a.
        //   "Whenever a creature you control enters, this enchantment deals
        //    1 damage to each opponent."
        // Warleader's Call is an Enchantment, never the moved creature, so
        // the "another"-shaped predicate (Triggers.OnAnotherCreatureYouControl-
        // Enters) is functionally identical to "a creature you control
        // enters" here. On resolution the enchantment deals 1 damage to each
        // opponent (CR 119 — damage to a player), via the resolver-injection
        // pattern (Glaring Fleshraker / Voldaren Epicure shape).
        // ----------------------------------------------------------------
        var damageEffect = new Effect(
            $"{CardName}: deal {EtbDamageAmount} damage to each opponent (a creature you control entered)",
            () =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    Fx.DealDamageAny(opp, EtbDamageAmount);
                }
            });

        var entersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAnotherCreatureYouControlEnters(owner, card),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(entersTrigger);
        triggers?.RegisterTriggeredAbility(entersTrigger);

        return card;
    }
}
