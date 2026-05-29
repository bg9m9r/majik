using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Den of the Bugbear (Adventures in the Forgotten
/// Realms manland cycle, red member — sibling of
/// <see cref="HiveOfTheEyeTyrantFactory"/>). Land.
///
/// Oracle text (verified against Scryfall, AFR printing):
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {R}.
///    {3}{R}: Until end of turn, this land becomes a 3/2 red Goblin
///    creature with \"Whenever this creature attacks, create a 1/1 red
///    Goblin creature token that's tapped and attacking.\" It's still a
///    land."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls one or fewer OTHER lands (i.e. enters tapped
///   when >= 2 other lands are present). Identical "two or more other
///   lands" shape to <see cref="HiveOfTheEyeTyrantFactory"/>.
/// - <b>{T}: Add {R}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack).
/// - <b>{3}{R}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{3}{R}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/> (shared manland-cycle effects):
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> and the
///       <see cref="CardSubtype.Goblin"/> subtype. The printed Land type
///       is left intact ("It's still a land", CR 613.1c). No printed
///       keywords on the animated body.
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 3/2 (CR 613.7b).
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
/// - <b>Per-instance "Whenever this creature attacks, create a 1/1 red
///   Goblin creature token that's tapped and attacking" trigger</b> —
///   wired via <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   (CR 508.1f). The trigger structure is attached unconditionally so
///   the shape is inspectable; while not animated the land can't attack,
///   so the trigger is unreachable in practice (CR 603.6 — the body
///   inherits its ability set from the animate layer effect). On
///   resolution it creates a 1/1 red Goblin token via
///   <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Red colour identity of the animated form</b> — same gap as
///   Hive of the Eye Tyrant / Creeping Tar Pit: the engine's colour layer
///   (Layer 5) has no colour-setting effect primitive yet. The Goblin body
///   should be red while animated; v1 records the intent but doesn't apply
///   it to the animated land (the token IS correctly red — TokenFactory
///   stamps the colour identity directly).
/// - <b>"Tapped and attacking" token state</b> — the printed token enters
///   already tapped and attacking. The engine exposes
///   <see cref="Majik.Core.Combat.CombatManager.AddTappedAndAttackingToken"/>,
///   but the per-instance attack trigger resolves through the standard
///   ability pipeline without a CombatManager handle in the shape-only /
///   factory path, so v1 ships the Goblin as a normal 1/1 ETB onto the
///   battlefield (untapped, not in an attacker slot). Same deferral as
///   <see cref="GeistOfSaintTraftFactory"/> / <see cref="DalkovanEncampmentFactory"/>.
/// - <b>Combat math through Compute</b>: same gap as the rest of the
///   manland cycle — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to
///   a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 3/2 doesn't surface for combat
///   resolution.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability
///   is instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Den of the Bugbear")]
public static class DenOfTheBugbearFactory
{
    public const string CardName = "Den of the Bugbear";
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Den of the Bugbear with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The mana ability + the animate ability + the structural attack
    /// trigger are all attached so the card surface is complete; the layer
    /// effects are not registered, the conditional ETB-tapped replacement
    /// is omitted, and the attack trigger is not auto-registered (its
    /// effect still runs when driven manually). Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Den of the Bugbear with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the conditional
    /// "enters tapped unless you control &lt;= 1 other land" rider
    /// (CR 614.1c). May be null — land enters untapped unconditionally in
    /// that posture (mirrors how every other conditional-tapped factory
    /// defers this to the production binder).</param>
    /// <param name="triggers">TriggerManager — when supplied the attack
    /// trigger is registered so a CreatureAttacksEvent matching this land
    /// lands it on the stack automatically. May be null — the trigger is
    /// still attached to the card shape and resolvable when driven
    /// manually.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Conditional ETB-tapped (CR 614.1c) — "If you control two or
        // more other lands, this land enters tapped."
        // Predicate: enters untapped iff controller controls <= 1 OTHER
        // land. Same shape as HiveOfTheEyeTyrantFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= 1));
        }

        // ----------------------------------------------------------------
        // {T}: Add {R}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {3}{R}: Until end of turn, this land becomes a 3/2 red Goblin
        // creature with "Whenever this creature attacks, create a 1/1 red
        // Goblin creature token that's tapped and attacking." It's still a
        // land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {3}{R}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} red Goblin creature with attack-token trigger until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Goblin subtype. No printed
                // keywords on the animated body. Printed Land type stays
                // ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Goblin },
                    extraTypes: null));

                // Layer 7b — set base P/T 3/2.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{R}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Per-instance attack trigger (animated form): "Whenever this
        // creature attacks, create a 1/1 red Goblin creature token that's
        // tapped and attacking."
        //
        // CR 508.1f / CR 603.6. v1: structurally attached unconditionally
        // (fires on CreatureAttacksEvent with Attacker == this land; while
        // not animated the land can't attack so the trigger is unreachable
        // in practice). On resolution creates a 1/1 red Goblin token.
        // "Tapped and attacking" fidelity is deferred — see class xmldoc /
        // GeistOfSaintTraftFactory.
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 red Goblin token (attack trigger, CR 508.1f)",
            () =>
            {
                var controller = land.Controller ?? owner;
                CreateGoblinToken(controller);
            });

        var attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 red Goblin creature token under
    /// <paramref name="controller"/>'s control. The printed "tapped and
    /// attacking" shape is deferred — see factory xmldoc.
    /// </summary>
    public static Creature CreateGoblinToken(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller);
    }

    /// <summary>
    /// CR 614 helper — count lands the controller controls excluding the
    /// candidate <paramref name="self"/>. Used by the conditional ETB-
    /// tapped predicate ("two or more OTHER lands").
    /// </summary>
    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}
