namespace Majik.Core.CardData.Factories;

/// <summary>
/// Marks a static factory class as the implementation for a printed card
/// name. Scanned at compile time by <c>Majik.Core.SourceGen.NamedCardFactoryGenerator</c>
/// to produce <see cref="NamedCardFactory"/>'s dispatch switch.
///
/// Apply to a class that exposes a <c>public static &lt;Card&gt; Create(Player owner)</c>
/// overload. Multiple attributes are allowed so a single factory can serve
/// functional reprints or alternate printings:
/// <code>
/// [CardName("Wrath of God")]
/// [CardName("Damnation")]
/// public static class MassWipeFactory { ... }
/// </code>
///
/// ## Parametric cycles (e.g. fetchlands, horizon lands)
///
/// A single factory class can also implement an entire MTG card cycle by
/// passing extra string args after the card name. At dispatch time the
/// source generator emits a call to a <c>Create(Player owner, string[] args)</c>
/// overload, with the args array shaped as:
/// <c>[0] = printed card name, [1..] = per-card payload from this attribute</c>.
/// Example:
/// <code>
/// [CardName("Bloodstained Mire", "Swamp",  "Mountain")]
/// [CardName("Arid Mesa",         "Plains", "Mountain")]
/// public static class FetchLandCycleFactory
/// {
///     public static Land Create(Player owner) => Create(owner, new[] { "Default" });
///     public static Land Create(Player owner, string[] args)
///     {
///         var cardName = args[0];   // "Bloodstained Mire" or "Arid Mesa"
///         var basicA   = args[1];   // first basic-subtype
///         var basicB   = args[2];   // second basic-subtype
///         ...
///     }
/// }
/// </code>
///
/// The source generator picks the args-aware overload when the factory
/// declares one; otherwise it falls back to the plain <c>Create(Player)</c>
/// overload and the args are silently ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CardNameAttribute : Attribute
{
    /// <summary>The printed card name this factory serves.</summary>
    public string Name { get; }

    /// <summary>
    /// Per-card-name payload (e.g. the two basic-land subtypes for a fetchland).
    /// Empty for non-parametric factories.
    /// </summary>
    public string[] Args { get; }

    public CardNameAttribute(string name, params string[] args)
    {
        Name = name;
        Args = args ?? Array.Empty<string>();
    }
}
