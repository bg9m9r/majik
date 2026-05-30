using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Siege-Gang Commander (Onslaught, {3}{R}{R}).
///
/// Creature — Goblin 2/2. Oracle text (Scryfall, verified):
///   "When this creature enters, create three 1/1 red Goblin creature tokens.
///    {1}{R}, Sacrifice a Goblin: This creature deals 2 damage to any target."
///
/// ## Shape source
/// Card identity (name, {3}{R}{R}, 2/2, Creature — Goblin) is loaded from
/// <c>Majik.Core/CardData/Cards/siege-gang-commander.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Both abilities are attached in code
/// below — the JSON ability schema does not yet express "create token" or
/// "activated ability" effects, so they are hand-rolled here, same posture as
/// <see cref="GoblinInstigatorFactory"/> (identity from JSON, behaviour in C#).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin at {3}{R}{R}; owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature enters,
///   create three 1/1 red Goblin creature tokens." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> active in
///   <see cref="ZoneType.Battlefield"/>. On resolution it mints three 1/1 red
///   Goblin creature tokens (CR 111 / CR 111.4) under the entering card's
///   controller via <see cref="GoblinRabblemasterFactory.CreateGoblinToken"/>
///   — same <see cref="Majik.Core.Tokens.TokenFactory.TokenSpec"/> used by
///   Krenko / Goblin Rabblemaster / Goblin Instigator (1/1, red, Goblin
///   subtype, no keywords). Token creation routes through
///   <see cref="ZoneService"/> when supplied so each token's ETB
///   CardMovedEvent publishes for downstream ETB triggers (Impact Tremors /
///   Soul Warden / Purphoros).
/// - <b>Activated ability (CR 602)</b>: "{1}{R}, Sacrifice a Goblin: This
///   creature deals 2 damage to any target." Cost = a <see cref="ManaCostCost"/>
///   for the {1}{R} mana plus a <see cref="SacrificeAGoblinCost"/> (Siege-Gang
///   Commander itself is eligible — the printed cost is "a Goblin," no
///   "another" qualifier). A single any-target <see cref="TargetRequest"/> is
///   declared (CR 602.2b); resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert to loyalty
///   removal (CR 306.7) — same any-target ping shape as Mogg Fanatic /
///   Pashalik Mons.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability but not registered with any
///   <see cref="TriggerManager"/>; token creation uses raw zone manipulation.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. The ETB trigger registers with <paramref name="triggers"/>;
///   token creation routes through <paramref name="zoneService"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Token ETB triggers fire only when zoneService supplied</b>: same
///   posture as Krenko / Rabblemaster / Goblin Instigator — the raw fallback
///   bypasses <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>.
/// - <b>Target / sacrifice prompting</b>: the activated-ability flow doesn't
///   prompt for the damage target or the sacrificed Goblin in the v1
///   dispatcher — callers set <see cref="ActivatedAbility.ChosenTargets"/>
///   before resolution and the <see cref="SacrificeAGoblinCost"/> auto-picks a
///   Goblin (preferring another over self). Same posture as Pashalik Mons /
///   Mogg Fanatic.
/// </summary>
[CardName("Siege-Gang Commander")]
public static class SiegeGangCommanderFactory
{
    public const string CardName = "Siege-Gang Commander";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>The mana portion of the activated ability's cost.</summary>
    public const string ActivatedManaCost = "{1}{R}";
    public const int PingDamage = 2;

    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int TokenCount = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("siege-gang-commander");

    /// <summary>
    /// Construct Siege-Gang Commander with its abilities attached to the card
    /// shape but the ETB trigger NOT registered with a
    /// <see cref="TriggerManager"/>; token creation uses raw zone
    /// manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Siege-Gang Commander with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional trigger manager so the ETB trigger is
    /// bus-driven automatically (CR 603.3 — queued on the stack).</param>
    /// <param name="zoneService">Optional zone service so each spawned Goblin
    /// token publishes <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// on ETB.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this creature enters, create three 1/1 red Goblin
        //    creature tokens."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create three 1/1 red Goblin tokens",
            () =>
            {
                // CR 111 / CR 111.4 — three 1/1 red Goblin tokens under the
                // entering card's controller (defaults to owner for v1).
                var controller = card.Controller ?? owner;
                for (int i = 0; i < TokenCount; i++)
                {
                    GoblinRabblemasterFactory.CreateGoblinToken(controller, zoneService);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {1}{R}, Sacrifice a Goblin: This creature deals 2 damage to any
        // target (CR 602 — activated ability; CR 701.16 — sacrifice cost).
        // Mana portion ({1}{R}) is a ManaCostCost; the sacrifice is a
        // SacrificeAGoblinCost (Siege-Gang Commander itself is eligible —
        // no "another" qualifier). Resolution reads ChosenTargets and routes
        // through Fx.DealDamageAny so Planeswalker loyalty removal (CR 306.7)
        // is handled — same ping shape as Mogg Fanatic / Pashalik Mons.
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;
        var pingEffect = new Effect(
            $"{CardName}: deal {PingDamage} damage to any target",
            () =>
            {
                if (pingAbility == null) return;
                if (pingAbility.ChosenTargets.Count == 0) return;
                if (pingAbility.ChosenTargets[0].Count == 0) return;
                var target = pingAbility.ChosenTargets[0][0];
                Fx.DealDamageAny(target, PingDamage);
            });

        pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                new SacrificeAGoblinCost(card, owner),
            },
            effects: new IEffect[] { pingEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(pingAbility);

        return card;
    }
}
