// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger.Tests;

[TestClass]
public class PipeLoggerServerInfoTests
{
    [TestMethod]
    public void GetLoggerAssemblyPath_ReturnsBundledLoggerInIsolatedOutputDirectory()
    {
        var loggerAssemblyPath = PipeLoggerServer.GetLoggerAssemblyPath();
        var loggerDirectory = Path.GetDirectoryName(loggerAssemblyPath);

        Assert.IsNotNull(loggerDirectory);
        Assert.AreEqual(PipeLoggerServer.LoggerAssemblyFileName, Path.GetFileName(loggerAssemblyPath));
        Assert.AreEqual(PipeLoggerServer.LoggerDirectoryName, Path.GetFileName(loggerDirectory));
        Assert.IsTrue(File.Exists(loggerAssemblyPath), $"Expected the bundled logger assembly at '{loggerAssemblyPath}'.");
        CollectionAssert.AreEquivalent(new[] { PipeLoggerServer.LoggerAssemblyFileName }, Directory.GetFiles(loggerDirectory).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void GetLoggerSpecification_ReturnsMsBuildLoggerSyntax()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "base");
        var expectedPath = Path.Combine(baseDirectory, PipeLoggerServer.LoggerDirectoryName, PipeLoggerServer.LoggerAssemblyFileName);

        var specification = PipeLoggerServer.GetLoggerSpecification(baseDirectory, "name=pipe");

        Assert.AreEqual($"{PipeLoggerServer.LoggerTypeName},{expectedPath};name=pipe", specification);
    }

    [TestMethod]
    public void GetLoggerSpecification_WithInvalidParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PipeLoggerServer.GetLoggerSpecification(null!));
        Assert.Throws<ArgumentException>(() => PipeLoggerServer.GetLoggerSpecification(" "));
        Assert.Throws<ArgumentNullException>(() => PipeLoggerServer.GetLoggerSpecification(null!, "name=pipe"));
        Assert.Throws<ArgumentException>(() => PipeLoggerServer.GetLoggerSpecification(" ", "name=pipe"));
    }
}
