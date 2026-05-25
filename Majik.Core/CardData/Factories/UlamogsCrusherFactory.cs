using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ulamog's Crusher (Rise of the Eldrazi, {8}).
///
/// Creature — Eldrazi 8/8. Oracle text (Scryfall, verified):
///   "Annihilator 2
///    Ulamog's Crusher attacks each combat if able."
///
/// ## Implemented (v1)
/// - 8/8 Creature — Eldrazi at {8}.
/// - <b>Annihilator 2 (CR 702.86)</b>: shipped via
///   <see cref="AnnihilatorFactory.Build"/> — the per-attacker trigger
///   fires on <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   and routes the two sacrifice picks through
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> when an agent
///   selector is supplied; deterministic first-two-permanents fallback
///   otherwise (legacy pre-agent posture). A discoverability
///   <see cref="KeywordAbility"/>("Annihilator", arg: 2) marker is
///   stamped alongside so keyword scans (CombatAbilities-style) see it.
///
/// ## Deferred (v1 gap)
/// - <b>"Attacks each combat if able" (CR 702.43 — attacks-each-turn
///   restriction)</b>: shipped as a <see cref="KeywordAbility"/>
///   ("AttacksEachCombat") marker only. The must-attack combat-restriction
///   primitive isn't wired yet; the marker keeps the printed text
///   discoverable on the bot rail. Same posture as Ward's discard-cost
///   gap on Reality Smasher.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Keyword markers + an
///   unbound Annihilator trigger (agent-less, first-two-permanents
///   fallback) are attached; the trigger isn't registered with any
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. The Annihilator trigger registers with
///   <paramref name="triggers"/> so attacks-fire-it goes through the
///   bus, and the supplied <paramref name="agentSelector"/> drives the
///   defender's sacrifice picks.
/// </summary>
[CardName("Ulamog's Crusher")]
public static class UlamogsCrusherFactory
{
    public const string CardName = "Ulamog's Crusher";
    public const string PrintedManaCost = "{8}";
    public const int Power = 8;
    public const int Toughness = 8;
    public const int AnnihilatorValue = 2;

    /// <summary>
    /// Construct Ulamog's Crusher with no live wiring. Keyword markers
    /// + an unbound Annihilator trigger (agent-less, first-two
    /// permanents fallback) are attached; the trigger isn't
    /// registered with any <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agentSelector: null);

    /// <summary>
    /// Construct Ulamog's Crusher with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the Annihilator trigger
    /// registers with the bus so attacks fire it automatically
    /// (CR 603.2).</param>
    /// <param name="agentSelector">When supplied, the defender's
    /// sacrifice picks consult
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>; null
    /// falls back to deterministic first-two-permanents.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.86 — Annihilator 2 marker for discoverability. The
        // wiring trigger below consumes the arg via the factory call,
        // not by re-reading the marker; the marker keeps the keyword
        // scan surface uniform (Trample / Haste / Indestructible
        // shape).
        card.AddAbility(new KeywordAbility(
            "Annihilator", card, owner, arg: AnnihilatorValue));

        // CR 702.43 — "attacks each combat if able" combat
        // restriction. Shipped as a marker only — the must-attack
        // primitive isn't wired yet (same posture as Reality
        // Smasher's non-mana Ward discard rider).
        card.AddAbility(new KeywordAbility(
            "AttacksEachCombat", card, owner));

        // CR 702.86a — the Annihilator triggered ability itself.
        var annihilator = AnnihilatorFactory.Build(
            source: card,
            n: AnnihilatorValue,
            agentSelector: agentSelector);
        card.AddAbility(annihilator);
        triggers?.RegisterTriggeredAbility(annihilator);

        return card;
    }
}
