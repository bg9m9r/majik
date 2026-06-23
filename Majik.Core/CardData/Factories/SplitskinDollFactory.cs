using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Splitskin Doll (Duskmourn: House of Horror, {1}{W}).
///
/// Artifact Creature — Toy 2/1. Oracle text (verified against Scryfall):
///   "When this creature enters, draw a card. Then discard a card unless you
///    control another creature with power 2 or less."
///
/// ## Shape (JSON-built)
/// The card SHELL — dual Artifact + Creature type (so artifact-matters
/// consumers see it, CR 301.1 / 302.1), the Toy subtype (CR 205.3m), 2/1, and
/// the {1}{W} cost — is materialised from
/// <c>Majik.Core/CardData/Cards/splitskin-doll.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/>, exactly
/// like <see cref="AlloyMyrFactory"/>'s dual-type body.
///
/// ## ETB triggered ability (bespoke — CR 603.1 / 603.6a)
/// The "draw, then conditionally discard" body has no declarative DSL verb (no
/// discard effect, and the discard is gated on a BOARD-STATE predicate rather
/// than a player choice or an intervening-if on the whole ability — the draw
/// must ALWAYS happen). It is therefore hand-rolled here, on the same
/// <see cref="Triggers.OnEnterBattlefieldSelf"/> + inline-<see cref="Effect"/>
/// shape as <see cref="CharmingPrinceFactory"/>:
/// <list type="number">
///   <item>CR 121.1 — the controller draws one card (always), routed through
///   <see cref="Fx.DrawCards(Player, int)"/> so draw-replacements (Dredge etc.)
///   and the empty-library SBA flag (CR 104.3c / 704.5b) apply.</item>
///   <item>CR 701.8 — "unless you control another creature with power 2 or
///   less": the discard is SKIPPED when the controller controls at least one
///   OTHER creature (CR 109.5 — "another" excludes the Doll itself, by
///   reference) whose current power (CR 613 layers, via
///   <see cref="Creature.Power"/>) is &lt;= 2. Otherwise one card is discarded
///   via <see cref="Fx.Discard(Player, int)"/> (v1 deterministic
///   first-in-hand pick — the agent-driven choice gap shared with Faithless
///   Looting / Liliana).</item>
/// </list>
/// The board-state predicate is evaluated at RESOLUTION (not when the trigger
/// is put on the stack), reading the live battlefield off the resolving
/// controller — so a creature that left, or one whose power dropped, between
/// trigger and resolution is judged on the resolution-time state (CR 603.1).
///
/// Supplying a <see cref="TriggerManager"/> registers the ETB on the bus so a
/// live enters-the-battlefield event queues it; without one the ability is
/// attached for shape / dispatch inspection only (same posture as
/// <see cref="SoulWardenFactory"/> / <see cref="CharmingPrinceFactory"/>).
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Splitskin Doll")]
public static class SplitskinDollFactory
{
    public const string CardName = "Splitskin Doll";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "splitskin-doll";

    /// <summary>"another creature with power 2 or less" — the power cap that
    /// turns OFF the discard (CR 701.8).</summary>
    public const int SmallCreaturePowerCap = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Splitskin Doll with no live trigger registration —
    /// the ETB is materialised for shape / dispatch inspection only.</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Splitskin Doll, registering the ETB triggered ability with
    /// <paramref name="triggers"/> when supplied so a qualifying
    /// enters-the-battlefield event automatically queues it.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / 603.6a):
        //   "When this creature enters, draw a card. Then discard a card
        //    unless you control another creature with power 2 or less."
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: draw a card, then discard unless you control another "
            + "creature with power 2 or less",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 121.1 — always draw one card first.
                Fx.DrawCards(controller, 1);

                // CR 701.8 — "unless you control another creature with power 2
                // or less". "another" (CR 109.5) excludes the Doll itself; the
                // power check reads the live (CR 613) power. If satisfied, the
                // discard is skipped entirely.
                var controlsAnotherSmallCreature = controller.Zones.Battlefield
                    .GetCards()
                    .OfType<Creature>()
                    .Any(c => !ReferenceEquals(c, card)
                              && c.Power <= SmallCreaturePowerCap);

                if (!controlsAnotherSmallCreature)
                {
                    Fx.Discard(controller, 1);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
