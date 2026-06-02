using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overlord of the Boilerbilges (Duskmourn: House of
/// Horror, {4}{R}{R}). Enchantment Creature — Avatar Horror 5/5. Oracle text
/// (verified against Scryfall):
///   "Impending 4—{2}{R}{R} (If you cast this spell for its impending cost, it
///    enters with four time counters and isn't a creature until the last is
///    removed. At the beginning of your end step, remove a time counter from
///    it.)
///    Whenever this permanent enters or attacks, it deals 4 damage to any
///    target."
///
/// The red member of the Duskmourn "Overlord" cycle — same Impending +
/// enters-or-attacks scaffold as <see cref="OverlordOfTheBalemurkFactory"/>,
/// but the trigger body is straight burn: 4 damage to any target (CR 115.3).
///
/// The card's base shape (name, Enchantment + Creature types, Avatar + Horror
/// subtypes, {4}{R}{R}, 5/5) is materialised from the embedded JSON definition
/// (<c>overlord-of-the-boilerbilges.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours (the
/// Impending marker keyword + the enters-or-attacks trigger) are layered on
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express keyword
/// markers or any-target damage triggers, so they live in the factory (same
/// posture as <see cref="OverlordOfTheBalemurkFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enters-or-attacks trigger (CR 603.1 ETB + CR 508.1f attack)</b>:
///   two <see cref="TriggeredAbility"/> instances, each carrying a single
///   1..1 "any target" <see cref="TargetRequest"/> — one gated on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> (same dual-trigger shape as
///   <see cref="OverlordOfTheBalemurkFactory"/>). On resolution each reads its
///   chosen target and routes through <see cref="Fx.DealDamageAny"/> so all
///   three legal target classes resolve correctly: Player → life loss
///   (CR 119.3), Creature → marked damage (CR 120.3), Planeswalker → loyalty
///   removal (CR 306.7). Illegal-on-resolution targets fail silently
///   (CR 608.2b). Same any-target damage primitive as
///   <see cref="PiaAndKiranNalaarFactory"/> / Shock.
///
/// ## Impending — modelled as a marker keyword (deferred mechanic)
/// "Impending 4—{2}{R}{R}" is an alternative-cost keyword (Duskmourn). The
/// engine does not yet have a first-class Impending alt-cost / "isn't a
/// creature until the last time counter is removed" path. Following the
/// established marker-keyword precedent (Delve, Suspend, and
/// <see cref="OverlordOfTheBalemurkFactory"/>'s Impending), Impending is wired
/// as a <see cref="KeywordAbility"/> marker with <c>Arg = 4</c> so
/// introspection (UI, bots, the alt-cost probe stream) can see the keyword +
/// counter count on the card. The full Impending mechanic — casting for
/// {2}{R}{R} with four Time counters (CR 122.1), the Layer-4 "isn't a creature"
/// type-strip while counters remain (CR 613), and the end-step "remove a time
/// counter" delayed trigger — is deferred. The card's printed gameplay payload
/// (the enters-or-attacks burn trigger) is fully implemented; only the
/// alternate way to pay for it is the deferred part. When cast for its normal
/// {4}{R}{R} cost the card behaves completely.
/// </summary>
[CardName("Overlord of the Boilerbilges")]
public static class OverlordOfTheBoilerbilgesFactory
{
    public const string CardName = "Overlord of the Boilerbilges";
    public const string Slug = "overlord-of-the-boilerbilges";

    /// <summary>Impending counter count — "Impending 4".</summary>
    public const int ImpendingCount = 4;

    /// <summary>Damage dealt by the enters-or-attacks trigger.</summary>
    public const int DamageAmount = 4;

    /// <summary>
    /// Construct Overlord of the Boilerbilges owned and controlled by
    /// <paramref name="owner"/>. The 5/5 Enchantment Creature — Avatar Horror
    /// identity comes from the JSON definition; the Impending marker + the two
    /// enters-or-attacks 4-damage triggers are layered on here. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Overlord of the Boilerbilges with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers are
    /// registered so the matching events land their abilities on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Avatar + Horror subtypes, {4}{R}{R}, 5/5). The
        // JSON carries no abilities — the Impending marker + the
        // enters-or-attacks trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Impending 4 — marker keyword (mechanic deferred; see class remarks).
        // Arg carries the printed counter count.
        card.AddAbility(new KeywordAbility("Impending", card, owner, arg: ImpendingCount));

        // ETB trigger — CR 603.1.
        var etbTrigger = BuildDamageTrigger(
            card, owner,
            Triggers.OnEnterBattlefieldSelf(card),
            $"{CardName}: enters — deal {DamageAmount} damage to any target");
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f.
        var attackTrigger = BuildDamageTrigger(
            card, owner,
            Triggers.OnAttackSelf(card),
            $"{CardName}: attacks — deal {DamageAmount} damage to any target");
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Build one enters-or-attacks triggered ability: a single 1..1 "any
    /// target" <see cref="TargetRequest"/> whose effect deals
    /// <see cref="DamageAmount"/> damage to the chosen target via
    /// <see cref="Fx.DealDamageAny"/> (CR 115.3 / CR 608.2b — gated per target
    /// shape). The closure reads the ability's own
    /// <see cref="TriggeredAbility.ChosenTargets"/>, populated by the trigger
    /// pipeline before resolution.
    /// </summary>
    private static TriggeredAbility BuildDamageTrigger(
        Creature card, Player owner, ITriggerCondition condition, string label)
    {
        TriggeredAbility? ability = null;

        var effect = new Effect(label, () =>
        {
            if (ability == null) return;
            if (ability.ChosenTargets.Count == 0) return;
            if (ability.ChosenTargets[0].Count == 0) return;

            var target = ability.ChosenTargets[0][0];
            Fx.DealDamageAny(target, DamageAmount); // CR 608.2b — gated per shape
        });

        ability = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        return ability;
    }
}
