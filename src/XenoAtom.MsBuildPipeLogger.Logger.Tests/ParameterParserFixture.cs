// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Logger.Tests;

[TestClass]
public class ParameterParserFixture
{
    [TestMethod]
    [DataRow("1234")]
    [DataRow(" 1234 ")]
    [DataRow("handle=1234")]
    [DataRow("HANDLE=1234")]
    [DataRow(" handle = 1234 ")]
    [DataRow("\"1234\"")]
    [DataRow("\"handle=1234\"")]
    [DataRow("\" handle = 1234 \"")]
    public void GetsAnonymousPipe(string parameters)
    {
        // Given, When
        var parts = ParameterParser.ParseParameters(parameters);

        // Then
        CollectionAssert.AreEqual(new[]
        {
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Handle, "1234")
        }, parts);
    }

    [TestMethod]
    [DataRow("name=Foo")]
    [DataRow("\"name=Foo\"")]
    [DataRow("NAME=Foo")]
    [DataRow(" name = Foo ")]
    public void GetsNamedPipe(string parameters)
    {
        // Given, When
        var parts = ParameterParser.ParseParameters(parameters);

        // Then
        CollectionAssert.AreEqual(new[]
        {
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Name, "Foo")
        }, parts);
    }

    [TestMethod]
    [DataRow("name=Foo;server=Bar")]
    [DataRow("\"name=Foo;server=Bar\"")]
    [DataRow("NAME=Foo;SERVER=Bar")]
    [DataRow(" name = Foo ; server = Bar")]
    public void GetsNamedPipeWithServer(string parameters)
    {
        // Given, When
        var parts = ParameterParser.ParseParameters(parameters);

        // Then
        CollectionAssert.AreEqual(new[]
        {
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Name, "Foo"),
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Server, "Bar")
        }, parts);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("123;foo;baz")]
    [DataRow("handle=1234;foo")]
    [DataRow("handle=1234;name=bar")]
    [DataRow("foo=bar")]
    [DataRow("123;name=bar")]
    [DataRow("server=foo")]
    [DataRow("socket=/tmp/foo")]
    [DataRow("socket=/tmp/foo;name=bar")]
    public void ThrowsForInvalidParameters(string parameters)
    {
        // Given, When, Then
        Assert.Throws<LoggerException>(() => ParameterParser.GetPipeFromParameters(parameters));
    }
}