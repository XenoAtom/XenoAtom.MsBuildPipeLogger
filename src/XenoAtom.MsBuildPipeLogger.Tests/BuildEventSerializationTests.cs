// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace XenoAtom.MsBuildPipeLogger.Tests;

[TestClass]
public class BuildEventSerializationTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(100000)]
    public void BuildEventArgsWriterProxy_RoundTripsBuildMessages(int messageCount)
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var writer = new BuildEventArgsWriterProxy(binaryWriter);
        using var binaryReader = new BinaryReader(memory);
        using var reader = new BuildEventArgsReader(binaryReader, GetBinaryLoggerFileFormatVersion());
        var events = new List<BuildEventArgs>();

        BuildEventAssertions.WriteEvents(new BuildEventArgsWriterAdapter(writer), messageCount);
        binaryWriter.Flush();

        memory.Position = 0;
        BuildEventArgs? eventArgs;
        while ((eventArgs = reader.Read()) is not null)
        {
            events.Add(eventArgs);
            if (memory.Position >= memory.Length)
            {
                break;
            }
        }

        BuildEventAssertions.AssertEvents(events, messageCount);
    }

    private static int GetBinaryLoggerFileFormatVersion()
    {
        var fileFormatVersionField = typeof(BinaryLogger).GetField(
                                         "FileFormatVersion",
                                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                     ?? throw new MissingFieldException(typeof(BinaryLogger).FullName, "FileFormatVersion");

        var fileFormatVersion = fileFormatVersionField.GetValue(null);
        if (fileFormatVersion is not int version)
        {
            throw new InvalidOperationException(
                $"Field '{typeof(BinaryLogger).FullName}.FileFormatVersion' must be an integer.");
        }

        return version;
    }

    private sealed class BuildEventArgsWriterAdapter : IPipeWriter
    {
        private readonly BuildEventArgsWriterProxy _writer;

        public BuildEventArgsWriterAdapter(BuildEventArgsWriterProxy writer)
        {
            _writer = writer;
        }

        public void Write(BuildEventArgs e)
        {
            _writer.Write(e);
        }

        public void Dispose()
        {
        }
    }
}
