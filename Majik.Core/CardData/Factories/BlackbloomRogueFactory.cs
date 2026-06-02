using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Blackbloom Rogue // Blackbloom Bog (Zendikar Rising, {2}{B}).
///
/// Creature — Human Rogue 2/3. Oracle text (front, verified against Scryfall
/// 2026-06):
///   "Menace (This creature can't be blocked except by two or more creatures.)"
///   "This creature gets +3/+0 as long as an opponent has eight or more cards
///    in their graveyard."
///
/// Back face — <see cref="BlackbloomBogFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {B}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AkoumWarriorFactory"/> / <see cref="AkoumTeethFactory"/> (the
/// companion ZNR creature-front + tapland-back MDFC). The front-face card
/// carries a castable <see cref="MdfcFace.Land"/> back-face descriptor on its
/// <see cref="MdfcState"/> so <see cref="Majik.Core.Game.MdfcCastFlow"/> can
/// offer the controller a face choice at play time and materialize a fresh
/// back-face land instance (Blackbloom Bog) when chosen. No transform happens —
/// only the chosen face exists (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 2/3 P/T are loaded from the embedded JSON
/// definition (<c>blackbloom-rogue.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker, the Menace keyword marker, and the graveyard-conditional self-pump
/// are attached in code (the JSON <c>AbilityDefinition</c> schema models none
/// of them).
///
/// ## Implemented (v1)
///
/// - 2/3 Creature — Human Rogue, mana cost {2}{B}, owner / controller wired
///   (from JSON).
/// - <see cref="MdfcState"/> attached (front = "Blackbloom Rogue", back =
///   "Blackbloom Bog") with a castable <see cref="MdfcFace.Land"/> back face;
///   starts on the front face.
/// - <b>Menace</b> (CR 702.111) as a <see cref="KeywordAbility"/> marker — the
///   source-of-truth read by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/>
///   for the "can't be blocked except by two or more creatures" combat rule.
///   Same marker posture as <see cref="GriefFactory"/> / <see cref="AkoumWarriorFactory"/>'s
///   Trample.
/// - <b>Graveyard-conditional self-pump (CR 613.7c — Layer 7c)</b>: an
///   <see cref="OpponentGraveyardSelfPumpStaticEffect"/> registers against the
///   supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Blackbloom
///   Rogue the effect tests whether at least one OPPONENT has
///   <see cref="GraveyardThreshold"/> (eight) or more cards in their graveyard
///   and applies +3/+0 when so. The condition re-evaluates dynamically: an
///   opponent's graveyard crossing the threshold flips the bonus on, dropping
///   back below it flips it off — no trigger / re-register cycle required. Same
///   self-pump shape as <see cref="InventorsApprenticeFactory"/>, but the
///   predicate reads opponents (reached via an injected
///   <c>allPlayersResolver</c>, the same posture as
///   <see cref="ScourgeOfTheSkyclavesFactory"/>) rather than the controller's
///   own battlefield.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="InventorsApprenticeFactory"/>:
/// subscribe to <see cref="CardMovedEvent"/>; register the static effect when
/// Blackbloom Rogue enters the battlefield, unregister when it leaves. The
/// <see cref="OpponentGraveyardSelfPumpStaticEffect.IsActive"/> battlefield gate
/// is belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Menace marker + castable-land
///   MDFC are attached; the layer-7c pump is NOT (no continuous-effects
///   service / opponents resolver). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, Func{IReadOnlyList{Player}}, ContinuousEffectsService, IEventBus)"/>
///   — fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## "An opponent" semantics (CR 102.1 / CR 109.5)
///
/// "An opponent has eight or more cards in their graveyard" reads true when
/// AT LEAST ONE player other than Blackbloom Rogue's controller has eight or
/// more cards in their graveyard. The controller's own graveyard never
/// satisfies the predicate. Evaluated live against the resolver, so a
/// control-changing effect on Blackbloom Rogue re-scopes "opponent" through
/// the new controller (CR 109.5).
///
/// ## References
///
/// - <see cref="AkoumWarriorFactory"/> — companion ZNR creature-front MDFC with
///   the same castable-land-back MdfcState shape.
/// - <see cref="InventorsApprenticeFactory"/> — the conditional self-pump
///   Layer-7c shape this factory mirrors (swapping the predicate + bonus).
/// - <see cref="ScourgeOfTheSkyclavesFactory"/> — the <c>allPlayersResolver</c>
///   posture used to reach opponents from a continuous effect.
/// </summary>
[CardName("Blackbloom Rogue")]
public static class BlackbloomRogueFactory
{
    public const string CardName = "Blackbloom Rogue";
    public const string BackName = "Blackbloom Bog";
    public const string Slug = "blackbloom-rogue";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>CR-text constants for the conditional pump.</summary>
    public const int GraveyardThreshold = 8;
    public const int BonusPower = 3;
    public const int BonusToughness = 0;

    /// <summary>
    /// Construct Blackbloom Rogue with no live pump wiring. Identity (name /
    /// Creature / Human Rogue subtypes / {2}{B} / 2/3) comes from the embedded
    /// JSON definition; the <see cref="MdfcState"/> with the castable land back
    /// face and the Menace keyword marker are layered on in code. The
    /// graveyard-conditional pump is NOT attached (no continuous-effects
    /// service). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, allPlayersResolver: null, effects: null, eventBus: null);

    /// <summary>
    /// Construct Blackbloom Rogue with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list at
    /// evaluation time. The pump counts each player's graveyard, excluding the
    /// controller's, and applies +3/+0 when any opponent has eight or more.
    /// Null → the pump body no-ops (shape path).</param>
    /// <param name="effects">Continuous-effects service the pump registers
    /// against on ETB. Pass null for shape-only P/T.</param>
    /// <param name="eventBus">Event bus for ETB/LTB pump tracking. May be null —
    /// the pump's battlefield gate covers correctness.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human/
        // Rogue subtypes, {2}{B}, 2/3). The JSON carries no abilities — the MDFC
        // face tracker + Menace marker + conditional pump are layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at play time and materializes a fresh
        // back-face land instance (wired to its ETB "enters tapped"
        // replacement via the supplied ReplacementBus) when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                BlackbloomBogFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        // CR 702.111 — Menace, as a KeywordAbility marker read by
        // CombatAbilities.HasMenace for the "blocked only by two or more"
        // combat rule.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // CR 613.7c — graveyard-conditional self-pump. Register the Layer-7c
        // continuous effect on ETB; unregister on LTB.
        if (effects != null)
        {
            var lifecycle = new GraveyardPumpLifecycle(card, allPlayersResolver, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 102.1 / CR 109.5 — "an opponent has eight or more cards in their
    /// graveyard." True when at least one player OTHER than
    /// <paramref name="controller"/> has <see cref="GraveyardThreshold"/> or
    /// more cards in their graveyard. The controller's own graveyard is never
    /// counted. An empty / null player list yields false (no opponent to read).
    /// </summary>
    public static bool AnyOpponentHasFullGraveyard(
        Player controller,
        IReadOnlyList<Player>? allPlayers)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (allPlayers == null) return false;

        foreach (var player in allPlayers)
        {
            if (player == null) continue;
            if (ReferenceEquals(player, controller)) continue; // CR 102.1 — not the controller.
            if (player.Zones.Graveyard.Count >= GraveyardThreshold) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // OpponentGraveyardSelfPumpStaticEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Blackbloom Rogue's graveyard pump. On
    /// every <see cref="ContinuousEffectsService.Compute"/> invocation the
    /// effect tests whether any opponent has eight or more cards in their
    /// graveyard and, if so, applies +3/+0 to Blackbloom Rogue. Without a
    /// qualifying opponent the effect contributes nothing (CR 613.7c — a
    /// continuous effect that reads "as long as" gates its application on the
    /// predicate; it does not unregister, but its <see cref="AppliesTo"/>
    /// returns true and <see cref="Apply"/> contributes 0 when the predicate is
    /// false).
    ///
    /// Active only while Blackbloom Rogue is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="GraveyardPumpLifecycle"/>). Mirrors
    /// <see cref="InventorsApprenticeFactory.ArtifactSelfPumpStaticEffect"/>,
    /// swapping the predicate (opponent-graveyard count, via the resolver) and
    /// the bonus (+3/+0).
    /// </summary>
    public sealed class OpponentGraveyardSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Func<IReadOnlyList<Player>>? _allPlayersResolver;

        public OpponentGraveyardSelfPumpStaticEffect(
            Creature source,
            Func<IReadOnlyList<Player>>? allPlayersResolver)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _allPlayersResolver = allPlayersResolver;
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Blackbloom Rogue is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Blackbloom Rogue itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +3/+0 when an opponent has eight or more cards in their
        /// graveyard; otherwise no contribution. Reads
        /// <see cref="Permanent.Controller"/> live so a control-changing effect
        /// on Blackbloom Rogue re-scopes "opponent" through the new controller
        /// (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            var players = _allPlayersResolver?.Invoke();
            if (!AnyOpponentHasFullGraveyard(controller, players)) return;
            chars.Power += BonusPower;
            chars.Toughness += BonusToughness;
        }
    }

    // -----------------------------------------------------------------------
    // GraveyardPumpLifecycle — ETB/LTB wiring for the graveyard pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Blackbloom Rogue's graveyard pump.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="OpponentGraveyardSelfPumpStaticEffect"/> when Blackbloom Rogue
    /// enters the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="InventorsApprenticeFactory"/>'s <c>ArtifactPumpLifecycle</c>.
    /// </summary>
    private sealed class GraveyardPumpLifecycle
    {
        private readonly Creature _source;
        private readonly Func<IReadOnlyList<Player>>? _allPlayersResolver;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private OpponentGraveyardSelfPumpStaticEffect? _registered;
        private bool _attached;

        public GraveyardPumpLifecycle(
            Creature source,
            Func<IReadOnlyList<Player>>? allPlayersResolver,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _allPlayersResolver = allPlayersResolver;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new OpponentGraveyardSelfPumpStaticEffect(_source, _allPlayersResolver);
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
