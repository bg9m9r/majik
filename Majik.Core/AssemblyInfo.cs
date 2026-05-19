using System.Runtime.CompilerServices;

// Friend assemblies that participate in the engine's internal contract.
// Treat as an explicit allow-list — additions should be justified by need
// for internal access (setters, helpers) that the public surface omits
// intentionally.
[assembly: InternalsVisibleTo("Majik.Core.Api")]
[assembly: InternalsVisibleTo("Majik.Core.Api.Tests")]
[assembly: InternalsVisibleTo("Majik.Core.Tests")]
[assembly: InternalsVisibleTo("Majik.Console")]
