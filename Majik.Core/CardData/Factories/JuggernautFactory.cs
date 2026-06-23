using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Juggernaut (Antiquities, {4}).
///
/// Artifact Creature — Juggernaut 5/3. Oracle text (verified against
/// Scryfall 2026-06-23):
///   "This creature attacks each combat if able.
///    This creature can't be blocked by Walls."
///
/// The base shape (name, Artifact + Creature types, Juggernaut subtype,
/// {4}, 5/3) is materialised from the embedded JSON definition
/// (<c>juggernaut.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two combat behaviours are
/// layered on top here.
///
/// ## Implemented (v1)
///
/// - <b>5/3 Artifact Creature — Juggernaut</b> at {4}.
/// - <b>"Attacks each combat if able" (CR 508.1a / 702.43 — the must-attack
///   combat restriction)</b>: shipped as a <see cref="KeywordAbility"/>
///   ("AttacksEachCombat") marker, ENFORCED by
///   <see cref="Majik.Core.Combat.CombatFlow"/> — an eligible creature
///   carrying this marker is force-declared into combat at declare-attackers
///   (CR 508.1a "if able") even when its controller's agent omits it. Same
///   posture as <see cref="UlamogsCrusherFactory"/>.
/// - <b>"Can't be blocked by Walls" (CR 509.1b — block restriction)</b>:
///   modelled as the complementary "can't be blocked except by non-Walls"
///   via a <see cref="CantBeBlockedExceptByEffect"/> registered on the
///   supplied <see cref="ContinuousEffectsService"/>. The predicate accepts a
///   would-be blocker iff it does NOT have the <see cref="CardSubtype.Wall"/>
///   subtype; <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> walks the
///   attacker's <see cref="Creature.ActiveEffects"/> and rejects any Wall
///   blocker. The subtype is read at block-declaration time so a creature that
///   has lost/gained the Wall type through the layer system (CR 613) is judged
///   on its CURRENT subtypes. Same posture as
///   <see cref="SteelLeafChampionFactory"/>.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The block restriction is NOT
///   registered (no effects service); the must-attack marker is always
///   attached. Suitable for dispatcher / identity tests; the contract test
///   exercises this single-arg path.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — fully wired
///   block restriction (also binding the service onto
///   <see cref="Creature.ActiveEffects"/> so the combat validator picks the
///   restriction up).
/// </summary>
[CardName("Juggernaut")]
public static class JuggernautFactory
{
    public const string CardName = "Juggernaut";
    public const string Slug = "juggernaut";

    /// <summary>
    /// Construct Juggernaut with no live wiring. The "can't be blocked by
    /// Walls" restriction is NOT registered (no effects service); the
    /// must-attack marker is attached. Suitable for dispatcher / shape tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Juggernaut with optional runtime services. Registers the
    /// "can't be blocked by Walls" restriction on <paramref name="effects"/>
    /// when supplied (also binding it onto <see cref="Creature.ActiveEffects"/>
    /// so the combat validator picks the restriction up). The must-attack
    /// marker is attached on every path.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact +
        // Creature, Juggernaut subtype, {4}, 5/3). The JSON carries no
        // abilities — the two combat behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 508.1a / 702.43 — "attacks each combat if able". The marker is
        // ENFORCED by CombatFlow: this creature is force-declared as an
        // attacker at declare-attackers whenever it can legally attack.
        card.AddAbility(new KeywordAbility(
            "AttacksEachCombat", card, owner));

        if (effects != null)
        {
            // Bind the service so BlockLegality reads (the can't-be-blocked-
            // except-by walk) flow through the same layer pipeline.
            card.ActiveEffects = effects;

            // CR 509.1b — "This creature can't be blocked by Walls." Modelled
            // as the complementary "can't be blocked except by non-Walls": the
            // predicate accepts a blocker iff it is a creature WITHOUT the Wall
            // subtype. The subtype is read continuously, so a creature that
            // gains/loses the Wall type via the layer system (CR 613) is judged
            // on its CURRENT subtypes at block-declaration time.
            effects.Register(new CantBeBlockedExceptByEffect(
                source: card,
                predicate: blocker => blocker is Creature c
                    && !c.HasSubtype(CardSubtype.Wall)));
        }

        return card;
    }
}
