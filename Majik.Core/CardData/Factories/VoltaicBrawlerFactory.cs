using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voltaic Brawler (Kaladesh, {R}{G}).
///
/// Creature — Human Warrior 3/2. Oracle text (verified against Scryfall):
///   "When this creature enters, you get {E}{E} (two energy counters).
///    Whenever this creature attacks, you may pay {E}. If you do, it gets
///    +1/+1 and gains trample until end of turn."
///
/// A Gruul Energy two-drop: banks two energy on ETB, then can spend a
/// single energy on each attack to swing as a 4/3 trampler. Base shape
/// (name, Creature, Human Warrior subtypes, {R}{G}, 3/2) is materialised
/// from the embedded JSON definition (<c>voltaic-brawler.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the energy ETB trigger and
/// the pay-{E} attack trigger are layered on top in C# (the JSON
/// <c>AbilityDefinition</c> schema does not express energy gain, attack
/// triggers, optional "may pay" riders, or pump/keyword-until-EOT effects
/// — same posture as <see cref="GuideOfSoulsFactory"/> /
/// <see cref="PlatedGeopedeFactory"/> whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 3/2 Creature — Human Warrior, mana cost {R}{G}, owner / controller wired.
/// - <b>Energy ETB trigger</b> (CR 603.6a + CR 106.13b) — a
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnEnterBattlefieldSelf"/>
///   (this creature entering the battlefield). On resolution
///   <see cref="Player.GainEnergy"/>(2) increments the controller's
///   player-scoped energy ledger by two ({E}{E}). Same shape as Guide of
///   Souls's ETB energy gain (collapsed to a single ledger call).
/// - <b>Pay-{E} attack trigger</b> (CR 508.1f attack trigger / CR 117.5
///   optional "may pay" / CR 613 layers / CR 514.2 EOT expiry) — a
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnAttackSelf"/>
///   (this creature attacking; no <see cref="TargetRequest"/> — the pump
///   names "it", i.e. this creature). On resolution the controller's
///   registered <see cref="IPlayerAgent"/> is consulted for the optional
///   {E} payment via <see cref="IPlayerAgent.ChooseYesNoAsync(string, BotIntent, System.Threading.CancellationToken)"/>:
///     - <b>Agent present</b>: ask "pay {E}?" with <see cref="BotIntent.Buff"/>
///       intent (the bot's buff scorer reads "+1/+1 + trample" as upside).
///       When the agent says yes AND the controller can afford it, spend one
///       energy via <see cref="Player.PayEnergy"/> and register the grants.
///     - <b>No agent</b> (shape-only / pre-agent path): default to paying
///       when affordable — the rider is strictly upside, mirroring Harnessed
///       Lightning's "spend energy when it advances the board" fallback.
///   When the payment is made the creature gets:
///     - +1/+1 EOT via <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c
///       CR 613.1g).
///     - Trample EOT via <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///       (Layer 6 CR 613.1c).
///   registered against this creature's own <see cref="Creature.ActiveEffects"/>.
///   When <see cref="Creature.ActiveEffects"/> is null (shape-only tests with
///   no live <see cref="Majik.Core.Services.ContinuousEffectsService"/>) the
///   registration is a no-op — mirrors <see cref="PlatedGeopedeFactory"/>.
///   When the controller cannot afford {E} (or the agent declines) no energy
///   is spent and no grants are made (CR 117.5 — "you may pay" is optional and
///   bounded by affordability).
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches both triggers for inspection but does not register them
///   with a bus. Use the <see cref="Create(Player, TriggerManager)"/> overload
///   for live firing.
/// - <b>Resolution-time "may pay" prompt vs cast-time</b>: same posture as
///   Harnessed Lightning — <see cref="IPlayerAgent.ChooseYesNoAsync"/> is the
///   pragmatic v1 surface for the resolve-time optional cost (the printed
///   "you may pay {E}" is a resolution decision, CR 603.4).
/// </summary>
[CardName("Voltaic Brawler")]
public static class VoltaicBrawlerFactory
{
    public const string CardName = "Voltaic Brawler";
    public const string Slug = "voltaic-brawler";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>Energy banked on ETB — {E}{E} (CR 106.13b).</summary>
    public const int EtbEnergyGain = 2;

    /// <summary>Energy spent per attack to fuel the pump rider.</summary>
    public const int AttackEnergyCost = 1;

    /// <summary>Layer 7c +P/+T magnitude granted when the {E} is paid.</summary>
    public const int PumpAmount = 1;

    /// <summary>
    /// Construct Voltaic Brawler with no live <see cref="TriggerManager"/>
    /// wiring. The ETB energy trigger + the pay-{E} attack trigger are
    /// attached for shape inspection; neither is registered with a bus.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Voltaic Brawler. When <paramref name="triggers"/> is supplied
    /// both triggered abilities are registered so a self-ETB
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> and a self
    /// <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
    /// automatically queue their abilities.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human Warrior subtypes, {R}{G}, 3/2). The JSON carries no
        // abilities — the energy ETB + pay-{E} attack triggers are layered
        // on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB — CR 603.6a + CR 106.13b.
        //   "When this creature enters, you get {E}{E}."
        // No target; on resolution the controller banks two energy. The
        // three-pip-style oracle wording collapses to a single ledger call
        // (energy is a single integer resource on Player).
        // ----------------------------------------------------------------
        var etbEnergyEffect = new Effect(
            $"{CardName}: you get {{E}}{{E}} (two energy counters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainEnergy(EtbEnergyGain);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEnergyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 117.5 / CR 613 / CR 514.2.
        //   "Whenever this creature attacks, you may pay {E}. If you do, it
        //    gets +1/+1 and gains trample until end of turn."
        //
        // No target — the rider names "it" (this creature). On resolution,
        // consult the controller's agent for the optional {E} payment; when
        // paid (and affordable), spend one energy and register +1/+1 (Layer
        // 7c) + Trample (Layer 6) EOT grants on this creature's own
        // ActiveEffects.
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: may pay {{E}} — if you do, +{PumpAmount}/+{PumpAmount} and gains trample until end of turn",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 117.5 — "you may pay" is optional and bounded by
                // affordability. Decide first whether to pay.
                if (controller.EnergyCounters < AttackEnergyCost) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool pay;
                if (agent != null)
                {
                    // Resolution-time optional-cost prompt (CR 603.4). The
                    // rider is strict upside, so it is classified Buff.
                    pay = (await agent.ChooseYesNoAsync(
                            $"{CardName}: pay {{E}} for +{PumpAmount}/+{PumpAmount} and trample until end of turn?",
                            BotIntent.Buff).ConfigureAwait(false));
                }
                else
                {
                    // No-agent fallback: pay when affordable — the rider is
                    // strictly upside (mirrors Harnessed Lightning's
                    // "spend energy when it advances the board" posture).
                    pay = true;
                }

                if (!pay) return;
                if (!controller.PayEnergy(AttackEnergyCost)) return;

                // ActiveEffects is null in shape-only tests (no live
                // ContinuousEffectsService) — grants no-op, mirroring
                // Plated Geopede.
                if (card.ActiveEffects == null) return;

                // CR 613.1g Layer 7c — +1/+1 until end of turn.
                card.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpAmount, PumpAmount));

                // CR 613.1c Layer 6 — gains Trample until end of turn.
                card.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, "Trample"));
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
}
