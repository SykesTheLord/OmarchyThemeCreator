using Xunit;

namespace OmarchyThemeCreator.Tests.Support;

/// <summary>
/// xUnit collection for tests that mutate process-global environment variables (HOME,
/// OMARCHY_PATH) via <see cref="TempHome"/>. Environment variables are process-wide, so these
/// tests must not run in parallel with each other — one test's <c>$HOME</c> would leak into
/// another's. Marking every such class <c>[Collection("env")]</c> puts them in a single
/// non-parallel collection. Tests that touch no shared global state stay fully parallel.
/// </summary>
[CollectionDefinition("env", DisableParallelization = true)]
public sealed class EnvCollection
{
}
