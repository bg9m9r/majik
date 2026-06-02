using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Krenko, Tin Street Kingpin (War of the Spark,
/// {2}{R}).
///
/// Legendary Creature — Goblin, 1/2. Oracle text (verified against Scryfall):
///   "Whenever Krenko attacks, put a +1/+1 counter on it, then create a
///    number of 1/1 red Goblin creature tokens equal to Krenko's power."
///
/// The base shape (name, Creature, Legendary supertype, Goblin subtype,
/// {2}{R}, 1/2) is materialised from the embedded JSON definition
/// (<c>krenko-tin-street-kingpin.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single attack-triggered
/// ability is layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express a "counter-then-tokens-equal-to-power" rider, so it
/// lives in the factory (same posture as
/// <see cref="HeroOfBladeholdFactory"/>'s attack-token rider and
/// <see cref="HeartfireHeroFactory"/>'s counter trigger).
///
/// ## Implemented (v1)
/// - 1/2 red Legendary Creature — Goblin at {2}{R}; Legendary supertype +
///   Goblin subtype wired (via the JSON def) so the Legend Rule (CR 704.5j)
///   and Goblin tribal lord scopes (Goblin Chieftain / Warchief /
///   Rabblemaster) see Krenko correctly. Red is derived from the {R} pip
///   (CR 105 / 202.2).
/// - <b>Attack trigger (CR 508.1f / 603.6c)</b> — an
///   <see cref="Triggers.OnAttackSelf"/> <see cref="TriggeredAbility"/>.
///   On resolution the effect runs strictly left-to-right (CR 608.2):
///   <ol>
///     <li>Put one +1/+1 counter on Krenko (CR 122 — counters; reflected in
///         <see cref="Creature.Power"/> via the layer compute when a
///         <see cref="ContinuousEffectsService"/> is bound to
///         <see cref="Card.ActiveEffects"/>). A fresh 1/2 Krenko becomes a
///         2/3.</li>
///     <li>THEN read Krenko's CURRENT power (post-counter) and create that
///         many 1/1 red Goblin creature tokens via
///         <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 / 111.4).
///         "Krenko's power" is the live power at the instant the tokens are
///         created, so the counter added in step 1 is already counted — a
///         fresh Krenko makes two tokens, not one. This left-to-right
///         ordering is the whole point of the card (it snowballs faster than
///         Krenko, Mob Boss).</li>
///   </ol>
///   The power snapshot is taken once after the counter is added and before
///   token creation begins (CR 608.2), so token-ETB side effects don't
///   retroactively bump the token count.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. The attack
///   trigger is attached for shape observability; without a
///   <see cref="ContinuousEffectsService"/> the +1/+1 counter is added but
///   <see cref="Creature.Power"/> falls back to base P/T, and without a
///   <see cref="ZoneService"/> tokens land via raw zone moves (token-ETB
///   triggers won't auto-fire). Suitable for shape / dispatch tests.
/// - <see cref="Create(Player, TriggerManager?, ContinuousEffectsService?, ZoneService?)"/>
///   — fully-wired overload. The trigger is registered with the
///   <see cref="TriggerManager"/>; the bound <see cref="ContinuousEffectsService"/>
///   makes the +1/+1 counter raise power so the token count reads the
///   post-counter power; the <see cref="ZoneService"/> funnels each token
///   through <see cref="ZoneService.MoveCard"/> so
///   <see cref="Events.CardMovedEvent"/> fires on ETB (Impact Tremors /
///   Goblin Bushwhacker / Purphoros pick the tokens up).
///
/// ## Deferred (v1 gaps)
/// - <b>"put onto the battlefield attacking"</b>: unlike Hero of Bladehold,
///   Krenko's printed tokens are NOT created tapped and attacking — the
///   oracle just says "create" — so no combat splice is needed (the tokens
///   enter the battlefield untapped, not in combat). No gap here; noted to
///   contrast with the Hero of Bladehold analogue.
/// </summary>
[CardName("Krenko, Tin Street Kingpin")]
public static class KrenkoTinStreetKingpinFactory
{
    public const string CardName = "Krenko, Tin Street Kingpin";
    public const string Slug = "krenko-tin-street-kingpin";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Krenko, Tin Street Kingpin with no live wiring. The attack
    /// trigger is attached for shape observability; without a
    /// <see cref="ContinuousEffectsService"/> the +1/+1 counter does not
    /// raise <see cref="Creature.Power"/>, and without a
    /// <see cref="ZoneService"/> tokens land via raw zone moves. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, effects: null, zoneService: null);

    /// <summary>
    /// Construct Krenko, Tin Street Kingpin with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger is registered
    /// so a <see cref="Domain.DomainEvents.CreatureAttacksEvent"/> for Krenko
    /// lands it on the stack automatically.</param>
    /// <param name="effects">ContinuousEffectsService bound onto the card so
    /// the +1/+1 counter is reflected in <see cref="Creature.Power"/> via the
    /// layer compute (CR 122 / 613). Required for the "tokens equal to
    /// Krenko's power" count to include the just-added counter. When null the
    /// counter is still added but <see cref="Creature.Power"/> falls back to
    /// base power.</param>
    /// <param name="zoneService">Optional zone service so each spawned Goblin
    /// token publishes <see cref="Events.CardMovedEvent"/> on ETB (Impact
    /// Tremors / Goblin Bushwhacker chain correctly). When null, tokens are
    /// placed on the battlefield via raw zone moves.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Legendary supertype, Goblin subtype, {2}{R}, 1/2). The JSON carries
        // no abilities — the attack trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // CR 508.1f — "Whenever Krenko attacks, put a +1/+1 counter on it,
        // then create a number of 1/1 red Goblin creature tokens equal to
        // Krenko's power."
        var attackEffect = new Effect(
            $"{CardName}: +1/+1 counter, then create 1/1 red Goblins = Krenko's power",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 608.2 — left-to-right. Step 1: put a +1/+1 counter on
                // Krenko. With ActiveEffects bound this raises power (a 1/2
                // becomes a 2/3) BEFORE the token count is read.
                card.Counters.Add(CounterType.PlusOnePlusOne); // CR 122

                // CR 608.2 — Step 2: "a number of ... tokens equal to
                // Krenko's power". Read CURRENT power (post-counter), snapshot
                // once so token-ETB side effects don't bump the count.
                int count = card.Power;
                if (count <= 0) return;

                for (int i = 0; i < count; i++)
                {
                    CreateGoblinToken(controller, zoneService);
                }
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
    /// CR 111 / CR 111.4 — create one 1/1 red Goblin creature token under
    /// <paramref name="controller"/>'s control. Mirrors
    /// <see cref="KrenkoMobBossFactory.CreateGoblinToken"/>'s shape so "1/1
    /// red Goblin token" minting stays uniform across Goblin sources.
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
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
