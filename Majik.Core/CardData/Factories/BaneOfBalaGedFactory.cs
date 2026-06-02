using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bane of Bala Ged (Battle for Zendikar, {7}).
/// Creature — Eldrazi 7/5. Oracle text (verified against Scryfall):
///   "Whenever this creature attacks, defending player exiles two
///    permanents they control."
///
/// The base shape (name, Eldrazi subtype, {7}, 7/5) is materialised from
/// the embedded JSON definition (<c>bane-of-bala-ged.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed behaviour
/// (the attack trigger) is layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express attack triggers (same
/// posture as <see cref="KozilekButcherOfTruthFactory"/>).
///
/// ## Implemented (v1)
/// - <b>7/5 Creature — Eldrazi at {7}</b> (mana value 7, colourless —
///   CR 105.2c, no coloured symbols).
/// - <b>Attack trigger — "defending player exiles two permanents they
///   control" (CR 508.1f / CR 603.2)</b>: a <see cref="TriggeredAbility"/>
///   over <see cref="CreatureAttacksEvent"/> filtered to self
///   (<c>e.Attacker == card</c>). On resolution the <em>defending</em>
///   player (CR 506.2) chooses two of their own permanents to exile.
///
///   This is the same per-attacker "defending player removes N permanents
///   of their choice" shape as Annihilator (CR 702.86,
///   <see cref="Keywords.AnnihilatorFactory"/>), but with two printed
///   differences from that keyword: (1) the removal is <b>exile</b>
///   (<see cref="Fx.MoveToExile"/>), not sacrifice — so it does NOT bypass
///   anything via the sacrifice path, it simply removes from the
///   battlefield to exile; and (2) <b>the defending player makes the
///   choice</b> (CR 800-series — a player who's instructed to exile
///   permanents they control chooses which), routed through
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> when an agent
///   selector is supplied, with a deterministic first-two-permanents
///   fallback otherwise (legacy pre-agent posture, mirroring
///   <see cref="Keywords.AnnihilatorFactory"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The attack trigger is
///   attached but registers with no <see cref="TriggerManager"/>; the
///   agent-less first-two-permanents fallback applies. Suitable for
///   dispatcher / structural tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. The attack trigger registers with the bus so attacks
///   fire it automatically (CR 603.2); the supplied
///   <paramref name="agentSelector"/> drives the defender's exile picks.
/// </summary>
[CardName("Bane of Bala Ged")]
public static class BaneOfBalaGedFactory
{
    public const string CardName = "Bane of Bala Ged";
    public const string Slug = "bane-of-bala-ged";
    public const int Power = 7;
    public const int Toughness = 5;

    /// <summary>Number of permanents the defending player exiles on attack.</summary>
    public const int ExileCount = 2;

    /// <summary>
    /// Construct Bane of Bala Ged with no live wiring. The attack trigger is
    /// attached for shape; nothing registers with any
    /// <see cref="TriggerManager"/> and the agent-less first-two-permanents
    /// fallback applies. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, agentSelector: null);

    /// <summary>
    /// Construct Bane of Bala Ged with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger registers
    /// with the bus so attacks automatically place the ability on the stack
    /// (CR 603.2).</param>
    /// <param name="agentSelector">When supplied, the defending player's
    /// exile picks consult
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>; null falls back
    /// to deterministic first-two-permanents.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi subtype, {7}, 7/5). The JSON carries no abilities — the
        // attack trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 603.2.
        //   "Whenever this creature attacks, defending player exiles two
        //    permanents they control."
        // Capture the defending player across condition → effect (mirrors
        // AnnihilatorFactory's capturedDefender closure — the event is the
        // single source of truth for "defending player at attack time",
        // CR 506.2).
        // ----------------------------------------------------------------
        Player? capturedDefender = null;

        var condition = new EventTriggerCondition<CreatureAttacksEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Attacker, card)) return false;
                // CR 506.2 — defender at attack time. A planeswalker defender
                // has no "they control" permanents of its own; the defending
                // player is the planeswalker's controller (CR 506.4c).
                capturedDefender = e.DefendingPlayerOrPlaneswalker switch
                {
                    Player p => p,
                    Planeswalker pw => pw.Controller,
                    _ => null,
                };
                return capturedDefender != null;
            });

        var effect = new Effect(
            $"{CardName}: defending player exiles {ExileCount} permanents they control",
            async ctx =>
            {
                var victim = capturedDefender;
                if (victim == null) return;

                var exiled = 0;
                while (exiled < ExileCount)
                {
                    // Re-read the battlefield each iteration — the previous
                    // exile may have removed other permanents (LTB triggers,
                    // etc.) so a one-shot snapshot would race (same guard as
                    // AnnihilatorFactory).
                    var candidates = victim.Zones.Battlefield
                        .GetCards()
                        .ToList();
                    if (candidates.Count == 0) break;

                    ICard? pick;
                    var agent = agentSelector?.Invoke(victim);
                    if (agent != null)
                    {
                        // CR 800-series — the defending player chooses which of
                        // their own permanents to exile (unlike a targeted
                        // removal spell the attacker would aim).
                        pick = await agent.ChooseFromBattlefieldAsync(
                                victim,
                                candidates,
                                Cards.BotIntent.Removal).ConfigureAwait(false);
                        // CR 608.2b — illegal-on-resolution guard. If the agent
                        // returns something no longer on the defender's
                        // battlefield (or null), fall back to the first
                        // candidate.
                        if (pick == null
                            || pick.Zone != ZoneType.Battlefield
                            || !ReferenceEquals(pick.Controller, victim))
                        {
                            pick = candidates[0];
                        }
                    }
                    else
                    {
                        // Deterministic v1 fallback — first permanent.
                        pick = candidates[0];
                    }

                    // CR 603.2 — exile (not sacrifice). Fx.MoveToExile routes
                    // the permanent from the battlefield to exile.
                    Fx.MoveToExile(pick);
                    exiled++;
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
