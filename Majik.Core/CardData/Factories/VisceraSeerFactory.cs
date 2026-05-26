using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Viscera Seer (Scars of Mirrodin, {B}).
///
/// Creature — Vampire Wizard 1/1. Oracle text:
///   "Sacrifice a creature: Scry 1."
///
/// ## Implemented (v1)
/// - 1/1 Vampire Wizard at {B}, owner/controller assigned.
/// - <b>Activated ability</b>: cost =
///   <see cref="SacrificeAnotherCreatureCost"/> (see deferred), effect =
///   peek the top card of library and let the controller's
///   <see cref="IPlayerAgent"/> decide top/bottom via
///   <see cref="ScryAction"/>. When no agent is registered the decision
///   falls back to "all to bottom" (same posture as the
///   <c>scry N</c> library-spell template).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrificing the Seer itself</b>: oracle text is "Sacrifice a
///   creature" (any creature including Viscera Seer), but the
///   <see cref="SacrificeAnotherCreatureCost"/> primitive excludes the
///   source. V1 uses "another"; self-sac (a real-game line — last creature
///   before death to scry) is deferred until a "sacrifice this or any
///   creature" cost primitive exists.
/// - <b>Sacrifice target prompt</b>: pre-set
///   <see cref="SacrificeAnotherCreatureCost.Target"/>; otherwise the cost
///   picks the first eligible creature deterministically (same gap as
///   Carrion Feeder / Goblin Bombardment / Yawgmoth).
/// - Scry 1 uses the global <see cref="AgentRegistry"/> (sync-over-async
///   <c>GetAwaiter().GetResult()</c>) — same TODO as the
///   <c>ScryNSpell</c> library template.
/// </summary>
[CardName("Viscera Seer")]
public static class VisceraSeerFactory
{
    public const string CardName = "Viscera Seer";

    /// <summary>
    /// Construct Viscera Seer owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{B}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice a creature: Scry 1.
        // CR 602 (activated abilities) + CR 701.20 (scry).
        // ----------------------------------------------------------------
        var sacrificeCost = new SacrificeAnotherCreatureCost(card);

        var ability = new VisceraSeerAbility(
            source: card,
            controller: owner,
            sacrificeCost: sacrificeCost,
            scryEffect: new Effect(
                "Viscera Seer: scry 1",
                () =>
                {
                    var peeked = ScryAction.Peek(owner, 1);
                    if (peeked.Count == 0) return;

                    // Consult the registered agent when available; default
                    // to all-to-bottom otherwise (same posture as
                    // LibrarySpellFactory.ScryNSpell).
                    // TODO: remove sync-over-async once IEffect.Execute
                    // becomes async.
                    var agent = AgentRegistry.Get(owner);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        decision = agent.ChooseScryDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                    }
                    else
                    {
                        decision = new ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    ScryAction.Apply(owner, 1, decision);
                }));

        card.AddAbility(ability);
        return card;
    }
}

/// <summary>
/// Viscera Seer's sole activated ability — sacrifice another creature to
/// scry 1. Subclasses <see cref="ActivatedAbility"/> so the sacrifice cost
/// is reachable from tests / bots that want to pre-set
/// <see cref="SacrificeAnotherCreatureCost.Target"/>.
/// </summary>
public sealed class VisceraSeerAbility : ActivatedAbility
{
    /// <summary>
    /// The sacrifice cost on the ability — exposed so callers can pre-set
    /// <see cref="SacrificeAnotherCreatureCost.Target"/> before activation.
    /// </summary>
    public SacrificeAnotherCreatureCost SacrificeChoice { get; }

    internal VisceraSeerAbility(
        Creature source,
        Player controller,
        SacrificeAnotherCreatureCost sacrificeCost,
        IEffect scryEffect)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[] { sacrificeCost },
            effects: new[] { scryEffect })
    {
        SacrificeChoice = sacrificeCost;
    }
}
