using Majik.Core.Abilities;
using Majik.Core.CardData.Adventures;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brazen Borrower // Petty Theft (Throne of
/// Eldraine, {1}{U}{U}).
///
/// ## Card text (verified against Scryfall 2026-06-02)
/// - Brazen Borrower — Creature — Faerie Rogue {1}{U}{U}, 3/1.
///     "Flash
///      Flying
///      This creature can block only creatures with flying."
/// - Petty Theft (Adventure) — Instant — Adventure {1}{U}.
///     "Return target nonland permanent an opponent controls to its owner's
///      hand."
///     (Then exile this card. You may cast the creature later from exile.)
///
/// Brazen Borrower is the Adventure sibling of <see cref="BonecrusherGiantFactory"/>
/// (creature // <i>instant</i> Adventure) — same framing: an instant-speed
/// flash flyer creature half plus a single-target instant Adventure half.
/// The bounce half (Petty Theft) is the opponent-restricted, nonland-only
/// cousin of <see cref="BoomerangFactory"/> ("return target permanent to its
/// owner's hand") and <see cref="EchoingTruthFactory"/> ("return target
/// nonland permanent ...").
///
/// ## Implemented (v1)
/// - <b>Creature shape</b> (name / Creature / Faerie Rogue / {1}{U}{U} / 3/1)
///   materialised from the embedded JSON definition
///   (<c>brazen-borrower-petty-theft.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="GlintNestCraneFactory"/> / <see cref="EchoingTruthFactory"/>
///   (the JSON ability schema does not express keyword markers, the Adventure
///   half, or the bounce, so those are layered on here).
/// - <b>Flash (CR 702.8)</b> + <b>Flying (CR 702.9)</b> attached as
///   <see cref="KeywordAbility"/> markers — same wiring as the Faerie flyer
///   <see cref="SpellstutterSpriteFactory"/>.
/// - <b>Petty Theft Adventure half (CR 715)</b>: attached as an
///   <see cref="AdventureSpec"/>. The cast flow
///   (<see cref="Costs.AdventureAlternativeCost"/> + <see cref="SpellCastFlow"/>)
///   routes Petty Theft through the standard Rule 601 sequence at the
///   Adventure mana cost ({1}{U}, instant-speed — Petty Theft is an Instant),
///   exiles the card on resolve (CR 715.3d), and grants the owner a runtime
///   "may cast the creature later from exile" permission for the printed
///   Brazen Borrower cost via <see cref="Card.GrantRuntimeExileCast"/> — the
///   same exile-cast probe Bonecrusher Giant / Murderous Rider reuse.
/// - <b>Petty Theft resolve</b> (<see cref="BuildAdventureSpell"/>): a single
///   1..1 "target nonland permanent an opponent controls" target request whose
///   resolve effect returns the chosen permanent to its owner's hand
///   (CR 701.10), re-checking legality at resolution (CR 608.2b). The
///   CandidateGatherer restricts to permanents controlled by a player OTHER
///   than the caster (CR 109.5 — "an opponent" = not you) and excludes Land
///   (CR 305 — Land is a card type).
///
/// ## Deferred (v1 gaps)
/// - <b>"This creature can block only creatures with flying"</b>: the engine
///   has no combat-block-restriction primitive for "can only block X" yet —
///   <see cref="Combat.CombatValidator.CanBlock"/> enforces the reverse
///   (a flyer can only be blocked by flyers/reach) but not this "this blocker
///   may only block flyers" rider. Documented as a known gap, identical to
///   the Pinnacle Emissary Drone token's same printed clause (see
///   <see cref="PinnacleEmissaryFactory"/>). Flying is stamped as a keyword
///   marker; the restriction picks up for free once the "can only block X"
///   primitive lands.
/// </summary>
[CardName("Brazen Borrower")]
public static class BrazenBorrowerFactory
{
    public const string CardName = "Brazen Borrower";
    public const string Slug = "brazen-borrower-petty-theft";
    public const string PrintedManaCost = "{1}{U}{U}";

    public const string AdventureName = "Petty Theft";
    public const string AdventureManaCost = "{1}{U}";

    private const string FlashKeyword = "Flash";
    private const string FlyingKeyword = "Flying";

    /// <summary>
    /// Construct Brazen Borrower. The creature shape is materialised from the
    /// embedded JSON definition; Flash + Flying keyword markers and the Petty
    /// Theft <see cref="AdventureSpec"/> are layered on here. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.8 — Flash. Allows casting Brazen Borrower at instant speed.
        card.AddAbility(new KeywordAbility(FlashKeyword, card, owner));

        // CR 702.9 — Flying. Combat blocking restriction (can only be blocked
        // by flyers / reach).
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // CR 715 — attach the Petty Theft Adventure half for the cast
        // pipeline. The AdventureSpec carries the alternative characteristics
        // ({1}{U}, Instant) + an effects-factory closure; the cast path is
        // driven by AdventureAlternativeCost + SpellCastFlow.
        card.AdventureSpec = new AdventureSpec(
            Name: AdventureName,
            ManaCost: ManaCost.Parse(AdventureManaCost),
            AdventureType: CardType.Instant,
            BuildDefinition: BuildAdventureSpell);

        return card;
    }

    /// <summary>
    /// Build the standalone Petty Theft <see cref="SpellDefinition"/> — a
    /// single 1..1 "target nonland permanent an opponent controls" target
    /// request whose resolve effect returns the chosen permanent to its
    /// owner's hand (CR 701.10).
    /// </summary>
    /// <param name="caster">The controller of Petty Theft. Used to scope the
    /// candidate pool to permanents an <i>opponent</i> controls (CR 109.5 —
    /// "an opponent" = a player other than you).</param>
    /// <param name="targetResolver">Resolves the raw target token to a live
    /// engine object (typically a <see cref="Permanent"/>).</param>
    public static SpellDefinition BuildAdventureSpell(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target nonland permanent an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Agent-prompt MVP: gather nonland permanents (CR 305 —
                    // Land is a card type) controlled by a player OTHER than
                    // the caster (CR 109.5 — "an opponent" excludes you).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, caster))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(c => !c.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Petty Theft: return target nonland permanent an opponent controls to its owner's hand",
                        () => Resolve(resolved, caster)),
                };
            });
    }

    /// <summary>
    /// CR 608.2b resolution-time legality re-check + CR 701.10 bounce. The
    /// chosen target must still be a nonland <see cref="Permanent"/> on the
    /// battlefield controlled by an opponent of the caster, else the spell
    /// does nothing.
    /// </summary>
    private static void Resolve(object resolved, Player caster)
    {
        if (resolved is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.HasType(CardType.Land)) return;

        var controller = target.Controller;
        // CR 109.5 — must be controlled by an opponent at resolution.
        if (controller == null || ReferenceEquals(controller, caster)) return;

        var owner = target.Owner;
        if (owner == null) return;

        // CR 701.10 — return to owner's hand.
        controller.Zones.Battlefield.RemoveCard(target);
        owner.Zones.Hand.AddCard(target);
        target.SetZone(ZoneType.Hand);
        target.SetController(owner);
    }
}
