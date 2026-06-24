using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Caretaker's Talent (Bloomburrow, {2}{W}).
///
/// Enchantment — Class {2}{W}. Oracle text (verified against Scryfall):
///   "(Gain the next level as a sorcery to add its ability.)
///    Whenever one or more tokens you control enter, draw a card. This
///      ability triggers only once each turn.
///    {W}: Level 2
///    When this Class becomes level 2, create a token that's a copy of
///      target token you control.
///    {3}{W}: Level 3
///    Creature tokens you control get +2/+2."
///
/// ## Implementation (full Class leveling — CR 716)
/// Mirrors <see cref="ArtistsTalentFactory"/> / <see cref="BanditsTalentFactory"/>
/// (the Enchantment — Class shell + <see cref="ClassState"/> side-table +
/// sorcery-speed level-up activated abilities), with Caretaker's Talent's
/// three abilities:
///
/// - <b>Level 1 — token-ETB draw, once each turn</b> (CR 603.1 / 603.2c):
///   a <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> that
///   fires when a token (<see cref="Permanent.IsToken"/> — the same probe
///   <see cref="AnointerPriestFactory"/> uses) the controller controls enters
///   the battlefield. "This ability triggers only once each turn" is a
///   captured <c>firedThisTurn</c> flag set in-resolution and reset on every
///   <see cref="TurnStartedEvent"/> (CR 500.1) — identical machinery to
///   <see cref="EnduringInnocenceFactory"/>'s once-per-turn ETB draw. Level-1
///   abilities are unconditional (a Class enters at level 1 with its level-1
///   ability active, CR 716.2).
///
///   The "one or more tokens … enter" batch wording (CR 603.3b — a single
///   ability triggers once for a batch of simultaneous enters) is approximated
///   by the once-per-turn flag: the first token's enter draws, and any further
///   token-enters that turn (batched or sequential) are suppressed. This
///   matches the printed once-each-turn outcome; see the deferred note.
///
/// - <b>Level 2 — "becomes level 2" copy-token trigger</b> (CR 716.2d /
///   CR 706): a <see cref="TriggeredAbility"/> over <see cref="ClassLevelUpEvent"/>
///   filtered to THIS Class advancing to level 2. "Create a token that's a
///   copy of target token you control" is a true target (CR 115.1): a 1..1
///   <see cref="TargetRequest"/> over the controller's token creatures routes
///   the choice through the shared prod targeting seam, exactly like
///   <see cref="EsikasChariotFactory"/>'s attack copy-trigger (whose
///   resolution helper this reuses verbatim). The level-2 activated ability
///   only advances the level; the becomes-level-2 trigger fires off the
///   <see cref="ClassState.OnLevelUp"/> hook's published event.
///
/// - <b>Level 3 — creature-token anthem</b> (CR 613.7c): "Creature tokens you
///   control get +2/+2." A <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: null</c> + <c>tokensOnly: true</c> (CR 111 — only
///   token creatures), scoped to the source's controller — the same shape as
///   <see cref="IntangibleVirtueFactory"/>, only +2/+2 instead of +1/+1 and
///   no granted keyword. Because <see cref="LordStaticEffect"/> is sealed and
///   exposes no level predicate, the anthem is REGISTERED on the
///   <see cref="ContinuousEffectsService"/> the moment the Class reaches level
///   3 (via the level-up hook), not gated up-front — keeping the well-tested,
///   sim-cloneable lord effect. Classes only gain levels (CR 716 — levels
///   never decrease), so a once-registered level-3 anthem stays correct.
///
/// ## Deferred (v1 gaps — shared with the Class / token families)
/// - <b>"One or more tokens … enter" true batch semantics</b>: CR 603.3b says
///   a single instance of "whenever one or more … enter" triggers once for a
///   batch of simultaneous enters. v1 fires on the first qualifying token-ETB
///   and relies on the once-per-turn flag for the rest, which yields the same
///   single-draw-per-turn outcome the card prints. A real batched-enter event
///   awaits an engine-wide simultaneous-ETB grouping primitive.
/// - <b>Copy of a NONCREATURE token</b>: "target token you control" includes
///   Treasure / Food / Clue tokens. The copy snapshot (name + base P/T +
///   subtypes + keywords + colours) is creature-shaped, so the target request
///   + <see cref="EsikasChariotFactory"/> copy helper scope to token
///   creatures — the same v1 restriction Esika's Chariot carries. Copying a
///   noncreature token awaits a noncreature-token copy primitive.
/// </summary>
[CardName("Caretaker's Talent")]
public static class CaretakersTalentFactory
{
    public const string CardName = "Caretaker's Talent";
    public const string PrintedManaCost = "{2}{W}";
    public const string Level2Cost = "{W}";
    public const string Level3Cost = "{3}{W}";

    /// <summary>Anthem boost granted to creature tokens you control at level 3.</summary>
    public const int AnthemBonus = 2;

    /// <summary>
    /// Construct Caretaker's Talent with no live runtime services. The
    /// Level-1 token-ETB draw trigger, the two level-up activated abilities,
    /// and the Level-2 becomes-level-2 copy trigger are attached for shape
    /// inspection. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Construct Caretaker's Talent with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the Level-1 token-ETB draw
    /// trigger + the Level-2 becomes-level-2 copy trigger are registered so
    /// the bus surfaces them as pending on matching events.</param>
    /// <param name="eventBus">When supplied, level-up resolutions publish
    /// <see cref="ClassLevelUpEvent"/> (which the copy trigger listens for),
    /// and a <see cref="TurnStartedEvent"/> handler resets the once-per-turn
    /// flag (CR 500.1).</param>
    /// <param name="continuousEffects">When supplied, the Level-3 +2/+2
    /// creature-token anthem is registered against the layers service the
    /// moment the Class reaches level 3.</param>
    /// <param name="zoneService">When supplied, the copied token routes
    /// through <see cref="TokenFactory.CreateOnBattlefield"/> using the
    /// service so the copy publishes <see cref="CardMovedEvent"/> on entry.</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus = null,
        ContinuousEffectsService? continuousEffects = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Class });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Class state binder (CR 716). MaxLevel=3, per-level costs {W} / {3}{W}.
        // The OnLevelUp hook publishes ClassLevelUpEvent (when a bus exists)
        // AND registers the Level-3 anthem once level 3 is reached.
        // ----------------------------------------------------------------
        var classState = new ClassState(
            maxLevel: 3,
            levelUpCosts: new[]
            {
                ManaCost.Parse(Level2Cost),
                ManaCost.Parse(Level3Cost),
            });

        classState.OnLevelUp = (from, to) =>
        {
            // Publish for the becomes-level-2 copy trigger + UI/bots.
            eventBus?.Publish(new ClassLevelUpEvent(card, card.Controller ?? owner, from, to));

            // CR 716.2c — the Level-3 static anthem exists only while at
            // level 3. Register the lord effect the instant the Class reaches
            // level 3 (Classes never lose levels — CR 716, so it stays valid).
            if (to == 3 && continuousEffects != null)
            {
                RegisterLevelThreeAnthem(card, continuousEffects);
            }
        };

        card.AttachClassState(classState);

        // ----------------------------------------------------------------
        // Level 1 — "Whenever one or more tokens you control enter, draw a
        // card. This ability triggers only once each turn." (CR 603.1 /
        // 603.2c — active from level 1, no level gate.)
        // ----------------------------------------------------------------
        var firedThisTurn = false;

        var drawCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (firedThisTurn) return false;                         // CR 603.2c — once each turn
            if (e.ToZone != ZoneType.Battlefield) return false;     // entering the battlefield
            if (e.Card is not Permanent perm || !perm.IsToken) return false; // CR 111 — a token
            // CR 603.1 "you control" — the post-ETB controller is the correct read.
            return ReferenceEquals(e.Card.Controller, card.Controller ?? owner);
        });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: drawCondition,
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: a token you control entered — draw a card (once each turn)",
                    () =>
                    {
                        // CR 603.2c — mark fired BEFORE drawing so a same-turn
                        // re-entry can't re-arm it; reset on TurnStartedEvent.
                        firedThisTurn = true;
                        Fx.DrawCards(card.Controller ?? owner, 1);
                    }),
            },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        // CR 500.1 — a new turn re-arms the once-per-turn ability.
        eventBus?.Subscribe<TurnStartedEvent>(_ => firedThisTurn = false);

        // ----------------------------------------------------------------
        // Level-up activated abilities — CR 716.4 (sequential), sorcery speed
        // (CR 716.3). Mirrors ArtistsTalentFactory / BanditsTalentFactory.
        // ----------------------------------------------------------------
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 2));
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 3));

        // ----------------------------------------------------------------
        // Level 2 — "When this Class becomes level 2, create a token that's a
        // copy of target token you control." (CR 716.2d / CR 706.)
        // A TriggeredAbility over ClassLevelUpEvent (this Class → level 2)
        // with a 1..1 "target token you control" request. Resolution reuses
        // Esika's Chariot's copy helper.
        // ----------------------------------------------------------------
        TriggeredAbility? copyTrigger = null;

        var copyCondition = new EventTriggerCondition<ClassLevelUpEvent>((e, _) =>
            ReferenceEquals(e.Source, card) && e.ToLevel == 2);

        var copyEffect = new Effect(
            $"{CardName}: create a token that's a copy of target token you control",
            () => CreateCopyOfTargetToken(card.Controller ?? owner, copyTrigger, zoneService));

        copyTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: copyCondition,
            effects: new IEffect[] { copyEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { BuildCopyTargetRequest(card, owner) });

        card.AddAbility(copyTrigger);
        triggers?.RegisterTriggeredAbility(copyTrigger);

        return card;
    }

    /// <summary>
    /// CR 613.7c — register the Level-3 "Creature tokens you control get
    /// +2/+2" anthem against <paramref name="continuousEffects"/>. No subtype
    /// gate (<c>matchingSubtype: null</c>), token-only (CR 111), scoped to the
    /// source's controller. Exposed for direct invocation by tests; the live
    /// path registers it via the level-up hook when the Class reaches level 3.
    /// </summary>
    public static void RegisterLevelThreeAnthem(
        Enchantment card, ContinuousEffectsService continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(continuousEffects);

        card.ActiveEffects = continuousEffects;
        continuousEffects.Register(new LordStaticEffect(
            source: card,
            matchingSubtype: null,
            power: AnthemBonus,
            toughness: AnthemBonus,
            tokensOnly: true));
    }

    /// <summary>
    /// Build the "Level up to <paramref name="targetLevel"/>" sorcery-speed
    /// activated ability (CR 716.3 / 716.4). Mirrors
    /// <see cref="ArtistsTalentFactory"/>.
    /// </summary>
    private static ActivatedAbility BuildLevelUpAbility(
        Enchantment card, Player owner, ClassState classState, int targetLevel)
    {
        var cost = classState.CostFor(targetLevel);

        var effect = new Effect(
            $"{CardName}: level up to {targetLevel}",
            () => classState.LevelUpTo(targetLevel));

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: new IEffect[] { effect },
            sorcerySpeed: true);
    }

    /// <summary>
    /// CR 115.1 — the "target token you control" request for the
    /// becomes-level-2 copy trigger. Candidates are enumerated live at
    /// agent-prompt time from the Class's CURRENT controller's battlefield
    /// (so a controller change still scopes "you control" correctly) and
    /// restricted to token creatures (CR 111.10 — see the noncreature-token
    /// deferral in the class xmldoc). MinTargets/MaxTargets = 1.
    /// </summary>
    private static TargetRequest BuildCopyTargetRequest(Enchantment card, Player owner)
    {
        return new TargetRequest(
            Description: "target token you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ =>
            {
                var ctrl = card.Controller ?? owner;
                if (ctrl == null) return Array.Empty<object>();
                return ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.IsToken && ReferenceEquals(c.Controller, ctrl))
                    .Cast<object>()
                    .ToList();
            });
    }

    /// <summary>
    /// CR 706 copy-token effect — create a token that's a copy of the chosen
    /// token <paramref name="controller"/> controls. Target resolution +
    /// the lossy CR 706.2 copiable-values snapshot (name + base P/T + subtypes
    /// + keyword names + colours) are delegated to
    /// <see cref="EsikasChariotFactory.CreateCopyOfTargetToken"/> — the
    /// identical "copy of target token you control" mechanic.
    /// </summary>
    private static Creature? CreateCopyOfTargetToken(
        Player controller, TriggeredAbility? trigger, ZoneService? zones) =>
        EsikasChariotFactory.CreateCopyOfTargetToken(
            controller: controller,
            trigger: trigger,
            picker: null,
            zones: zones);
}
