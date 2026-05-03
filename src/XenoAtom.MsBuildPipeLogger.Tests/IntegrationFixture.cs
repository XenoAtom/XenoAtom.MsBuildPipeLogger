using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MsBuildPipeLogger.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class IntegrationFixture
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(100000)]
        public void SerializesData(int messageCount)
        {
            // Given
            Stopwatch sw = new Stopwatch();
            MemoryStream memory = new MemoryStream();
            BinaryWriter binaryWriter = new BinaryWriter(memory);
            BuildEventArgsWriterProxy writer = new BuildEventArgsWriterProxy(binaryWriter);
            BinaryReader binaryReader = new BinaryReader(memory);
            BuildEventArgsReader reader = new BuildEventArgsReader(binaryReader, GetBinaryLoggerFileFormatVersion());
            List<BuildEventArgs> eventArgs = new List<BuildEventArgs>();

            // When
            sw.Start();
            writer.Write(new BuildStartedEventArgs("Testing", "help"));
            for (int m = 0; m < messageCount; m++)
            {
                writer.Write(new BuildMessageEventArgs($"Testing {m}", "help", "sender", MessageImportance.Normal));
            }
            sw.Stop();
            TestContext.WriteLine($"Serialization completed in {sw.ElapsedMilliseconds} ms");

            memory.Position = 0;
            sw.Restart();
            BuildEventArgs? e;
            while ((e = reader.Read()) is not null)
            {
                eventArgs.Add(e);
                if (memory.Position >= memory.Length)
                {
                    break;
                }
            }
            sw.Stop();
            TestContext.WriteLine($"Deserialization completed in {sw.ElapsedMilliseconds} ms");

            // Then
            Assert.AreEqual(messageCount + 1, eventArgs.Count);
            Assert.IsInstanceOfType(eventArgs[0], typeof(BuildStartedEventArgs));
            Assert.AreEqual("Testing", eventArgs[0].Message);
            int c = 0;
            foreach (BuildEventArgs eventArg in eventArgs.Skip(1))
            {
                Assert.IsInstanceOfType(eventArg, typeof(BuildMessageEventArgs));
                Assert.AreEqual($"Testing {c++}", eventArg.Message);
            }
        }

        [TestMethod]
        public void NamedPipeSupportsCancellation()
        {
            // Given
            BuildEventArgs? buildEvent;
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                using (NamedPipeLoggerServer server = new NamedPipeLoggerServer("Foo", tokenSource.Token))
                {
                    // When
                    tokenSource.CancelAfter(1000);  // The call to .Read() below will block so need to set a timeout for cancellation
                    buildEvent = server.Read();
                }
            }

            // Then
            Assert.IsNull(buildEvent);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(100000)]
        public void SendsDataOverAnonymousPipe(int messageCount)
        {
            // Given
            List<BuildEventArgs> eventArgs = new List<BuildEventArgs>();
            int exitCode;
            using (AnonymousPipeLoggerServer server = new AnonymousPipeLoggerServer())
            {
                server.AnyEventRaised += (s, e) => eventArgs.Add(e);

                // When
                exitCode = RunClientProcess(server, server.GetClientHandle(), messageCount);
            }

            // Then
            Assert.AreEqual(0, exitCode);
            Assert.AreEqual(messageCount + 1, eventArgs.Count);
            Assert.IsInstanceOfType(eventArgs[0], typeof(BuildStartedEventArgs));
            Assert.AreEqual("Testing", eventArgs[0].Message);
            int c = 0;
            foreach (BuildEventArgs eventArg in eventArgs.Skip(1))
            {
                Assert.IsInstanceOfType(eventArg, typeof(BuildMessageEventArgs));
                Assert.AreEqual($"Testing {c++}", eventArg.Message);
            }
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(100000)]
        public void SendsDataOverNamedPipe(int messageCount)
        {
            // Given
            List<BuildEventArgs> eventArgs = new List<BuildEventArgs>();
            int exitCode;
            using (NamedPipeLoggerServer server = new NamedPipeLoggerServer("foo"))
            {
                server.AnyEventRaised += (s, e) => eventArgs.Add(e);

                // When
                exitCode = RunClientProcess(server, "name=foo", messageCount);
            }

            // Then
            Assert.AreEqual(0, exitCode);
            Assert.AreEqual(messageCount + 1, eventArgs.Count);
            Assert.IsInstanceOfType(eventArgs[0], typeof(BuildStartedEventArgs));
            Assert.AreEqual("Testing", eventArgs[0].Message);
            int c = 0;
            foreach (BuildEventArgs eventArg in eventArgs.Skip(1))
            {
                Assert.IsInstanceOfType(eventArg, typeof(BuildMessageEventArgs));
                Assert.AreEqual($"Testing {c++}", eventArg.Message);
            }
        }

        private static int GetBinaryLoggerFileFormatVersion()
        {
            FieldInfo fileFormatVersionField = typeof(BinaryLogger).GetField(
                "FileFormatVersion",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(BinaryLogger).FullName, "FileFormatVersion");

            object? fileFormatVersion = fileFormatVersionField.GetValue(null);
            if (fileFormatVersion is not int version)
            {
                throw new InvalidOperationException(
                    $"Field '{typeof(BinaryLogger).FullName}.FileFormatVersion' must be an integer.");
            }

            return version;
        }

        private int RunClientProcess(IPipeLoggerServer server, string arguments, int messages)
        {
            using Process process = new Process();
            int exitCode = -1;
            bool started = false;
            try
            {
                string testDirectory = Path.GetDirectoryName(typeof(IntegrationFixture).Assembly.Location)
                    ?? throw new InvalidOperationException("Could not locate test assembly directory.");
                string clientDirectory = testDirectory.Replace(
                    "XenoAtom.MsBuildPipeLogger.Tests",
                    "XenoAtom.MsBuildPipeLogger.Tests.Client",
                    StringComparison.Ordinal);
                string clientAssemblyPath = Path.Combine(clientDirectory, "XenoAtom.MsBuildPipeLogger.Tests.Client.dll");

                process.StartInfo.FileName = "dotnet";
                process.StartInfo.Arguments = $"\"{clientAssemblyPath}\" \"{arguments}\" {messages}";
                process.StartInfo.WorkingDirectory = clientDirectory;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;

                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        TestContext.WriteLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        TestContext.WriteLine(e.Data);
                    }
                };

                started = process.Start();
                TestContext.WriteLine($"Started process {process.Id}");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                server.ReadAll();
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Process error: {ex}");
            }
            finally
            {
                if (started)
                {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    TestContext.WriteLine($"Exited process {process.Id} with code {exitCode}");
                }
            }
            return exitCode;
        }
    }
}