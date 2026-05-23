using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spellskite (New Phyrexia, {2}).
///
/// Artifact Creature — Horror 0/4. Oracle text:
///   "{U/P}: Change the target of target spell or ability with a single
///    target to Spellskite.
///    ({U/P} can be paid with either {U} or 2 life.)"
///
/// CR 107.4f / 118.8 — Phyrexian mana symbols may be paid with one mana
/// of the listed colour OR 2 life. CR 114.6 — changing targets.
///
/// ## Implemented (v1)
///
/// Two parallel <see cref="ActivatedAbility"/> instances are wired so the
/// engine's existing cost primitives cover both legal payments without
/// inventing a new "hybrid mana / life" cost shape on activated abilities
/// (the existing <see cref="Majik.Core.Costs.PhyrexianManaAlternativeCost"/>
/// only models the spell-level printed-cost case — Surgical Extraction
/// shape — and isn't reachable from an <see cref="ActivatedAbility"/>'s
/// cost list).
///
/// - <b>Pay {U}</b>: <see cref="ManaCostCost"/>("{U}") + the single
///   <see cref="TargetRequest"/> for the spell/ability to redirect.
/// - <b>Pay 2 life</b>: <see cref="AdditionalCost.PayLife"/>(2) + an
///   identical <see cref="TargetRequest"/>.
///
/// Both abilities share the same redirect closure: when the chosen target
/// is a <see cref="Spell"/> with exactly one entry in
/// <see cref="Spell.ChosenTargets"/>, rewrite that entry to the Spellskite
/// itself. Multi-target spells (ChosenTargets.Count != 1) are rejected at
/// resolution as a no-op (CR 608.2b — illegal target → effect does
/// nothing). Same lossy-stub caveat as
/// <see cref="Majik.Core.Services.SpellRedirector"/>: the pre-built
/// effect closures captured by the redirected spell already baked in
/// their original target, so v1 only rewrites the spell's
/// <see cref="Spell.ChosenTargets"/> bookkeeping (visible to CR 608.2b
/// legality recheck) without flipping the actual damage / counter /
/// destroy landing site. A future revision of <see cref="SpellCaster"/>
/// + <see cref="StackResolver"/> can promote the v1 stub into real
/// semantics without touching this factory.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ability targets</b>: Spellskite's printed clause is "target spell
///   or ability with a single target". v1 only supports redirecting
///   spells — the engine's <see cref="ActivatedAbility"/> / triggered-
///   ability surface doesn't yet carry an editable "chosen targets" slot
///   parallel to <see cref="Spell.ChosenTargets"/>. Same gap as the
///   Deflection / Imp's Mischief / Shunt / Swerve binding in
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.RedirectTemplate"/>.
/// - <b>Hybrid pip on an activated ability</b>: a future
///   <see cref="ICost"/> shape that models a single-pip hybrid choice
///   ({U/P}, {2/W}, etc.) on <see cref="ActivatedAbility"/> would
///   collapse the two parallel abilities into one. The current
///   <see cref="Majik.Core.Costs.PhyrexianManaAlternativeCost"/> is
///   wired only for spell-level alt-costs.
/// </summary>
public static class SpellskiteFactory
{
    public const string CardName = "Spellskite";
    public const string PrintedManaCost = "{2}";
    public const int Power = 0;
    public const int Toughness = 4;

    /// <summary>Construct Spellskite — Artifact Creature — Horror 0/4 {2}.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Horror });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType-based lookups + colour identity see
        // both types (mirrors Wurmcoil Engine / Walking Ballista).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // Pay {U} variant.
        var manaAbility = BuildRedirectAbility(
            card,
            owner,
            new ICost[] { new ManaCostCost("{U}") });
        card.AddAbility(manaAbility);

        // Pay 2 life variant. CR 118.8 — the phyrexian-pip 2-life
        // alternative; see PhyrexianManaAlternativeCost for the
        // spell-level analogue.
        var lifeAbility = BuildRedirectAbility(
            card,
            owner,
            new ICost[] { AdditionalCost.PayLife(2) });
        card.AddAbility(lifeAbility);

        return card;
    }

    /// <summary>
    /// Build one redirect <see cref="ActivatedAbility"/> with the supplied
    /// cost list. Effect: on resolve, if the chosen target is a
    /// <see cref="Spell"/> with exactly one entry in
    /// <see cref="Spell.ChosenTargets"/>, rewrite that entry to
    /// <paramref name="spellskite"/>. Multi-target spells (or any other
    /// chosen target shape) resolve as a no-op (CR 608.2b).
    /// </summary>
    private static ActivatedAbility BuildRedirectAbility(
        Creature spellskite,
        Player controller,
        ICost[] costs)
    {
        ActivatedAbility? ability = null;

        var effect = new Effect(
            $"{CardName}: redirect target single-target spell to {CardName}",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0
                    || ability.ChosenTargets[0].Count == 0) return;

                var picked = ability.ChosenTargets[0][0];
                if (picked is not Spell spell) return;
                // CR 114.6 / Spellskite-specific clause — only redirect a
                // spell with a SINGLE target. Multi-target picks are
                // ineligible → no-op (CR 608.2b).
                if (spell.ChosenTargets.Count != 1) return;

                spell.ChosenTargets[0] = spellskite;
            });

        ability = new ActivatedAbility(
            source: spellskite,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell or ability with a single target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            });

        return ability;
    }
}
