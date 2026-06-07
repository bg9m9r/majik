using System.Runtime.CompilerServices;

// Expose internals to the Api test project so tests can reach the
// live-stack and turn-state seams on GameFacade without promoting
// those surfaces to public API.
[assembly: InternalsVisibleTo("Majik.Core.Api.Tests")]
