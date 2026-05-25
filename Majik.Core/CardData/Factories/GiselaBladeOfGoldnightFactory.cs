using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gisela, Blade of Goldnight (Avacyn Restored,
/// {4}{R}{W}{W}).
///
/// Legendary Creature — Angel {4}{R}{W}{W} 5/5. Oracle text:
///   "Flying, first strike, lifelink.
///    If a source would deal damage to an opponent or a permanent an
///    opponent controls, that source deals double that damage to that
///    player or permanent instead.
///    If a source would deal damage to you or a permanent you control,
///    prevent half that damage, rounded up."
///
/// ## Implementation
///
/// - Card identity (Legendary Creature — Angel 5/5, mana cost
///   {4}{R}{W}{W}, owner / controller wiring).
/// - <b>Evergreen keywords</b> (CR 702.9 Flying, CR 702.7 First Strike,
///   CR 702.15 Lifelink) wired as <see cref="KeywordAbility"/> markers —
///   the combat helpers in <see cref="Majik.Core.Combat.CombatAbilities"/>
///   read these directly (same wiring as Atraxa, Grand Unifier).
/// - <b>Asymmetric damage doubling</b> (CR 614) — single
///   <see cref="DamageDoubleReplacement"/> registration on the supplied
///   <see cref="ReplacementBus"/>, gated identically to Angrath's
///   Marauders: source uncontrolled, target = opponent or opponent's
///   permanent. (Gisela's printed text reads "If a source would deal
///   damage to an opponent..." with no "you control" restriction on the
///   source — i.e. every damage intent aimed at an opponent doubles,
///   regardless of who controls the source. This factory matches that
///   reading by omitting the source-controller gate.)
/// - <b>Asymmetric damage halving</b> (CR 615) — single
///   <see cref="DamageHalveRoundedUpReplacement"/> registration on the
///   supplied bus, gated on target = controller or controller's
///   permanent. Halving applies before doubling on the bus (registration
///   order); CR 616 player-choice ordering is deferred (same scope as
///   the rest of the replacement family).
/// - The doubling + halving clauses each register one
///   <see cref="IReplacementEffect{DamageIntent}"/>. Both replacements
///   gate on Gisela being on the battlefield, so blink / bounce
///   automatically suspends both clauses without explicit
///   deregistration.
/// - Gisela's controller is read live from <see cref="Card.Controller"/>
///   rather than captured at construction, so control-change effects
///   (Mind Control, Threaten) repoint both clauses as soon as the
///   controller flips.
///
/// ## Notes
/// - Two-overload shape mirrors Inquisitor's Flail / Furnace of Rath /
///   Angrath's Marauders: single-arg <see cref="Create(Player)"/> is
///   shape-only for dispatcher tests (no bus → no replacement
///   registration); the <see cref="Create(Player, ReplacementBus?)"/>
///   overload wires both live clauses when a bus is supplied.
/// - The doubling clause reads "an opponent or a permanent an opponent
///   controls" (no source-controller restriction on Gisela's printed
///   text — distinct from Angrath's Marauders which requires "a source
///   you control"). Both factories share the
///   <see cref="AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent"/>
///   target predicate.
/// </summary>
[CardName("Gisela, Blade of Goldnight")]
public static class GiselaBladeOfGoldnightFactory
{
    public const string CardName = "Gisela, Blade of Goldnight";
    public const string Cost = "{4}{R}{W}{W}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Gisela, Blade of Goldnight with card identity +
    /// evergreen keywords only — no damage-doubling or damage-halving
    /// replacements are registered. Suitable for shape / dispatcher
    /// tests; bus-driven clauses live on the
    /// <see cref="Create(Player, ReplacementBus?)"/> overload.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Gisela, Blade of Goldnight. When
    /// <paramref name="replacements"/> is supplied, both printed clauses
    /// (double damage to opponents + halve damage to controller) are
    /// registered against the bus, gated on Gisela being on the
    /// battlefield.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evergreen keywords — CR 702.9 Flying, CR 702.7 First Strike,
        // CR 702.15 Lifelink. KeywordAbility markers read directly by
        // the combat helpers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("First Strike", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        if (replacements != null)
        {
            // --------------------------------------------------------------
            // Doubling: "If a source would deal damage to an opponent or a
            // permanent an opponent controls, that source deals double that
            // damage to that player or permanent instead."
            // --------------------------------------------------------------
            replacements.Register<DamageIntent>(new DamageDoubleReplacement(
                intent =>
                    card.Zone == ZoneType.Battlefield
                    && AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent(intent, card.Controller)));

            // --------------------------------------------------------------
            // Halving: "If a source would deal damage to you or a permanent
            // you control, prevent half that damage, rounded up." —
            // rewrites Amount to ceil(N/2).
            // --------------------------------------------------------------
            replacements.Register<DamageIntent>(new DamageHalveRoundedUpReplacement(
                intent =>
                    card.Zone == ZoneType.Battlefield
                    && TargetIsControllerOrTheirPermanent(intent, card.Controller)));
        }

        return card;
    }

    /// <summary>
    /// "You or a permanent you control" — true when the intent's target
    /// is <paramref name="controller"/> directly OR a Creature /
    /// Planeswalker whose <see cref="Card.Controller"/> is
    /// <paramref name="controller"/>. Sibling of
    /// <see cref="AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent"/>.
    /// </summary>
    internal static bool TargetIsControllerOrTheirPermanent(DamageIntent intent, Player? controller)
    {
        if (controller is null) return false;

        if (intent.TargetPlayer is { } p)
            return ReferenceEquals(p, controller);

        if (intent.TargetCreature is { } c)
            return ReferenceEquals(c.Controller, controller);

        if (intent.TargetPlaneswalker is { } pw)
            return ReferenceEquals(pw.Controller, controller);

        return false;
    }
}
