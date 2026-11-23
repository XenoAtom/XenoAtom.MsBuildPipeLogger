// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.Diagnostics;

namespace XenoAtom.MsBuildPipeLogger.Tests;

internal static class DotNetProcess
{
    // `dotnet test` exports the paths of its SDK to the test process. A `dotnet` started from a test
    // resolves its own SDK, which is another one when the project it builds is outside the global.json
    // of this repository, and MSBuild then fails to start its task host (MSB4216) with the mixed paths.
    private static readonly string[] InheritedSdkEnvironmentVariables = { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath" };

    /// <summary>
    /// Removes the SDK paths inherited from the `dotnet` that runs the tests, so that the process resolves its own SDK.
    /// </summary>
    public static void RemoveInheritedSdkVariables(ProcessStartInfo startInfo)
    {
        foreach (var name in InheritedSdkEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }
    }
}
