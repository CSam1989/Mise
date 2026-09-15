global using FluentAssertions;
global using Xunit;

// Deliberately NOT `global using Bunit;`. CLAUDE.md's original concern was that
// Bunit.TestContext collides with xUnit v3's own Xunit.TestContext — bunit 2.9+ renamed its
// type to BunitContext specifically to end that collision, so it no longer applies letter
// for letter. Kept local anyway: a global using still leaves every file in this project
// exposed to any *future* bunit type that happens to collide with an xUnit or BCL name,
// for no benefit over a one-line `using Bunit;` per file that needs it.
