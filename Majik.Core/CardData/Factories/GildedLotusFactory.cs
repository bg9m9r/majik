using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gilded Lotus (Mirrodin / reprints).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{T}: Add three mana of any one color."
///   Printed mana cost: {5}.
///
/// Analogue of <see cref="LotusBloomFactory"/> — identical mana output
/// ("three mana of any one color") but without Lotus Bloom's Suspend
/// wrapper and without the sacrifice rider on the ability. Gilded Lotus
/// is a permanent fixture: it taps for three mana of a single chosen
/// colour and stays on the battlefield, so it can do it again each turn.
///
/// ## Modelling
/// "Three mana of any one color" is bound as five
/// <see cref="Majik.Core.Abilities.ManaAbility"/> instances, one per WUBRG,
/// each producing three pips of its colour ({W}{W}{W}, {U}{U}{U}, …). This
/// is the same "any one color" decomposition Lotus Bloom uses; the bot's
/// source-picker selects the right colour mode at payment time. Here the
/// five abilities are authored declaratively in
/// <c>Majik.Core/CardData/Cards/gilded-lotus.json</c> as vanilla
/// "{T}: Add &lt;pips&gt;" mana shapes (no additional cost), so
/// <see cref="CardDefinitionFactory"/> wires each through the simple
/// <see cref="Majik.Core.Abilities.ManaAbility"/> constructor.
///
/// CR 605.1 — each is a mana ability: it doesn't use the stack. CR 605.1b
/// — only one of the five may be activated per "any one color" choice
/// because each taps Gilded Lotus as its cost; the {T} gate on
/// <see cref="Majik.Core.Abilities.ManaAbility.CanActivate"/> disables the
/// remaining four the moment one resolves and taps the source.
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/gilded-lotus.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>; <c>IsImplemented</c> flips
/// automatically from the <c>[CardName]</c> registry.
/// </summary>
[CardName("Gilded Lotus")]
public static class GildedLotusFactory
{
    public const string CardName = "Gilded Lotus";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("gilded-lotus");

    /// <summary>Construct Gilded Lotus owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
