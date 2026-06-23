using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormsplitter (Outlaws of Thunder Junction, {3}{R}).
///
/// Creature — Otter Wizard 1/4. Oracle text (Scryfall, verified 2026-06-23):
///   "Haste
///    Whenever you cast an instant or sorcery spell, create a token that's a
///    copy of this creature. Exile that token at the beginning of the next end
///    step."
///
/// ## Implementation
///
/// The base shape (name, Creature, Otter + Wizard subtypes, {3}{R}, 1/4, the
/// Haste keyword marker) is materialised from the embedded JSON definition
/// (<c>stormsplitter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> (Haste → a
/// <see cref="KeywordAbility"/> marker, CR 702.10). The on-cast self-copy
/// trigger is layered on in C# — the JSON <c>AbilityDefinition</c> schema
/// doesn't express the on-cast token-copy trigger (same posture as
/// <see cref="MurmuringMysticFactory"/> / <see cref="YoungPyromancerFactory"/>).
///
/// - <b>Instant/sorcery-cast self-copy trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   matches when the spell's controller is Stormsplitter's controller AND the
///   spell's card has type <see cref="CardType.Instant"/> or
///   <see cref="CardType.Sorcery"/> (CR 300.1 / 307.1). Creature spells do not
///   fire even if they carry a secondary instant/sorcery type — the printed
///   oracle tests the card types of the spell as cast (CR 112.1).
///
/// - <b>"a token that's a copy of this creature" (CR 706 / 707.2)</b>: on
///   resolution, snapshot Stormsplitter's own copiable values per CR 706.2
///   (name, base P/T, subtypes, keyword names — which include Haste — and
///   colour identity) into a fresh token via
///   <see cref="TokenFactory.CreateOnBattlefield"/> under Stormsplitter's
///   controller. The token enters with Haste copied (CR 702.10b — summoning
///   sickness cleared) and routes through <see cref="ZoneService"/> when one is
///   wired so token-ETB triggers (Impact Tremors / Purphoros) fire. Lossy v1
///   copy: the token snapshots base characteristics at resolve and does not
///   track later changes to Stormsplitter (same posture as
///   <see cref="KikiJikiMirrorBreakerFactory"/> / <see cref="SplinterTwinFactory"/>).
///
/// - <b>"Exile that token at the beginning of the next end step" (CR 603.7)</b>:
///   each resolution registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> on the supplied
///   <see cref="TriggerManager"/>, closed over the specific token minted by
///   THIS resolution and gated to the next <see cref="StepStateType.End"/> step
///   strictly after the resolution timestamp (mirrors Kiki-Jiki's delayed
///   end-step exile so multiple casts in a turn each exile their own token).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. The cast
///   trigger is attached structurally; no bus / trigger-manager / zone-service
///   wiring. Suitable for <see cref="NamedCardFactory"/> dispatch / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?)"/> —
///   fully-wired overload. The cast trigger is registered so the bus surfaces it
///   on a matching <see cref="SpellCastEvent"/>; tokens publish
///   <see cref="CardMovedEvent"/> on ETB and the delayed end-step exile is
///   bus-driven.
///
/// ## Deferred (v1 gaps)
/// - <b>Layer 1/6 copy fidelity</b>: the token is a resolve-time snapshot of
///   Stormsplitter's base characteristics + keyword names; it does not relay
///   later characteristic changes, nor does it itself re-fire a "whenever you
///   cast" trigger on subsequent spells unless the copy's own
///   <see cref="TriggeredAbility"/> were registered (the snapshot copies the
///   keyword markers, not the bespoke C#-layered cast trigger). Aligns with the
///   v1 <see cref="CopyEffect"/> lossiness documented on Kiki-Jiki / Splinter
///   Twin.
/// </summary>
[CardName("Stormsplitter")]
public static class StormsplitterFactory
{
    public const string CardName = "Stormsplitter";
    public const string Slug = "stormsplitter";
    public const string PrintedManaCost = "{3}{R}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Stormsplitter with no live bus / trigger-manager / zone-service
    /// wiring. The cast trigger is attached to the card for shape observability;
    /// tokens land via raw zone moves and the delayed end-step exile is NOT
    /// registered. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Stormsplitter with optional event bus + trigger manager +
    /// zone service. When <paramref name="triggers"/> is supplied the cast
    /// trigger is registered so the bus surfaces it as pending on a matching
    /// <see cref="SpellCastEvent"/>, and each resolution registers a delayed
    /// end-step exile for the token it spawns.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, create a
        // token that's a copy of this creature. Exile that token at the
        // beginning of the next end step."
        // Predicate: spell controller matches AND the spell has Instant or
        // Sorcery card type (CR 300.1 / 307.1).
        var tokenCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var tokenEffect = new Effect(
            $"{CardName}: create a token copy of this creature, exile it at the next end step (whenever you cast an instant or sorcery spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                var token = CreateSelfCopyToken(card, controller, zoneService);
                RegisterEndStepExile(token, controller, triggers, zoneService);
            });

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tokenCondition,
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 706.2 — create a token that's a copy of <paramref name="self"/>
    /// (Stormsplitter) under <paramref name="controller"/>'s control. Snapshots
    /// copiable values: name, base P/T, subtypes, keyword names (including
    /// Haste), and colour identity. The token clears summoning sickness because
    /// the copied Haste keyword (CR 702.10b) lets it attack immediately.
    /// </summary>
    public static Creature CreateSelfCopyToken(
        Creature self,
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(controller);

        // CR 706.2 — snapshot copiable values: keyword names (Haste included),
        // colours, subtypes, base P/T, name. v1 lossy w.r.t. later changes.
        var keywords = self.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        var colours = CardColors.GetColors(self).ToList();

        var spec = new TokenFactory.TokenSpec(
            Name: self.Name,
            Power: self.BasePower,
            Toughness: self.BaseToughness,
            Subtypes: self.Subtypes.ToList(),
            Keywords: keywords,
            Colors: colours);

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 702.10b — the copied Haste means the token can attack the turn it
        // enters; clear summoning sickness.
        token.HasSummoningSickness = false;

        return token;
    }

    /// <summary>
    /// CR 603.7 — register a one-shot delayed triggered ability that exiles
    /// <paramref name="token"/> at the beginning of the next end step. The
    /// closure captures the specific token minted by this resolution and the
    /// trigger fires only on an <see cref="StepStateType.End"/> step strictly
    /// after the resolution timestamp, so each cast exiles its own token. No-op
    /// when <paramref name="triggers"/> is null (shape / dispatcher path).
    /// </summary>
    private static void RegisterEndStepExile(
        Creature token,
        Player controller,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();

        var exileEffect = new Effect(
            $"{CardName}: exile token at next end step",
            () =>
            {
                if (token.Zone != ZoneType.Battlefield) return;
                if (!controller.Zones.Battlefield.GetCards().Contains(token)) return;

                if (zoneService != null)
                {
                    zoneService.MoveCard(token, ZoneType.Battlefield, ZoneType.Exile, controller);
                }
                else
                {
                    controller.Zones.Battlefield.RemoveCard(token);
                    controller.Zones.Exile.AddCard(token);
                    token.SetZone(ZoneType.Exile);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: token,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == StepStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { exileEffect });

        triggers.RegisterDelayed(delayed);
    }
}
