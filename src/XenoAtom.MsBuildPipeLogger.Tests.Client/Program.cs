// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Tests.Client;

internal class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine(string.Join("; ", args));
        var messages = int.Parse(args[1]);
        try
        {
            using (var writer = ParameterParser.GetPipeFromParameters(args[0]))
            {
                writer.Write(new BuildStartedEventArgs($"Testing", "help"));
                for (var c = 0; c < messages; c++)
                {
                    writer.Write(new BuildMessageEventArgs($"Testing {c}", "help", "sender", MessageImportance.Normal));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return 1;
        }

        return 0;
    }
}