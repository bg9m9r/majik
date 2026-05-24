namespace Majik.Core.CardData.Factories;

/// <summary>
/// Marks a static factory class as the implementation for a printed card
/// name. Scanned at compile time by <c>Majik.Core.SourceGen.NamedCardFactoryGenerator</c>
/// to produce <see cref="NamedCardFactory"/>'s dispatch switch.
///
/// Apply to a class that exposes a <c>public static &lt;Card&gt; Create(Player owner)</c>
/// overload. Multiple attributes are allowed so a single factory can
/// serve functional reprints or alternate printings:
/// <code>
/// [CardName("Wrath of God")]
/// [CardName("Damnation")]
/// public static class MassWipeFactory { ... }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CardNameAttribute : Attribute
{
    /// <summary>The printed card name this factory serves.</summary>
    public string Name { get; }

    public CardNameAttribute(string name)
    {
        Name = name;
    }
}
