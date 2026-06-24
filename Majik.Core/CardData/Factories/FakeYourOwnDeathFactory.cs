using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fake Your Own Death (Innistrad: Midnight Hunt,
/// {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Until end of turn, target creature gets +2/+0 and gains 'When this
///    creature dies, return it to the battlefield tapped under its owner's
///    control and you create a Treasure token.' (It's an artifact with
///    '{T}, Sacrifice this token: Add one mana of any color.')"
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>fake-your-own-death.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time spell behaviour
/// (pump + grant-a-dies-trigger) is attached in code (the JSON schema models
/// neither an until-end-of-turn pump nor a granted triggered ability).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{1}{B}</c>, mono-black, owner / controller wired
///   (from JSON).
/// - One 1..1 "target creature" <see cref="TargetRequest"/>.
/// - Resolution (CR 608.2):
///     <list type="bullet">
///       <item><b>+2/+0 until end of turn</b> (CR 514.2 / CR 613.7c — Layer
///         7c) — a self-sourced <see cref="PumpUntilEndOfTurnEffect"/>
///         registered on the target creature's
///         <see cref="Creature.ActiveEffects"/>.</item>
///       <item><b>Grant the dies-trigger until end of turn</b> (CR 514.2 /
///         CR 613.1f) — a self-sourced <see cref="GrantAbilityEffect"/> that
///         adds a <see cref="TriggeredAbility"/> over
///         <see cref="Triggers.OnDies"/> (Battlefield → Graveyard self
///         condition + <c>activeZones = {Battlefield, Graveyard}</c> so the
///         trigger survives the death zone-move, the same validated shape
///         <see cref="MalakirRebirthFactory"/> / Persist use). On the
///         trigger's resolution the creature is returned from the graveyard
///         to the battlefield <b>tapped</b> under its <b>owner's</b> control
///         (CR 701.20) AND the <b>caster</b> ("you" — the controller of the
///         granted ability, CR 109.5 / CR 603.3d) creates one Treasure token
///         (CR 111.10) via <see cref="TokenFactory.CreateTreasure"/>.</item>
///     </list>
///
/// ## "you" = the caster, not the creature's owner
///
/// The granted ability's "you" refers to the player who controls that ability
/// (CR 109.5). A granted triggered ability is controlled by the player who
/// controlled the spell that granted it (CR 603.3d), i.e. the caster of Fake
/// Your Own Death — NOT the creature's owner. So the granted trigger's
/// <c>controller</c> is the caster and the Treasure is created under the
/// caster's control, even when the buffed creature belongs to an opponent.
/// (The creature itself still returns under its OWNER's control per the
/// printed "under its owner's control".)
///
/// ## Why this is NOT Undying / Persist
///
/// Undying / Persist are permanent intrinsic keywords that return with a
/// counter under the controller's control. Fake Your Own Death grants a
/// one-shot, until-end-of-turn "when this dies, return it tapped under its
/// owner's control and you create a Treasure" trigger with no counter — so the
/// grant uses <see cref="GrantAbilityEffect"/> (<c>expiresAtEndOfTurn</c>)
/// rather than a keyword factory, but reuses the validated death-trigger
/// wiring (OnDies + {Battlefield, Graveyard} active zones).
///
/// ## References
///
/// - <see cref="MalakirRebirthFactory"/> — the dies → return-tapped EOT grant
///   this factory mirrors (this card adds +2/+0 + a Treasure and removes the
///   2-life cost).
/// - <see cref="TreasureVaultFactory"/> — <see cref="TokenFactory.CreateTreasure"/>
///   minting a CR 111.10 Treasure.
/// - <see cref="ApostlesBlessingFactory"/> — EOT <see cref="GrantAbilityEffect"/>
///   registered on a target creature's <see cref="Creature.ActiveEffects"/>.
/// </summary>
[CardName("Fake Your Own Death")]
public static class FakeYourOwnDeathFactory
{
    public const string CardName = "Fake Your Own Death";
    public const string Slug = "fake-your-own-death";

    /// <summary>Power bonus granted until end of turn (+2/+0).</summary>
    public const int PowerBonus = 2;

    /// <summary>Toughness bonus granted until end of turn (none — +2/+0).</summary>
    public const int ToughnessBonus = 0;

    /// <summary>
    /// Construct Fake Your Own Death as an Instant (identity from JSON). The
    /// resolve-time <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the resolve-time "Until end of turn, target creature gets +2/+0
    /// and gains 'When this creature dies, return it to the battlefield tapped
    /// under its owner's control and you create a Treasure token.'"
    /// <see cref="SpellDefinition"/>.
    /// </summary>
    /// <param name="caster">Spell controller — the "you" who creates the
    /// Treasure and controls the granted dies-trigger (CR 603.3d).</param>
    /// <param name="resolver">Target resolver — maps the chosen target token to
    /// the live game object (expected to be a <see cref="Creature"/> on the
    /// battlefield).</param>
    /// <param name="zoneService">Optional. When supplied the granted dies
    /// trigger's graveyard → battlefield return AND the Treasure ETB route
    /// through <see cref="ZoneService.MoveCard"/> so ETB triggers fire
    /// (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: target creature gets +{PowerBonus}/+{ToughnessBonus} and gains a dies → return-tapped + Treasure trigger EOT",
                        () => Resolve(caster, raw, resolver, zoneService)),
                };
            });
    }

    /// <summary>
    /// Resolve Fake Your Own Death against <paramref name="rawTarget"/>.
    /// Exposed for direct invocation by tests / bots without driving the full
    /// cast flow.
    /// </summary>
    /// <returns>The creature that received the buff + grant, or <c>null</c>
    /// when the target was illegal at resolution (CR 608.2b/608.2c — clean
    /// no-op).</returns>
    public static Creature? Resolve(
        Player caster,
        object? rawTarget,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var live = rawTarget is null ? null : resolver(rawTarget);

        // CR 608.2b — target must still be a creature on the battlefield.
        if (live is not Creature creature || creature.Zone != ZoneType.Battlefield)
        {
            // CR 608.2c — a spell whose only target is illegal doesn't resolve;
            // none of its effects happen.
            return null;
        }
        if (creature.ActiveEffects is null)
        {
            // Shape-only no-op: without a continuous-effects service we can't
            // register the EOT pump or grant.
            return null;
        }

        // CR 514.2 / CR 613.7c — +2/+0 until end of turn (Layer 7c).
        var pump = new PumpUntilEndOfTurnEffect(creature, PowerBonus, ToughnessBonus);
        creature.ActiveEffects.Register(pump);

        // CR 514.2 / CR 613.1f — grant the dies-trigger until end of turn. The
        // granted ability's "you" (the Treasure recipient) and its controller
        // are the CASTER (CR 603.3d), not the creature's owner.
        var grant = new GrantAbilityEffect(
            source: creature,
            target: creature,
            ability: BuildReturnTappedTreasureDeathTrigger(creature, caster, zoneService),
            expiresAtEndOfTurn: true);
        creature.ActiveEffects.Register(grant);
        // Sync immediately so the trigger is on the creature's Abilities list
        // (and thus observable by the TriggerManager) on the same priority
        // window (CR 117.5).
        grant.Sync();

        return creature;
    }

    /// <summary>
    /// Build the granted "When this creature dies, return it to the battlefield
    /// tapped under its owner's control and you create a Treasure token."
    /// trigger.
    ///
    /// Mirrors <see cref="MalakirRebirthFactory"/>'s validated death-trigger
    /// shape: <see cref="Triggers.OnDies"/> (Battlefield → Graveyard self) with
    /// <c>activeZones = {Battlefield, Graveyard}</c> so the trigger survives the
    /// death zone-move (ZoneService stamps the card's Graveyard zone before
    /// publishing the event, so a Battlefield-only active-zone set would not
    /// match at evaluation time).
    /// </summary>
    /// <param name="creature">The buffed creature; returns under its OWNER's
    /// control (CR 701.20 / the printed "its owner's control").</param>
    /// <param name="caster">The "you" who creates the Treasure — the controller
    /// of the granted ability (CR 603.3d), not the creature's owner.</param>
    private static TriggeredAbility BuildReturnTappedTreasureDeathTrigger(
        Creature creature,
        Player caster,
        ZoneService? zoneService)
    {
        var effect = new Effect(
            $"{CardName} — return creature tapped under owner's control and you create a Treasure",
            () =>
            {
                // CR 608.2 — creature must still be in the graveyard at
                // resolution (a later effect could have moved it elsewhere).
                if (creature.Zone == ZoneType.Graveyard)
                {
                    var cardOwner = creature.Owner;
                    if (cardOwner != null)
                    {
                        // CR 701.20 — graveyard → battlefield under its OWNER's
                        // control. ZoneService-routed when supplied so ETB
                        // triggers fire (CR 603.6a); raw-zone fallback otherwise.
                        Fx.ReturnFromGraveyardToBattlefield(creature, cardOwner, zoneService);

                        // "tapped" (CR 701.20). Tap after the move so the
                        // permanent is on the battlefield.
                        if (creature.Zone == ZoneType.Battlefield)
                        {
                            Fx.Tap(creature);
                        }
                    }
                }

                // "and you create a Treasure token." (CR 111.10) — "you" is the
                // controller of this granted ability, i.e. the caster of Fake
                // Your Own Death (CR 603.3d), even when the creature belongs to
                // an opponent. This part is NOT contingent on the return (the
                // printed text creates the Treasure regardless of whether the
                // creature successfully returned).
                TokenFactory.CreateTreasure(caster, zoneService);
            });

        return new TriggeredAbility(
            source: creature,
            controller: caster,
            condition: Triggers.OnDies(creature),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
