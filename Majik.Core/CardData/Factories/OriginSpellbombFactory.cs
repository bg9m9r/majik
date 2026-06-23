using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Origin Spellbomb (Magic 2014 / reprints).
///
/// Artifact — {1}. Oracle text (Scryfall, verified):
///   "{1}, {T}, Sacrifice this artifact: Create a 1/1 colorless Myr artifact
///    creature token.
///    When this artifact is put into a graveyard from the battlefield, you may
///    pay {W}. If you do, draw a card."
///
/// ## Shape source
///
/// Card identity (name, {1}, Artifact) is loaded from
/// <c>Majik.Core/CardData/Cards/origin-spellbomb.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The activated ability + dies trigger
/// are wired in code below.
///
/// ## Implemented (v1)
/// - <b>{1}, {T}, Sacrifice this artifact: Create a 1/1 colorless Myr artifact
///   creature token</b> — wired as an <see cref="ActivatedAbility"/> whose cost
///   is <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost"/>.Tap +
///   self-sacrifice (Battlefield → Graveyard). The sacrifice is performed by
///   the effect closure (the generic <see cref="AdditionalCost.Pay"/> sacrifice
///   path is a no-op stub — same posture as <see cref="NihilSpellbombFactory"/>
///   / <see cref="AetherSpellbombFactory"/>). On resolution a 1/1 colourless
///   Myr artifact-creature token is minted under the controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111.1 / CR 111.4) — the
///   <see cref="CardSubtype.Myr"/> creature shell is additively stamped
///   <see cref="CardType.Artifact"/> for the artifact-creature multi-type,
///   mirroring <see cref="ServoSchematicFactory"/>'s Servo wiring.
/// - <b>Dies trigger — CR 603.6c</b>: "When this artifact is put into a
///   graveyard from the battlefield, you may pay {W}. If you do, draw a card."
///   Fires on a Battlefield → Graveyard <see cref="CardMovedEvent"/> matching
///   this card (<see cref="Triggers.OnDies"/>). v1 auto-pays {W} from the
///   controller's mana pool when it can cover it ("you may" defaults to
///   accepting when mana is available — identical posture to
///   <see cref="NihilSpellbombFactory"/>'s {B} leg); draws one card on success.
///   <c>activeZones</c> spans Battlefield + Graveyard so the trigger still
///   matches after ZoneService stamps Zone = Graveyard before publishing.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt for {W} payment</b>: v1 auto-accepts when the mana
///   pool has {W} (same posture as Nihil Spellbomb / Sneak Attack). A real
///   prompt is deferred until IPlayerAgent grows a yes/no payment surface.
/// - <b>Sacrifice payment side effects</b>: same no-op stub as Nihil/Aether
///   Spellbomb — the effect closure performs the zone move.
/// </summary>
[CardName("Origin Spellbomb")]
public static class OriginSpellbombFactory
{
    public const string CardName = "Origin Spellbomb";
    public const string MyrTokenName = "Myr";
    public const int MyrPower = 1;
    public const int MyrToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("origin-spellbomb");

    /// <summary>
    /// Construct Origin Spellbomb. The dies trigger is attached to the card
    /// shape but not registered with a TriggerManager (suitable for shape and
    /// dispatcher tests). Token tokens enter via the raw zone path.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, zones: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — see <see cref="FestivalCrasherFactory"/>). Threads
    /// <c>effects.EventBus</c> into the self-sacrifice cost so paying it
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the cost-payer. The dies trigger auto-binds on the live
    /// manager's first zone crossing, so no TriggerManager is needed here.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, triggers: null, eventBus: effects?.EventBus, zones: null);

    /// <summary>
    /// Construct Origin Spellbomb with optional TriggerManager wiring. When
    /// <paramref name="triggers"/> is supplied, the dies trigger is registered
    /// so a Battlefield → Graveyard CardMovedEvent places it on the stack
    /// automatically.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers) =>
        Create(owner, triggers, eventBus: null, zones: null);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> so the
    /// cost-payment path publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a). <paramref name="zones"/> (when non-null) routes each Myr
    /// token's ETB through <see cref="ZoneService"/> so CardMovedEvent fires
    /// (Soul Warden etc.). Null preserves the legacy posture.
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this artifact: Create a 1/1 colorless Myr
        // artifact creature token. CR 602 — activated ability. Cost is
        // {1} + tap + self-sacrifice (Battlefield → Graveyard). The
        // sacrifice is performed by the effect closure (AdditionalCost.Pay
        // is a stub — same posture as Nihil / Aether Spellbomb).
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 colorless Myr artifact creature token + sac self",
            () =>
            {
                // Sacrifice payment stub: move spellbomb Battlefield → Graveyard.
                SacrificeSelf(spellbomb, owner);
                CreateMyrToken(spellbomb, owner, zones);
            });

        var tokenAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(spellbomb),
                AdditionalCost.Sacrifice(spellbomb, eventBus),
            },
            effects: new IEffect[] { tokenEffect });

        spellbomb.AddAbility(tokenAbility);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c.
        //   "When this artifact is put into a graveyard from the
        //    battlefield, you may pay {W}. If you do, draw a card."
        //
        // Fires on a Battlefield → Graveyard CardMovedEvent matching this
        // specific card. v1 auto-pays {W} when the controller's mana pool
        // can cover it; draws one card on success. activeZones spans
        // Battlefield + Graveyard so the trigger is still evaluated after
        // ZoneService stamps Zone = Graveyard before publishing (mirrors
        // Nihil Spellbomb's {B} leg / Wurmcoil Engine / Undying pattern).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: may pay {{W}} to draw a card",
            () =>
            {
                // "You may pay {W}. If you do, draw a card."
                // v1 auto-accepts when the pool has the mana.
                var cost = ManaCost.Parse("{W}");
                if (!owner.ManaPool.CanPay(cost)) return;

                owner.PayMana(cost);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var diesTrigger = new TriggeredAbility(
            source: spellbomb,
            controller: owner,
            condition: Triggers.OnDies(spellbomb),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        spellbomb.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return spellbomb;
    }

    /// <summary>
    /// CR 111.1 / CR 111.4 — create one 1/1 colourless Myr artifact creature
    /// token under the source's current controller. <see cref="TokenFactory"/>
    /// mints a <see cref="CardSubtype.Myr"/> creature shell with an explicit
    /// colourless colour set; <see cref="CardType.Artifact"/> is stamped
    /// additively for the artifact-creature multi-type (same multi-type stamp
    /// as <see cref="ServoSchematicFactory"/>'s Servo).
    /// </summary>
    private static void CreateMyrToken(Artifact source, Player owner, ZoneService? zones)
    {
        var controller = source.Controller ?? owner;

        var spec = new TokenFactory.TokenSpec(
            Name: MyrTokenName,
            Power: MyrPower,
            Toughness: MyrToughness,
            Subtypes: new[] { CardSubtype.Myr },
            Keywords: null,
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        token.AddCardType(CardType.Artifact);
    }

    /// <summary>
    /// Move <paramref name="spellbomb"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if the card is already off the battlefield.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
