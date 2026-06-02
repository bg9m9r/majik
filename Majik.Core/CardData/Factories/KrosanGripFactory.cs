using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Krosan Grip (Dissension, {2}{G}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Split second (As long as this spell is on the stack, players can't cast
///    spells or activate abilities that aren't mana abilities.)
///    Destroy target artifact or enchantment."
///
/// Krosan Grip is the split-second-stapled cousin of Disenchant: the same
/// "destroy target artifact or enchantment" body
/// (<see cref="DisenchantFactory.BuildDefinition"/>) plus the printed Split
/// second keyword — exactly the assembly pattern <see cref="SuddenEdictFactory"/>
/// uses (embedded-JSON card shape + Split second marker + a shared destroy/edict
/// body). Both primitives already ship, so no new engine mechanic is introduced.
///
/// ## Implemented (v1)
/// - Instant {2}{G} green card shape. Card shape comes from the embedded JSON
///   (<c>krosan-grip.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Split second</b> (CR 702.61) modelled as a <see cref="KeywordAbility"/>
///   marker ("Split second"), exactly as <see cref="ExtirpateFactory"/> /
///   <see cref="SuddenEdictFactory"/>. The full restriction surface (preventing
///   other spells / non-mana activated abilities while the spell is on the
///   stack) is enforced elsewhere once the priority manager learns to consult
///   the marker; this factory declares the keyword on the card, matching the
///   project-wide convention for keyword markers.
/// - <see cref="BuildDefinition"/> delegates to
///   <see cref="DisenchantFactory.BuildDefinition"/>: one 1..1 "target artifact
///   or enchantment" request; on resolution the chosen permanent is destroyed
///   via the Destroy-reason gate (CR 701.7), so Indestructible (CR 702.12) and
///   regeneration (CR 701.15) shields are honoured. Illegal target at
///   resolution (no longer a battlefield artifact/enchantment) → no-op
///   (CR 608.2b).
///
/// ## Rules citations
/// - CR 702.61 — Split second.
/// - CR 701.7  — Destroy (honours Indestructible / regeneration).
/// - CR 608.2b — single-target spell with an illegal target fizzles.
///
/// ## Deferred (v1 gaps)
/// - <b>Split second restriction enforcement</b>: the marker is present, but the
///   priority manager does not yet consult it (same queue as
///   <see cref="ExtirpateFactory"/> / <see cref="SuddenEdictFactory"/>).
/// </summary>
[CardName("Krosan Grip")]
public static class KrosanGripFactory
{
    public const string CardName = "Krosan Grip";
    public const string Slug = "krosan-grip";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>
    /// Build the Krosan Grip instant from the embedded JSON definition and stamp
    /// the Split second keyword marker (CR 702.61). Card shape only — the
    /// resolve-time target request + destroy body is built on demand via
    /// <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 702.61 — Split second declared as a keyword marker. The priority
        // manager will consult markers like this once split-second restriction
        // enforcement lands; for now the marker documents the card's printed
        // keyword and matches ExtirpateFactory / SuddenEdictFactory's posture.
        card.AddAbility(new KeywordAbility("Split second", card, owner));
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Krosan Grip is cast.
    /// Identical to Disenchant: a single 1..1 "target artifact or enchantment"
    /// request; on resolution that permanent is destroyed (CR 701.7). Split
    /// second is a static restriction on the stack, not part of the resolve
    /// body, so the destroy effect is exactly Disenchant's.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver) =>
        DisenchantFactory.BuildDefinition(targetResolver);
}
