using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Haywire Mite (The Brothers' War, {1}).
///
/// Artifact Creature — Insect 1/1. Oracle text (verified against Scryfall):
///   "When this creature dies, you gain 2 life.
///    {G}, Sacrifice this creature: Exile target noncreature artifact or
///    noncreature enchantment."
///
/// The card's base shape (name, Artifact + Creature, Insect subtype, {1},
/// 1/1) is materialised from the embedded JSON definition
/// (<c>haywire-mite.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="GlaringFleshrakerFactory"/> / <see cref="AdaptiveAutomatonFactory"/>).
/// The dies trigger is now declarative (carried by the JSON definition via the
/// <c>dies_self</c> trigger variant + <c>gain_life_self</c> effect); the
/// sacrifice-exile activated ability is still layered on here — the JSON
/// schema does not yet express sacrifice-self + targeted-exile activations.
///
/// ## Implemented (v1)
///
/// - <b>Dies trigger (CR 603.6c / 700.4)</b> — "When this creature dies, you
///   gain 2 life." Now declarative: the JSON <c>dies_self</c> trigger variant
///   (<see cref="DiesSelfTriggerDef"/>) builds a <see cref="TriggeredAbility"/>
///   over the Battlefield → Graveyard move (<see cref="Triggers.OnDies"/>),
///   and the <c>gain_life_self</c> effect gains the controller 2 life on
///   resolution (CR 119.3). The trigger variant declares active zones
///   {Battlefield, Graveyard} because <see cref="ZoneService"/> stamps
///   <c>card.Zone = Graveyard</c> before publishing the move event — the
///   trigger must still be observable then (same posture as Doomed Traveler
///   / Aven Fisher / Wurmcoil Engine).
/// - <b>{G}, Sacrifice this creature: Exile target noncreature artifact or
///   noncreature enchantment (CR 602)</b> — an <see cref="ActivatedAbility"/>
///   with a <see cref="ManaCostCost"/>("{G}") plus
///   <see cref="AdditionalCost.Sacrifice"/> on the mite itself, and a single
///   1..1 <see cref="TargetRequest"/> so the activating player's agent picks
///   the target at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and gates the chosen
///   permanent on (Artifact OR Enchantment) AND NOT Creature AND on the
///   battlefield with a live owner (CR 608.2b — an illegal target makes the
///   exile part of the effect do nothing). On a legal target the permanent
///   is moved to its owner's exile zone via <see cref="Fx.MoveToExile"/>
///   (CR 701.20). The self-sacrifice is carried out by the effect closure
///   because the shared <see cref="AdditionalCost.Sacrifice"/> Pay() is a
///   no-op stub (same trick as <see cref="AetherSpellbombFactory"/> /
///   <see cref="FulminatorMageFactory"/>); the mite still sacrifices even
///   when the target half fizzles, because the cost was paid on activation.
/// - <b>Instant speed</b> — the activated ability has no sorcery-speed
///   restriction (CR 602.5b).
///
/// ## Deferred (v1 gaps)
///
/// - <b>AdditionalCost.Sacrifice zone-move TODO</b> — the shared sacrifice
///   cost is still a no-op stub, so the self-sac is routed through the effect
///   closure (shared with Aether Spellbomb / Fulminator Mage / Mishra's
///   Bauble).
/// - <b>Agent target legality filtering</b> — <c>ActionValidator</c> does not
///   yet restrict the agent's target list to noncreature artifacts /
///   enchantments. The resolution-time guard catches illegal picks
///   (CR 608.2b); the tests exercise the legal artifact / enchantment paths
///   and the artifact-creature fizzle path.
/// - <b>ZoneService routing for the exile</b> — raw zone manipulation via
///   <see cref="Fx.MoveToExile"/> (mirrors the other exile factories), so
///   leave-the-battlefield triggers via
///   <see cref="Majik.Core.Events.CardMovedEvent"/> are not emitted by this
///   path. The single-arg dispatcher path likewise does not register the
///   declarative dies trigger with a <see cref="TriggerManager"/> (correct
///   shape for factory-shape / dispatch tests; production callers use the full
///   overload).
/// </summary>
[CardName("Haywire Mite")]
public static class HaywireMiteFactory
{
    public const string CardName = "Haywire Mite";
    public const string Slug = "haywire-mite";
    public const int LifeGain = 2;

    /// <summary>
    /// Construct Haywire Mite with the dies trigger + exile activated
    /// ability attached to the card shape, but the dies trigger NOT
    /// registered with a <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Haywire Mite with an optional <see cref="TriggerManager"/>.
    /// When supplied, the dies trigger is registered so a Battlefield →
    /// Graveyard move places it on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape + declarative dies trigger from the embedded JSON
        // definition (name, Artifact + Creature, Insect subtype, {1}, 1/1 +
        // the dies_self → gain_life_self ability). The sacrifice-exile
        // activated ability is layered on below — its self-sacrifice + targeted
        // exile is not yet expressible declaratively.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Dies trigger (CR 603.6c / 700.4): "When this creature dies, you
        // gain 2 life." Now declarative — the JSON dies_self trigger variant
        // builds a TriggeredAbility over the Battlefield → Graveyard move with
        // active zones {Battlefield, Graveyard} (so it stays observable after
        // ZoneService stamps card.Zone = Graveyard). Register it with the
        // supplied TriggerManager so the move auto-queues it (CR 603.3).
        // ----------------------------------------------------------------
        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        // ----------------------------------------------------------------
        // {G}, Sacrifice this creature: Exile target noncreature artifact or
        // noncreature enchantment. CR 602 — activated ability with a {G}
        // mana cost + self-sacrifice + a single 1..1 target request. The
        // resolution effect gates the chosen permanent on
        // (Artifact OR Enchantment) AND NOT Creature AND on-battlefield with
        // a live owner (CR 608.2b — illegal target → exile does nothing),
        // then exiles it (CR 701.20). The self-sacrifice runs inline because
        // AdditionalCost.Sacrifice's Pay() is a no-op stub.
        // ----------------------------------------------------------------
        ActivatedAbility? exileAbility = null;
        var exileEffect = new Effect(
            $"{CardName}: exile target noncreature artifact or enchantment + sac self",
            () =>
            {
                if (exileAbility != null
                    && exileAbility.ChosenTargets.Count > 0
                    && exileAbility.ChosenTargets[0].Count > 0
                    && exileAbility.ChosenTargets[0][0] is ICard target
                    && IsLegalTarget(target))
                {
                    Fx.MoveToExile(target);
                }

                SacrificeSelf(card, owner);
            });

        exileAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{G}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target noncreature artifact or noncreature enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(exileAbility);

        return card;
    }

    /// <summary>
    /// CR 608.2b — a legal target is a noncreature artifact OR a noncreature
    /// enchantment on the battlefield with a live owner. An artifact creature
    /// (or enchantment creature) is excluded by the !Creature gate.
    /// </summary>
    private static bool IsLegalTarget(ICard target)
    {
        if (target.Owner == null) return false;
        if (target.Zone != ZoneType.Battlefield) return false;
        if (target.HasType(CardType.Creature)) return false;
        return target.HasType(CardType.Artifact) || target.HasType(CardType.Enchantment);
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment. Idempotent if already sacrificed.
    /// Mirrors <see cref="AetherSpellbombFactory"/>'s self-sac closure — the
    /// shared <see cref="AdditionalCost.Sacrifice"/> Pay() is a no-op stub.
    /// </summary>
    private static void SacrificeSelf(Creature self, Player owner)
    {
        if (self.Zone != ZoneType.Battlefield) return;
        var holder = self.Controller ?? owner;
        var ownerOfSelf = self.Owner ?? owner;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }
}
