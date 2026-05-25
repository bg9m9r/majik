using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Griselbrand (Avacyn Restored, {4}{B}{B}{B}).
///
/// Legendary Creature — Demon 7/7. Oracle text (Scryfall, verified):
///   "Flying
///    Lifelink
///    Pay 7 life: Draw seven cards."
///
/// ## Implemented (v1)
/// - 7/7 Legendary Creature — Demon at {4}{B}{B}{B}.
/// - <b>Flying (CR 702.9)</b> + <b>Lifelink (CR 702.15)</b>: shipped as
///   <see cref="KeywordAbility"/> markers. Combat-side reads via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>; the lifelink
///   marker drives <see cref="Majik.Core.Services.OracleSpellBinder.DealDamage"/>'s
///   "controller gains that much life" tail.
/// - <b>Activated: "Pay 7 life: Draw seven cards." (CR 605 — regular
///   activated ability, not a mana ability)</b>:
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="PayLifeCost"/>(<see cref="LifeCost"/>) cost. On
///   resolution the controller draws
///   <see cref="CardsDrawn"/> via <see cref="Fx.DrawCards"/>.
///
///   Activation is instant-speed (default — <see cref="ActivatedAbility.IsSorcerySpeed"/>
///   is false). Printed text has no sorcery-speed rider; the life cost
///   is the only restriction beyond timing-by-priority (CR 602.1a). The
///   <see cref="PayLifeCost.CanPay"/> gate enforces CR 119.4 ("you can't
///   pay life you don't have") — Griselbrand at 6 life or less can't
///   activate. The Fx.DrawCards primitive routes through the engine's
///   draw pipeline so any draw-replacement effects (Spirit of the
///   Labyrinth's intent shape when wired, Alms Collector, Notion Thief)
///   intercept normally and an empty-library mid-draw flags the
///   controller for the loss-via-draw SBA (CR 704.5b).
///
/// ## Edge-case notes
/// - <b>Source on battlefield</b>: activated abilities of permanents
///   require the source to be on the battlefield (CR 602.5a). The
///   engine's <see cref="Majik.Core.Rules.ActionValidator"/> /
///   <see cref="Majik.Core.Services.AbilityActivator"/> path gates on
///   <c>card.Zone == Battlefield</c> at activation request time. No
///   factory-side zone gate needed — the ability has no source-zone
///   override.
/// - <b>Life payment + lifelink interaction</b>: paying 7 life routes
///   through <see cref="Player.LoseLife"/> so any life-loss triggers
///   (Sanguine Bond, Vizkopa Guildmage) fire normally. The
///   payment is part of cost payment (CR 601.2h) — it precedes putting
///   the ability on the stack and is unaffected by Griselbrand's own
///   Lifelink (Lifelink only triggers off damage, not paid-life costs).
/// - <b>Death from over-draw</b>: drawing the 7 cards is a single
///   resolution step; if the controller has fewer than 7 cards in
///   library each draw individually flags
///   <see cref="Player.TriedToDrawFromEmptyLibrary"/> on overflow, and
///   the loss-by-empty-library SBA fires after the resolution
///   completes (CR 704.5b). Griselbrand still resolves the full
///   "draw seven" effect — the SBA is checked on the next state-based
///   action pass.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — the only overload. The activated
///   ability is fully self-contained (no event bus / trigger manager
///   dependencies), so a single overload suffices.
/// </summary>
[CardName("Griselbrand")]
public static class GriselbrandFactory
{
    public const string CardName = "Griselbrand";
    public const string PrintedManaCost = "{4}{B}{B}{B}";
    public const int Power = 7;
    public const int Toughness = 7;

    /// <summary>Life cost of the activated ability — "Pay 7 life".</summary>
    public const int LifeCost = 7;

    /// <summary>Cards drawn by the activated ability — "Draw seven cards".</summary>
    public const int CardsDrawn = 7;

    /// <summary>
    /// Construct Griselbrand. No event bus / trigger manager required —
    /// the activated ability is self-contained.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Demon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Combat-side reads via
        // CombatAbilities.HasFlying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.15 — Lifelink marker. Damage dealt by Griselbrand also
        // causes its controller to gain that much life — OracleSpellBinder
        // / CombatFlow consult the marker on damage resolution.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // Activated — "Pay 7 life: Draw seven cards."
        //
        // CR 605 — not a mana ability (the effect doesn't produce mana
        // and isn't on the mana-ability fast path). CR 602.1a — instant
        // speed by default (no sorcery rider on the printed card).
        //
        // The cost is a single PayLifeCost(7); PayLifeCost.CanPay gates
        // on the controller having ≥ 7 life (CR 119.4). On resolution
        // Fx.DrawCards(owner, 7) routes through the engine's draw
        // pipeline so any draw-replacement effects intercept normally
        // and library-empty handling flags the loss-by-empty-library
        // SBA per draw.
        // ----------------------------------------------------------------
        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new PayLifeCost(LifeCost) },
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: controller draws {CardsDrawn} cards",
                    () =>
                    {
                        // Read live controller off the card — control
                        // could have changed between activation and
                        // resolution (Threads of Disloyalty et al.).
                        var resolvingController = card.Controller ?? owner;
                        Fx.DrawCards(resolvingController, CardsDrawn);
                    }),
            });

        card.AddAbility(activated);

        return card;
    }
}
