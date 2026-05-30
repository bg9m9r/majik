using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanctifier en-Vec (Time Spiral, {W}{W}).
///
/// Creature — Human Cleric 2/2. Oracle text (verified against Scryfall):
///   "Protection from black and from red
///    When this creature enters, exile all cards that are black or red
///    from all graveyards.
///    If a black or red permanent, spell, or card not on the battlefield
///    would be put into a graveyard, exile it instead."
///
/// Structurally this is <see cref="RestInPeaceFactory"/> (ETB
/// exile-all-graveyards + static graveyard→exile replacement) FILTERED to
/// black-or-red objects, plus the two protection qualities. The base
/// shape (name, Creature, Human/Cleric, {W}{W}, 2/2) is materialised from
/// the embedded JSON definition (<c>sanctifier-en-vec.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the protection / ETB /
/// replacement behaviours are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express protection keywords,
/// graveyard sweeps, or replacement effects (same posture as
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - 2/2 <see cref="Creature"/> at {W}{W} with Human + Cleric subtypes.
/// - <b>Protection from black and from red (CR 702.16)</b>: two separate
///   <see cref="ProtectionAbility"/> instances (quality "black" and
///   "red"), mirroring <see cref="PhyrexianCrusaderFactory"/>'s "red +
///   white" pair. <see cref="Majik.Core.Rules.Protection"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities"/> / target-legality
///   helpers read each quality independently (DEBT-A).
/// - <b>ETB exile black-or-red from all graveyards (CR 603.6a /
///   CR 701.21)</b>: a <see cref="TriggeredAbility"/> on the creature's
///   own ETB; on resolve walks every player supplied via
///   <paramref name="allPlayersResolver"/> and moves every card whose
///   colours (CR 105.2, via <see cref="CardColors.GetColors"/>) include
///   black or red out of <see cref="ZoneType.Graveyard"/> to
///   <see cref="ZoneType.Exile"/>. Routes through
///   <see cref="ZoneService"/> when supplied so the move publishes
///   <see cref="Majik.Core.Events.CardMovedEvent"/>; null → direct zone
///   mutation. Same plumbing as Rest in Peace's sweep, gated on colour.
/// - <b>Static replacement (CR 614)</b>: while Sanctifier en-Vec is on
///   the battlefield, any <see cref="ZoneMoveIntent"/> headed to
///   <see cref="ZoneType.Graveyard"/> whose moving card is black or red
///   is rewritten to <see cref="ZoneType.Exile"/>. The oracle's "black or
///   red permanent, spell, or card not on the battlefield" enumerates
///   every object that could reach a graveyard; v1 collapses this to "the
///   moving object is black or red" since the destination-is-graveyard
///   gate already excludes everything that stays on the battlefield. Not
///   EOT-expirable (CR 614.6); gates internally on the creature being on
///   the battlefield so blink / bounce / destroy stop the rewrite
///   immediately.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-ordering prompt</b> (CR 616.1): the bus applies in
///   registration order — overlapping with Rest in Peace / Leyline of the
///   Void / another graveyard replacement picks the registration order
///   today. Affected-player choice deferred (same gap as every other
///   replacement factory).
/// </summary>
[CardName("Sanctifier en-Vec")]
public static class SanctifierEnVecFactory
{
    public const string CardName = "Sanctifier en-Vec";
    public const string Slug = "sanctifier-en-vec";

    /// <summary>
    /// Construct Sanctifier en-Vec with card identity + protection only
    /// (no ETB or replacement wiring). The ETB trigger is attached for
    /// shape inspection but not registered with a
    /// <see cref="TriggerManager"/>; the static replacement is omitted.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, allPlayersResolver: null, replacements: null, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Sanctifier en-Vec.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list at ETB
    /// resolution. v1 walks every player's graveyard. Null → the ETB exile
    /// half no-ops.</param>
    /// <param name="replacements">Bus on which the static black-or-red
    /// graveyard→exile replacement is registered. Null → static half is
    /// skipped.</param>
    /// <param name="zoneService">Used to route the ETB graveyard-to-exile
    /// moves so <see cref="Majik.Core.Events.CardMovedEvent"/> publishes.
    /// Null → direct zone mutation.</param>
    /// <param name="triggers">When supplied, the ETB triggered ability is
    /// registered so it lands on the stack on battlefield arrival.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ReplacementBus? replacements,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Cleric, {W}{W}, 2/2). The JSON carries no abilities —
        // protection / ETB / replacement are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.16 — Protection from black and from red. Two separate
        // ProtectionAbility instances (one quality each), same shape as
        // Phyrexian Crusader's "red + white" pair. Rules.Protection reads
        // each quality independently.
        card.AddAbility(new ProtectionAbility("black"));
        card.AddAbility(new ProtectionAbility("red"));

        // ----------------------------------------------------------------
        // Static replacement — register up-front. The replacement's
        // Applies check gates on Card.Zone == Battlefield, so it's inert
        // until the creature lands. CR 614.6 — static effects from
        // on-battlefield permanents are not EOT-expirable.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(new SanctifierEnVecGraveyardRewrite(card));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a. "When this creature enters,
        // exile all cards that are black or red from all graveyards."
        // Walks every player's graveyard at resolve time, colour-gated.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName} — exile all black or red cards from all graveyards (CR 701.21)",
            () => ResolveEtbExile(allPlayersResolver, zoneService));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 105.2 — a card is black or red iff its colours (mana-cost pips +
    /// color indicator, via <see cref="CardColors.GetColors"/>) include
    /// <see cref="ManaColor.Black"/> or <see cref="ManaColor.Red"/>.
    /// </summary>
    internal static bool IsBlackOrRed(ICard card)
    {
        if (card == null) return false;
        var colors = CardColors.GetColors(card);
        return colors.Contains(ManaColor.Black) || colors.Contains(ManaColor.Red);
    }

    /// <summary>
    /// Resolve helper: exile every black-or-red card sitting in every
    /// supplied player's graveyard. Exposed so tests can invoke directly
    /// without driving the full TriggerManager loop. CR 608.2b — an empty
    /// (or all-other-colour) graveyard is a clean no-op.
    /// </summary>
    public static void ResolveEtbExile(
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ZoneService? zoneService)
    {
        var players = allPlayersResolver?.Invoke();
        if (players == null) return;

        foreach (var player in players)
        {
            if (player == null) continue;
            // Snapshot to a list — moving cards mutates the underlying
            // collection while we iterate.
            var graveyardCards = player.Zones.Graveyard.GetCards().ToList();
            foreach (var graveCard in graveyardCards)
            {
                if (!IsBlackOrRed(graveCard)) continue;

                if (zoneService != null)
                {
                    zoneService.MoveCard(graveCard, ZoneType.Graveyard, ZoneType.Exile);
                }
                else
                {
                    player.Zones.Graveyard.RemoveCard(graveCard);
                    player.Zones.Exile.AddCard(graveCard);
                    graveCard.SetZone(ZoneType.Exile);
                }
            }
        }
    }
}

/// <summary>
/// CR 614 replacement effect: while Sanctifier en-Vec is on the
/// battlefield, every <see cref="ZoneMoveIntent"/> headed to
/// <see cref="ZoneType.Graveyard"/> whose moving object is black or red
/// is rewritten to <see cref="ZoneType.Exile"/>. Not EOT-expirable — the
/// static stays live as long as the creature is on the battlefield
/// (CR 614.6).
/// </summary>
public sealed class SanctifierEnVecGraveyardRewrite : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Creature _source;

    public SanctifierEnVecGraveyardRewrite(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;
        return SanctifierEnVecFactory.IsBlackOrRed(intent.Card);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
