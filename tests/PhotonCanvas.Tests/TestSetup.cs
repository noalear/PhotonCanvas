using System.Runtime.CompilerServices;
using PhotonCanvas.Core;

namespace PhotonCanvas.Tests;

static class TestSetup
{
    /// <summary>
    /// The same MOPA table on every computer: the built-in one, not whatever xTool Studio is installed
    /// (<see cref="MopaTable"/>). Runs before any test touches the machine profiles.
    /// </summary>
    [ModuleInitializer]
    internal static void UseBuiltInMopaTable() => Environment.SetEnvironmentVariable(MopaTable.SourceVariable, "builtin");
}
