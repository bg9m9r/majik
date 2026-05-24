using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Splinter Twin (Rise of the Eldrazi).
///
/// Enchantment — Aura — {2}{R}{R}
/// Oracle text:
///   "Enchant creature
///    Enchanted creature has '{T}: Create a token that's a copy of this
///    creature, except it has haste. Exile the token at the beginning of
///    the next end step.'"
///
/// ## Implementation
///
/// CR 303.4 / 613.1f — Splinter Twin grants an activated ability to the
/// enchanted creature while attached. Lifecycle is wired via
/// <see cref="AttachedAuraAbilityGrantStaticEffect"/>: on attach (and on
/// every <see cref="CardMovedEvent"/> involving the aura), if the aura is
/// on the battlefield with a non-null <see cref="Permanent.AttachedTo"/>,
/// an <see cref="ActivatedAbility"/> is registered on the bearer's
/// <see cref="Card.Abilities"/> collection. When the aura leaves the
/// battlefield (or detaches), the ability is removed from the bearer.
///
/// The activated ability:
///   - Cost: <see cref="AdditionalCost.Tap"/> on the bearer.
///   - Effect: spawn a creature token under the aura controller's
///     control. The token is a copy of the bearer (name + P/T + subtypes
///     + keyword names snapshotted at activation time, mirroring v1
///     <see cref="CopyEffect"/> semantics — printed P/T + keywords + a
///     freshly-added Haste keyword). Haste is added to the keyword list
///     even if absent on the bearer (CR 702.10 / "except it has haste").
///   - Delayed trigger (CR 603.7): exile the spawned token at the
///     beginning of the next end step. Registered on the supplied
///     <see cref="TriggerManager"/> when one is wired.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Layer 1 copy effect</b>: the token's P/T + keywords are
///   snapshotted at the moment the ability resolves; if the bearer's
///   characteristics change later (counters, +1/+1 boost, lord
///   anthems), the token does NOT track them. Aligns with the existing
///   <see cref="CopyEffect"/> v1 lossiness; a future revision can
///   register a live <see cref="CopyEffect"/> via the
///   <see cref="ContinuousEffectsService"/> overload.
/// - <b>Real "create a token that's a copy" pipeline</b>: the
///   <see cref="TokenFactory"/> token returns a fresh Creature with
///   snapshotted characteristics. The "is a copy" relationship (CR
///   706.2 copiable values incl. mana cost, colours, abilities-other-
///   than-keywords) is approximated to the engine's existing copy
///   primitive.
/// - <b>Cast-time targeting + auto-attach</b>: covered by
///   <see cref="AuraSpellDefinitionBuilder.ForAuraFromOracle"/> on the
///   <see cref="BuildSpellDefinition"/> path, identical shape to
///   Spreading Seas.
/// </summary>
[CardName("Splinter Twin")]
public static class SplinterTwinFactory
{
    public const string CardName = "Splinter Twin";
    public const string Cost = "{2}{R}{R}";

    /// <summary>Printed oracle text. <see cref="AuraEnchantClauseParser"/>
    /// derives the cast-time target predicate from the "Enchant creature"
    /// line.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature has \"{T}: Create a token that's a copy of " +
        "this creature, except it has haste. Exile the token at the " +
        "beginning of the next end step.\"";

    /// <summary>Creates a Splinter Twin with correct card identity only
    /// (no live grant lifecycle). Suitable for factory-shape / naming
    /// tests.</summary>
    public static Enchantment Create(Player owner)
        => Create(owner, eventBus: null, zoneService: null, triggers: null);

    /// <summary>
    /// Creates a fully-wired Splinter Twin. When <paramref name="eventBus"/>
    /// is supplied, an <see cref="AttachedAuraAbilityGrantStaticEffect"/>
    /// is attached so the granted activated ability registers on the
    /// bearer when the aura enters the battlefield attached to a creature,
    /// and is revoked when the aura leaves. When
    /// <paramref name="zoneService"/> is supplied, the spawned token
    /// enters the battlefield via <see cref="ZoneService"/> so
    /// <see cref="CardMovedEvent"/> publishes (ETB triggers from other
    /// permanents — Soul Warden, etc. — fire). When
    /// <paramref name="triggers"/> is supplied, the delayed end-step
    /// exile is registered as a <see cref="DelayedTriggeredAbility"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            Cost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            var lifecycle = new AttachedAuraAbilityGrantStaticEffect(
                card,
                eventBus,
                abilityFactory: bearer =>
                    BuildGrantedAbility(card, bearer, owner, zoneService, triggers, eventBus));
            lifecycle.Attach();
            // Surface the lifecycle so tests / runtime can re-Sync if they
            // call AttachTo outside the CardMovedEvent path.
            SplinterTwinLifecycleAccessor.SetLifecycle(card, lifecycle);
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Splinter Twin
    /// — "Enchant creature" → single Creature target. CR 303.4f — Auras
    /// enter the battlefield attached to their target.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAuraFromOracle(
            aura,
            OracleText,
            battlefield);
    }

    /// <summary>
    /// Build the granted activated ability: <c>{T}: Create a token copy
    /// with haste. Exile token at next end step.</c>
    /// </summary>
    private static ActivatedAbility BuildGrantedAbility(
        Enchantment aura,
        Permanent bearer,
        Player controller,
        ZoneService? zones,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        // Cost: {T} on the bearer (CR 107.3 / 602.1b).
        var costs = new ICost[]
        {
            AdditionalCost.Tap(bearer),
        };

        var effect = new Effect(
            $"{CardName}: create a token copy of {bearer.Name} with haste, exile EOT",
            () =>
            {
                // CR 706.2 — snapshot copiable values: name, P/T, subtypes,
                // keyword names. v1 lossy: doesn't track later changes to
                // the bearer's characteristics (see factory xmldoc).
                if (bearer is not Creature original) return;

                var keywords = new List<string>(
                    original.Abilities.OfType<KeywordAbility>()
                        .Select(k => k.Keyword));
                if (!keywords.Contains("Haste")) keywords.Add("Haste");

                var spec = new TokenFactory.TokenSpec(
                    Name: original.Name,
                    Power: original.BasePower,
                    Toughness: original.BaseToughness,
                    Subtypes: original.Subtypes.ToList(),
                    Keywords: keywords);

                var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

                // Haste applies — clear summoning sickness so the bot /
                // tests observe the token as attack-ready immediately
                // (CR 702.10b).
                token.HasSummoningSickness = false;

                // CR 603.7 — delayed end-step trigger to exile the token.
                // Bound at activation time so the closure captures the
                // specific token spawned by this activation.
                if (triggers != null && eventBus != null)
                {
                    var resolvedAt = DateTime.UtcNow;
                    var exileEffect = new Effect(
                        $"{CardName}: exile token at next end step",
                        () =>
                        {
                            if (token.Zone != ZoneType.Battlefield) return;
                            if (!controller.Zones.Battlefield.GetCards().Contains(token)) return;

                            if (zones != null)
                            {
                                zones.MoveCard(token, ZoneType.Battlefield, ZoneType.Exile, controller);
                            }
                            else
                            {
                                controller.Zones.Battlefield.RemoveCard(token);
                                controller.Zones.Exile.AddCard(token);
                                token.SetZone(ZoneType.Exile);
                            }
                        });

                    var delayed = new DelayedTriggeredAbility(
                        source: aura,
                        controller: controller,
                        condition: new EventTriggerCondition<StepStartedEvent>(
                            (e, _) => e.StepType == PhaseStateType.End
                                      && e.Timestamp > resolvedAt),
                        effects: new IEffect[] { exileEffect });

                    triggers.RegisterDelayed(delayed);
                }
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { effect });
    }
}

/// <summary>
/// Internal extension carrier: hangs the grant lifecycle off the aura so
/// tests / runtime can call <c>Sync()</c> after a manual
/// <see cref="Permanent.AttachTo"/> (since the bus-driven path only
/// re-syncs on the aura's own <see cref="CardMovedEvent"/>).
/// </summary>
public static class SplinterTwinLifecycleAccessor
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Enchantment, AttachedAuraAbilityGrantStaticEffect> _lifecycles = new();

    public static void SetLifecycle(Enchantment aura, AttachedAuraAbilityGrantStaticEffect lifecycle)
        => _lifecycles.AddOrUpdate(aura, lifecycle);

    public static AttachedAuraAbilityGrantStaticEffect? GetLifecycle(Enchantment aura)
        => _lifecycles.TryGetValue(aura, out var l) ? l : null;
}
