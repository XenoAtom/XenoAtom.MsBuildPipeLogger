// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Tests;

internal static class BuildEventAssertions
{
    public static void WriteEvents(IPipeWriter writer, int messageCount, bool includeBuildFinished = false, bool includeMessageAfterBuildFinished = false)
    {
        writer.Write(new BuildStartedEventArgs("Testing", "help"));
        for (var index = 0; index < messageCount; index++)
        {
            writer.Write(CreateMessage(index));
        }

        if (includeBuildFinished)
        {
            writer.Write(new BuildFinishedEventArgs("Finished", "help", true));
        }

        if (includeMessageAfterBuildFinished)
        {
            writer.Write(new BuildMessageEventArgs("After finish", "help", "sender", MessageImportance.High));
        }
    }

    public static void AssertEvents(IReadOnlyList<PipeBuildEventArgs> events, int messageCount, bool includeBuildFinished = false)
    {
        var expectedCount = messageCount + 1 + (includeBuildFinished ? 1 : 0);
        Assert.AreEqual(expectedCount, events.Count);
        Assert.IsInstanceOfType(events[0], typeof(PipeBuildStartedEventArgs));
        Assert.AreEqual("Testing", events[0].Message);

        for (var index = 0; index < messageCount; index++)
        {
            var eventArg = events[index + 1];
            Assert.IsInstanceOfType(eventArg, typeof(PipeBuildMessageEventArgs));
            Assert.AreEqual($"Testing {index}", eventArg.Message);
        }

        if (includeBuildFinished)
        {
            var finishedEvent = events[events.Count - 1];
            Assert.IsInstanceOfType(finishedEvent, typeof(PipeBuildFinishedEventArgs));
            Assert.AreEqual("Finished", finishedEvent.Message);
            Assert.IsTrue(((PipeBuildFinishedEventArgs)finishedEvent).Succeeded);
        }
    }

    private static BuildMessageEventArgs CreateMessage(int index) =>
        new($"Testing {index}", "help", "sender", MessageImportance.Normal);
}
