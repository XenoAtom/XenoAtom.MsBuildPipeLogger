// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Provides helpers for locating and configuring the MSBuild pipe logger that is bundled with this package.
/// </summary>
public static class PipeLoggerServer
{
    /// <summary>
    /// The directory, relative to <see cref="AppContext.BaseDirectory"/>, that contains the bundled logger assembly.
    /// </summary>
    public const string LoggerDirectoryName = "XenoAtom.MsBuildPipeLogger";

    /// <summary>
    /// The file name of the bundled logger assembly.
    /// </summary>
    public const string LoggerAssemblyFileName = "XenoAtom.MsBuildPipeLogger.Logger.dll";

    /// <summary>
    /// The fully qualified MSBuild logger type name.
    /// </summary>
    public const string LoggerTypeName = "XenoAtom.MsBuildPipeLogger.PipeLogger";

    /// <summary>
    /// Gets the expected location of the bundled logger assembly under <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    /// <returns>The absolute path to the bundled logger assembly.</returns>
    public static string GetLoggerAssemblyPath() => GetLoggerAssemblyPath(AppContext.BaseDirectory);

    /// <summary>
    /// Gets the expected location of the bundled logger assembly under the specified base directory.
    /// </summary>
    /// <param name="baseDirectory">The application base directory that contains the logger content directory.</param>
    /// <returns>The absolute path to the bundled logger assembly.</returns>
    /// <exception cref="ArgumentException"><paramref name="baseDirectory"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="baseDirectory"/> is <see langword="null"/>.</exception>
    public static string GetLoggerAssemblyPath(string baseDirectory)
    {
        if (baseDirectory is null)
        {
            throw new ArgumentNullException(nameof(baseDirectory));
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("The base directory cannot be empty or whitespace.", nameof(baseDirectory));
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, LoggerDirectoryName, LoggerAssemblyFileName));
    }

    /// <summary>
    /// Gets an MSBuild logger specification that can be passed to MSBuild's logger command-line option.
    /// </summary>
    /// <param name="loggerParameters">The pipe logger parameters, such as an anonymous pipe handle or <c>name=&lt;pipeName&gt;</c>.</param>
    /// <returns>An MSBuild logger specification in the form <c>type,assembly;parameters</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="loggerParameters"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="loggerParameters"/> is <see langword="null"/>.</exception>
    public static string GetLoggerSpecification(string loggerParameters) =>
        GetLoggerSpecification(AppContext.BaseDirectory, loggerParameters);

    /// <summary>
    /// Gets an MSBuild logger specification that can be passed to MSBuild's logger command-line option.
    /// </summary>
    /// <param name="baseDirectory">The application base directory that contains the logger content directory.</param>
    /// <param name="loggerParameters">The pipe logger parameters, such as an anonymous pipe handle or <c>name=&lt;pipeName&gt;</c>.</param>
    /// <returns>An MSBuild logger specification in the form <c>type,assembly;parameters</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="baseDirectory"/> or <paramref name="loggerParameters"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="baseDirectory"/> or <paramref name="loggerParameters"/> is <see langword="null"/>.</exception>
    public static string GetLoggerSpecification(string baseDirectory, string loggerParameters)
    {
        if (loggerParameters is null)
        {
            throw new ArgumentNullException(nameof(loggerParameters));
        }

        if (string.IsNullOrWhiteSpace(loggerParameters))
        {
            throw new ArgumentException("The logger parameters cannot be empty or whitespace.", nameof(loggerParameters));
        }

        return $"{LoggerTypeName},{GetLoggerAssemblyPath(baseDirectory)};{loggerParameters}";
    }
}
