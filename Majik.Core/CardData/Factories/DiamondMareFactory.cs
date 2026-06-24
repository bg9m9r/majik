using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Diamond Mare (Core Set 2019, {2}).
///
/// Artifact Creature — Horse 1/3. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "As this creature enters, choose a color.
///    Whenever you cast a spell of the chosen color, you gain 1 life."
///
/// ## Implementation
///
/// Card identity (Artifact Creature — Horse, {2}, 1/3, colourless) is loaded
/// from <c>Majik.Core/CardData/Cards/diamond-mare.json</c> through
/// <see cref="CardDefinitionFactory"/> (the JSON-driven posture shared with
/// the artifact mana rocks — Coldsteel Heart et al.).
///
/// ## Choose a color (CR 614.12 — "as this enters" replacement)
///
/// "As this creature enters, choose a color." (CR 614.12) selects the colour
/// the cast trigger keys off. The choice isn't known when the card is built, so
/// the factory stashes a shared <see cref="ColorChoice"/> holder in
/// <see cref="ColorChoiceRegistry"/> (seeded White) and the cast trigger reads
/// it LIVE at trigger time. On the routed production build the overlay
/// <see cref="ChooseColorPermanentBinder"/> finds that holder and registers an
/// agent-prompting <see cref="Majik.Core.Effects.ChooseColorReplacement"/> that
/// stamps the controller's pick onto the holder as the creature enters — the
/// same machinery Coldsteel Heart / Utopia Sprawl use. Until the choice
/// resolves the holder sits at its seeded default (White).
///
/// ## Cast trigger (CR 603.1 — "whenever you cast a spell of the chosen color")
///
/// A <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>, gated to
///   (a) "you cast" — the spell's controller is this card's controller
///       (CR 603.1; same predicate as <see cref="SramSeniorEdificerFactory"/>),
///   (b) "of the chosen color" — the spell's colour set
///       (<see cref="CardColors.GetColors"/>) contains the live
///       <see cref="ColorChoice.Chosen"/> colour (CR 105 / 202.2).
/// On resolution the controller gains 1 life (CR 119.3 — life-gain effect).
///
/// ## Routed production wiring
///
/// Diamond Mare is a non-land factory, so it is routed through this factory in
/// production (<see cref="FactoryRouting"/>). The routed instance-swap build
/// dispatches only the single-arg <see cref="Create(Player)"/> overload and
/// runs no triggered-ability binder, so that overload both attaches the cast
/// trigger AND registers it with the live per-game
/// <see cref="TriggerManager"/> resolved from <see cref="TriggerManagerRegistry"/>
/// (the ambient per-game manager installed at game start). The trigger only
/// matches while Diamond Mare is on the battlefield
/// (<see cref="TriggeredAbility"/>'s <c>ActiveZones</c> gate, CR 603.1), so
/// registering at deck-build time is harmless until the creature resolves.
/// </summary>
[CardName("Diamond Mare")]
public static class DiamondMareFactory
{
    public const string CardName = "Diamond Mare";
    public const string Slug = "diamond-mare";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "As this creature enters, choose a color.\n" +
        "Whenever you cast a spell of the chosen color, you gain 1 life.";

    public const int LifeGain = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Single-arg dispatch — the overload the routed production build
    /// (<see cref="FactoryRouting"/>) invokes. Seeds the choose-a-color holder
    /// to White (the overlay <see cref="ChooseColorPermanentBinder"/> stamps the
    /// agent's real "as this enters" pick onto it, CR 614.12) and registers the
    /// cast trigger with the live per-game <see cref="TriggerManager"/> resolved
    /// from <see cref="TriggerManagerRegistry"/>. Outside any game scope the
    /// registry yields <c>null</c> and the trigger is merely attached.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, ManaColor.White, TriggerManagerRegistry.Get());

    /// <summary>
    /// Construct a fully-wired Diamond Mare. The cast trigger is attached to the
    /// card's <see cref="Card.Abilities"/> collection; when
    /// <paramref name="triggers"/> is supplied it is also registered with the
    /// <see cref="TriggerManager"/> so it surfaces as pending end-to-end. The
    /// keyed colour is read live from the card's <see cref="ColorChoice"/> holder;
    /// <paramref name="chosenColor"/> seeds that holder (the prod path later
    /// overwrites the seed with the agent's "as this enters" pick).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The seed color for the choice "as this creature
    /// enters" (CR 614.12). Must be one of W/U/B/R/G.</param>
    /// <param name="triggers">Optional live trigger manager for end-to-end
    /// firing.</param>
    public static Creature Create(Player owner, ManaColor chosenColor, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 614.12 — one shared per-card choice holder seeded to the supplied
        // colour. The cast trigger reads it LIVE, and the overlay
        // ChooseColorReplacement stamps the agent's pick onto it as the creature
        // enters. Stashed so ChooseColorPermanentBinder can find it.
        var choice = new ColorChoice(chosenColor);
        ColorChoiceRegistry.Set(card, choice);

        // ----------------------------------------------------------------
        // "Whenever you cast a spell of the chosen color, you gain 1 life."
        // CR 603.1 — on-cast trigger over SpellCastEvent.
        //   (a) "you cast"      → spell's controller == this card's controller.
        //   (b) "of the chosen  → spell's colour set contains the live chosen
        //        color"            colour (CR 105 / 202.2).
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 603.1 — controller match for the printed "you cast". Compare to
            // the card's current controller at evaluation time (owner is a safe
            // fallback because Diamond Mare cannot change controller in any
            // printed effect).
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController)) return false;

            // CR 105 / 202.2 — "of the chosen color": the cast spell's colour set
            // must include the live chosen colour. Read LIVE (CR 614.12) — the
            // agent's ETB pick may have overwritten the seed.
            var spellColors = CardColors.GetColors(e.Spell.Card);
            return spellColors.Contains(choice.Chosen);
        });

        var gainLifeEffect = new Effect(
            $"{CardName}: gain {LifeGain} life (whenever you cast a spell of the chosen color)",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGain);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { gainLifeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
