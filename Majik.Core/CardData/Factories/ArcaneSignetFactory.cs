using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcane Signet (Throne of Eldraine Commander, {2}).
///
/// Artifact. Oracle text:
///   "{T}: Add one mana of any color in your commander's color identity."
///
/// ## Commander-identity clause degrades to any-color (v1)
/// Majik is a 1v1 / no-Commander engine — there is no commander, so a
/// commander's colour identity is undefined. Per the standard posture the
/// other commander-flavoured fixers take here (the implemented Signet /
/// Talisman mana rocks), the "any color in your commander's color identity"
/// restriction has no narrowing effect in this format and degrades to a
/// plain "{T}: Add one mana of any color". CR 605.1 — mana ability, doesn't
/// use the stack; CR 106.6 — a "mana of any color" instruction lets the
/// player choose the colour.
///
/// ## Implementation
/// Loads <c>Majik.Core/CardData/Cards/arcane-signet.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The
/// "add one mana of any color" ability is modeled as five
/// <see cref="Abilities.ManaAbility"/> instances (one per WUBRG) in the JSON
/// — the same any-colour shape <see cref="OrnithopterOfParadiseFactory"/>
/// uses; the mana picker satisfies any single colour pip by selecting the
/// matching ability slot. Unlike the Ravnica Signets this rock has no
/// printed {1} activation cost — only the bare {T} self-tap baked into
/// <see cref="Abilities.ManaAbility"/>'s default ctor path.
/// </summary>
[CardName("Arcane Signet")]
public static class ArcaneSignetFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("arcane-signet");

    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
