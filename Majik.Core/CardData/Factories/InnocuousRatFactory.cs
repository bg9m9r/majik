using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Innocuous Rat (Murders at Karlov Manor, {1}{B}).
///
/// Creature — Rat 1/1. Oracle text (verified against Scryfall):
///   "When this creature dies, manifest dread. (Look at the top two cards
///    of your library. Put one onto the battlefield face down as a 2/2
///    creature and the other into your graveyard. Turn it face up any time
///    for its mana cost if it's a creature card.)"
///
/// ## Why it gets its own factory
/// A vanilla 1/1 body plus one death-triggered ability that runs real
/// manifest dread (CR 701.59). Both halves already ship:
/// <see cref="Triggers.OnDies"/> for the dies condition (the same helper
/// <see cref="NecropedeFactory"/> uses) and
/// <see cref="ManifestDreadEffect.Resolve(Player, Majik.Core.Services.ZoneService?)"/>
/// for the manifest-dread body (the same effect
/// <see cref="AbhorrentOculusFactory"/> invokes). No new engine mechanic is
/// required — this factory just composes the two.
///
/// ## Implemented (v1)
/// - <b>Creature — Rat {1}{B} 1/1</b>. Card shape comes from the embedded JSON
///   (<c>innocuous-rat.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Dies trigger (CR 603.6c)</b>: fires on the Battlefield → Graveyard
///   transition via <see cref="Triggers.OnDies"/>. <c>activeZones</c> includes
///   <see cref="ZoneType.Battlefield"/> + <see cref="ZoneType.Graveyard"/> so
///   the trigger still matches after ZoneService stamps Zone = Graveyard
///   before publishing (mirrors <see cref="NecropedeFactory"/> /
///   <see cref="PersistFactory"/>). No targets, no "you may" — manifest dread
///   is mandatory.
/// - <b>Manifest dread (CR 701.59)</b>: the trigger resolves real manifest
///   dread for the Rat's controller via
///   <see cref="ManifestDreadEffect.Resolve(Player, Majik.Core.Services.ZoneService?)"/>.
///   Look at the top two cards of the controller's library, manifest the first
///   as a face-down 2/2 <see cref="ManifestedCreature"/> on the battlefield,
///   and put the second into the controller's graveyard. The wrapper preserves
///   a reference to the underlying card so the granted "turn face up for its
///   mana cost" activated ability (CR 708.6) can swap the wrapper out for the
///   printed creature if it's a creature card. The controller is read at
///   resolve time (capture <c>card</c>) so a control change between the Rat
///   dying and the trigger resolving manifests for the correct player.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The dies trigger is attached
///   for inspection but not registered (no trigger manager). Suitable for
///   dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, Majik.Core.Services.ZoneService?)"/> —
///   fully wired. When <paramref name="triggers"/> is supplied the trigger
///   registers with the live trigger manager; <paramref name="zones"/> threads
///   ZoneService-routed manifest dread (ETB / LTB triggers fire).
///
/// ## Rules citations
/// - CR 603.6c — death trigger ("When this creature dies").
/// - CR 701.59 — manifest dread.
/// - CR 708.2 / 708.6 — face-down permanents + turn-face-up for mana cost.
/// </summary>
[CardName("Innocuous Rat")]
public static class InnocuousRatFactory
{
    public const string CardName = "Innocuous Rat";
    public const string Slug = "innocuous-rat";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Innocuous Rat with no live wiring. The dies trigger is
    /// attached for shape inspection; manifest dread resolves via raw-zone
    /// moves (no ZoneService event routing). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Innocuous Rat with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional <see cref="TriggerManager"/>. When
    /// supplied, the dies trigger is registered so the Battlefield → Graveyard
    /// <c>CardMovedEvent</c> surfaces the manifest-dread trigger on the stack
    /// automatically (CR 603.3). May be null — the trigger is still attached to
    /// the card shape.</param>
    /// <param name="zones">Optional <see cref="Majik.Core.Services.ZoneService"/>
    /// for event-routed manifest dread resolution (ETB / LTB triggers fire).
    /// Null → raw-zone moves.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Majik.Core.Services.ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Rat,
        // {1}{B}, 1/1). No abilities in the JSON — the dies trigger is layered
        // on below.
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c.
        //   "When this creature dies, manifest dread."
        // Mandatory (no "you may"), no targets. The body runs real manifest
        // dread (CR 701.59) for the Rat's controller, read at resolve time so
        // control changes between death + resolution manifest for the right
        // player. activeZones includes Graveyard because ZoneService stamps
        // Zone = Graveyard before publishing the death move (mirrors Necropede).
        // ----------------------------------------------------------------
        var capturedCard = card;
        var capturedZones = zones;
        var manifestDreadEffect = new Effect(
            $"{CardName}: manifest dread (CR 701.59)",
            () => ManifestDreadEffect.Resolve(
                capturedCard.Controller ?? owner,
                capturedZones));

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { manifestDreadEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
