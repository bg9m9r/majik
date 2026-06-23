using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mishra's Self-Replicator (The Brothers' War, {5}).
///
/// Artifact Creature — Assembly-Worker 2/2 (colourless). Oracle text
/// (Scryfall, verified):
///   "Whenever you cast a historic spell, you may pay {1}. If you do, create
///    a token that's a copy of this creature. (Artifacts, legendaries, and
///    Sagas are historic.)"
///
/// The base shape (name, Artifact + Creature types, Assembly-Worker subtype,
/// {5}, 2/2) is materialised from the embedded JSON definition
/// (<c>mishras-self-replicator.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities —
/// the printed trigger is layered on here (same posture as
/// <see cref="SamwiseGamgeeFactory"/>, whose JSON is shape-only).
///
/// ## Implemented (v1)
///
/// - 2/2 colourless <see cref="Creature"/> — Artifact, Assembly-Worker
///   subtype, mana cost {5}. Owner / controller wired.
/// - <b>Cast-historic-spell trigger (CR 603.1)</b>: a single
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> firing
///   when the controller casts a HISTORIC spell. Predicate gates on
///   (a) spell controller == this card's controller ("you cast", CR 109.5),
///   and (b) the spell's card is historic — Artifact / Legendary / Saga
///   (CR 205.2b / 205.4 / 714, via <see cref="MonumentalHengeFactory.IsHistoric"/>).
///   Same cast-event predicate shape as <see cref="SaiMasterThopteristFactory"/>'s
///   artifact-cast trigger, widened from "artifact" to "historic".
/// - <b>"You may pay {1}" optional rider (CR 117.5)</b>: on resolution the
///   controller's <see cref="IPlayerAgent"/> is consulted via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/>; agent-less callers auto-pay
///   if able (Mentor of the Meek posture). <see cref="Player.PayMana"/>
///   returns false when the pool can't satisfy {1}; the trigger fizzles
///   harmlessly.
/// - <b>"Create a token that's a copy of this creature" (CR 706 / 707.2)</b>:
///   if {1} is paid, a token is minted as a copy of this creature's copiable
///   values (CR 706.2 — name, base P/T, card types, subtypes, keyword names,
///   colours). Lossy v1 snapshot, mirroring
///   <see cref="EsikasChariotFactory"/>'s token-copy posture and
///   <see cref="Majik.Core.Effects.CopyEffect"/>. The copy is itself an
///   Artifact Creature — Assembly-Worker, so casting further historic spells
///   while it's on the battlefield triggers IT too (the printed self-replicating
///   feedback loop — each copy carries the same trigger).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The cast trigger is attached
///   for dispatcher / structural inspection; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring (the
///   token copy enters via the raw zone branch).
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired: the cast trigger registers for bus-driven firing and the token
///   copy routes through the ZoneService so its ETB
///   <see cref="CardMovedEvent"/> publishes.
///
/// ## Notes
///
/// - <b>Self-cast does NOT trigger</b>: the trigger fires on CASTING a
///   historic spell, not on this creature entering. Mishra's own ETB does
///   not fire it. (And the copy is created by a resolving ability, not cast,
///   so making a copy does not itself re-trigger — CR 707.2 / CR 601.)
/// - <b>Token copies carry the trigger</b>: each minted copy is built through
///   the same factory closure (a full copy-of-self via
///   <see cref="BuildSelfCopyToken"/>), so it independently watches future
///   historic casts — the printed snowball. The copy's trigger is registered
///   with the same <see cref="TriggerManager"/> when one is supplied.
/// </summary>
[CardName("Mishra's Self-Replicator")]
public static class MishrasSelfReplicatorFactory
{
    public const string CardName = "Mishra's Self-Replicator";
    public const string Slug = "mishras-self-replicator";
    public const string PrintedManaCost = "{5}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int OptionalManaCost = 1;

    /// <summary>
    /// Construct Mishra's Self-Replicator with no live wiring. The
    /// cast-historic-spell trigger is attached to the card shape for
    /// dispatcher / structural tests; not registered with any
    /// <see cref="TriggerManager"/> and no <see cref="ZoneService"/> is wired.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Mishra's Self-Replicator with optional runtime services.
    /// When <paramref name="triggers"/> is supplied the cast-historic-spell
    /// trigger registers so a matching <see cref="SpellCastEvent"/>
    /// (historic spell cast by the controller) automatically queues the
    /// may-pay-then-copy effect. <paramref name="zoneService"/> is threaded
    /// into the token-copy mint so the copy's ETB
    /// <see cref="CardMovedEvent"/> publishes.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact +
        // Creature, Assembly-Worker, {5}, 2/2). The JSON carries no
        // abilities — the cast trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AttachCastTrigger(card, owner, triggers, zoneService);

        return card;
    }

    /// <summary>
    /// Attach (and optionally register) the cast-historic-spell trigger to
    /// <paramref name="card"/>. Factored out so token COPIES — which must
    /// independently carry the same trigger (CR 706.2 copiable values include
    /// abilities) — wire it onto themselves through the same closure.
    /// </summary>
    private static void AttachCastTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        // ----------------------------------------------------------------
        // Cast-historic-spell trigger — CR 603.1.
        //   "Whenever you cast a historic spell, you may pay {1}. If you do,
        //    create a token that's a copy of this creature."
        //
        // Predicate:
        //   - Spell controller == this card's controller ("you cast",
        //     CR 109.5).
        //   - Spell's card is HISTORIC — Artifact / Legendary / Saga
        //     (CR 205.2b / 205.4 / 714, via MonumentalHengeFactory.IsHistoric).
        //
        // Same SpellCastEvent predicate shape as Sai, Master Thopterist's
        // artifact-cast trigger, widened "artifact" → "historic".
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            return MonumentalHengeFactory.IsHistoric(e.Spell.Card);
        });

        var copyEffect = new Effect(
            $"{CardName}: may pay {{{OptionalManaCost}}} → create a token copy of this creature",
            async ctx =>
            {
                // CR 603.6c — the source must still be on the battlefield to
                // resolve. activeZones gates the event match; the in-effect
                // check is defence-in-depth for manual Execute() calls.
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // "You may pay {1}" — consult the controller's agent.
                // Agent-less fallback: auto-pay if able (Mentor of the Meek
                // posture).
                var oneGeneric = ManaCost.Zero.AddGenericCost(OptionalManaCost);
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool pay = agent == null
                    || await agent.ChooseYesNoAsync(
                        $"Pay {{{OptionalManaCost}}} to copy {CardName}?",
                        BotIntent.Token).ConfigureAwait(false);

                if (!pay) return;

                // CR 117.5 — optional may-pay; trigger fizzles when the mana
                // isn't available.
                if (!controller.PayMana(oneGeneric)) return;

                // CR 706 / 707.2 — create a token that's a copy of this
                // creature. The copy snapshots the source's copiable values
                // and carries the same cast trigger (the self-replicating
                // snowball).
                BuildSelfCopyToken(card, controller, triggers, zoneService);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { copyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);
    }

    /// <summary>
    /// CR 706.2 — mint a token that's a copy of <paramref name="source"/>
    /// under <paramref name="controller"/>'s control. The copy snapshots the
    /// source's copiable values (name, base P/T, card types, subtypes,
    /// keyword names, colours) — lossy v1, mirroring
    /// <see cref="EsikasChariotFactory"/>'s token-copy posture — and then
    /// re-attaches the cast-historic-spell trigger so each copy independently
    /// watches future historic casts (the printed self-replicating loop).
    /// </summary>
    public static Creature BuildSelfCopyToken(
        Creature source,
        Player controller,
        TriggerManager? triggers = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(controller);

        // CR 706.2 copiable values snapshot — keyword names, colours.
        var keywords = source.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        var colours = CardColors.GetColors(source).ToList();

        var spec = new TokenFactory.TokenSpec(
            Name: source.Name,
            Power: source.BasePower,
            Toughness: source.BaseToughness,
            Subtypes: source.Subtypes.ToList(),
            Keywords: keywords,
            Colors: colours);

        var copy = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 706.2 — copy the source's card types beyond the Creature stamp
        // TokenFactory applies. Mishra's is an Artifact Creature, so the copy
        // must also report Artifact (same additive-type posture as Sai's
        // Thopter / Whirler Virtuoso).
        foreach (var t in source.CardTypes)
        {
            if (!copy.HasType(t)) copy.AddCardType(t);
        }

        // CR 706.2 — copiable values include the source's abilities. Re-attach
        // the cast-historic-spell trigger to the copy so it carries the same
        // self-replicating behaviour (each copy can make further copies).
        AttachCastTrigger(copy, controller, triggers, zoneService);

        return copy;
    }
}
