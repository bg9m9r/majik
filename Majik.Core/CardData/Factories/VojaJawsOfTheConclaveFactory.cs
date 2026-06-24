using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voja, Jaws of the Conclave (Murders at Karlov Manor
/// Commander, {2}{R}{G}{W}). Legendary Creature — Wolf, 5/5. Oracle text
/// (verified against Scryfall):
///   "Vigilance, trample, ward {3}
///    Whenever Voja attacks, put X +1/+1 counters on each creature you
///    control, where X is the number of Elves you control. Draw a card for
///    each Wolf you control."
///
/// The base shape (name, Legendary supertype, Creature, Wolf subtype,
/// {2}{R}{G}{W}, 5/5) is materialised from the embedded JSON definition
/// (<c>voja-jaws-of-the-conclave.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three keywords and the
/// attack trigger are layered on here (the JSON <c>AbilityDefinition</c>
/// schema doesn't express keyword markers or attack triggers — same posture as
/// <see cref="AdelineResplendentCatharFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Vigilance (CR 702.21), Trample (CR 702.19), Ward {3} (CR 702.21)</b> —
///   <see cref="KeywordAbility"/> markers so <c>ICard.Abilities</c> reflects
///   the printed line and Scryfall keyword parsing matches. Vigilance + trample
///   are read off the keyword set during combat; Ward is a keyword-surface
///   marker only (the spell/ability-targeting Ward consultation surface is a
///   documented cross-factory gap — same posture as
///   <see cref="AbolethSpawnFactory"/>).
///
/// - <b>"Whenever Voja attacks, put X +1/+1 counters on each creature you
///   control, where X is the number of Elves you control. Draw a card for each
///   Wolf you control." (CR 508.1 / 508.3g)</b> — a
///   <see cref="TriggeredAbility"/> scoped to
///   <see cref="AttackersDeclaredEvent"/> where Voja is among the declared
///   attackers ("Whenever Voja attacks", CR 508.3 — a self-attack trigger, the
///   same gate posture as <see cref="RaffineSchemingSeerFactory"/>). On
///   resolution:
///   <list type="number">
///     <item>X is read fresh as the number of Elves the controller controls
///       (CR 608.2 — resolved with current game state).</item>
///     <item>When X &gt; 0, X +1/+1 counters are placed on every creature the
///       controller controls (Voja included), routed through
///       <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///       replacements observe the placement (CR 614 / CR 121.2). When X = 0 no
///       counters are placed (CR 122.1 — zero counters is a no-op).</item>
///     <item>One card is drawn per Wolf the controller controls (Voja is a
///       Wolf, so the minimum is 1), routed through
///       <see cref="Fx.DrawCards"/> (replacement bus per draw; empty-library
///       loss flagged via SBA, CR 704.5c, rather than throwing).</item>
///   </list>
///   The controller is read off <c>ResolutionContext.Source</c>'s controller so
///   an Agatha / copy re-home pumps the BEARER's controller (CR 707.2 /
///   CR 613.1f); the trigger is registered <c>rebindSafe</c>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ward {3} consultation</b>: the targeting-time "counter unless its
///   controller pays {3}" surface is not yet wired (no Ward-trigger primitive);
///   Voja's Ward is a keyword marker only. Same cross-factory gap as Aboleth
///   Spawn / Tolarian Terror.
/// </summary>
[CardName("Voja, Jaws of the Conclave")]
public static class VojaJawsOfTheConclaveFactory
{
    public const string CardName = "Voja, Jaws of the Conclave";
    public const string Slug = "voja-jaws-of-the-conclave";

    /// <summary>Granted keywords — CR 702.21 / 702.19.</summary>
    public const string Vigilance = "Vigilance";
    public const string Trample = "Trample";
    public const string Ward = "Ward";

    /// <summary>CR 702.21 — printed Ward cost: {3}.</summary>
    public const int WardCost = 3;

    /// <summary>
    /// Construct Voja with no live runtime wiring (the dispatcher / shape
    /// path). The keywords and the attack trigger are attached for shape
    /// observability; with no triggers/replacement bus the trigger places no
    /// counters and draws no cards. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Voja with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the attack trigger registers with
    /// so an <see cref="AttackersDeclaredEvent"/> including Voja lands it on the
    /// stack. May be null.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> the
    /// +1/+1 counter placements route through (Hardened Scales / Doubling Season
    /// observe the bump). May be null — counters fall through to a direct
    /// add.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Wolf, {2}{R}{G}{W}, 5/5). No abilities in the JSON — all
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.21 / 702.19 — keyword markers.
        card.AddAbility(new KeywordAbility(Vigilance, card, owner));
        card.AddAbility(new KeywordAbility(Trample, card, owner));
        card.AddAbility(new KeywordAbility(Ward, card, owner, arg: WardCost));

        AddAttackTrigger(card, owner, triggers, replacements);

        return card;
    }

    /// <summary>
    /// Count creatures of <paramref name="subtype"/> the
    /// <paramref name="controller"/> controls. Pure helper exposed for tests;
    /// mirrors the closures baked into the live trigger.
    /// </summary>
    public static int CountSubtypeControlled(Player controller, CardSubtype subtype)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(subtype));
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever Voja attacks, put X +1/+1 counters on each
    // creature you control, where X is the number of Elves you control. Draw a
    // card for each Wolf you control." (CR 508.3.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            // "Whenever Voja attacks" — fires when Voja is among the declared
            // attackers (CR 508.3 — a self-attack trigger).
            e.Combat.Attackers.Any(a => ReferenceEquals(a.Creature, card)));

        var effect = new Effect(
            $"{CardName}: on attack, +X +1/+1 counters on each creature you control (X = Elves), draw per Wolf",
            ctx =>
            {
                ResolveAttackTrigger(card, owner, ctx, replacements);
                return ValueTask.CompletedTask;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield. (The effect reads
            // "each creature / Elves / Wolves YOU control" off
            // ResolutionContext.Source's controller, so an Agatha / copy re-home
            // pumps the bearer's controller — CR 707.2 / CR 613.1f.)
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static void ResolveAttackTrigger(
        Creature card,
        Player owner,
        ResolutionContext ctx,
        ReplacementBus? replacements)
    {
        // "you control" — read the live source's controller off
        // ResolutionContext.Source (the bearer after a RebindTo; otherwise this
        // Voja), never the captured card.
        var controller = (ctx.Source as Permanent)?.Controller
            ?? card.Controller ?? owner;

        // X = number of Elves you control (CR 608.2 — current game state).
        var x = CountSubtypeControlled(controller, CardSubtype.Elf);

        // "put X +1/+1 counters on each creature you control" — snapshot the
        // creatures first so adding counters doesn't perturb the iteration; X=0
        // is a no-op (CR 122.1). Voja herself is a creature you control.
        if (x > 0)
        {
            var creatures = controller.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .ToList();
            foreach (var creature in creatures)
            {
                CountersService.Add(creature, CounterType.PlusOnePlusOne, x, replacements);
            }
        }

        // "Draw a card for each Wolf you control" — Voja is a Wolf, so the
        // minimum is 1. Fx.DrawCards no-ops on 0 and flags empty-library loss
        // via SBA (CR 704.5c) rather than throwing.
        var wolves = CountSubtypeControlled(controller, CardSubtype.Wolf);
        Fx.DrawCards(controller, wolves);
    }
}
