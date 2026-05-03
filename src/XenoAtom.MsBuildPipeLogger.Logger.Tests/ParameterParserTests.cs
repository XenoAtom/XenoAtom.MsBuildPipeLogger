// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Logger.Tests;

[TestClass]
public class ParameterParserTests
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
    public void ParseParameters_WithAnonymousPipeHandle_ReturnsHandleSegment(string parameters)
    {
        var parts = ParameterParser.ParseParameters(parameters);

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
    public void ParseParameters_WithNamedPipe_ReturnsNameSegment(string parameters)
    {
        var parts = ParameterParser.ParseParameters(parameters);

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
    public void ParseParameters_WithNamedPipeAndServer_ReturnsNameAndServerSegments(string parameters)
    {
        var parts = ParameterParser.ParseParameters(parameters);

        CollectionAssert.AreEqual(new[]
        {
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Name, "Foo"),
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Server, "Bar")
        }, parts);
    }

    [TestMethod]
    [DataRow("server=Bar;name=Foo")]
    [DataRow("SERVER=Bar;NAME=Foo")]
    [DataRow(" server = Bar ; name = Foo")]
    public void ParseParameters_WithServerThenNamedPipe_ReturnsServerAndNameSegments(string parameters)
    {
        var parts = ParameterParser.ParseParameters(parameters);

        CollectionAssert.AreEqual(new[]
        {
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Server, "Bar"),
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Name, "Foo")
        }, parts);
    }

    [TestMethod]
    public void ParseParameters_WithEqualsInValue_PreservesValue()
    {
        var parts = ParameterParser.ParseParameters("name=Foo=Bar");

        CollectionAssert.AreEqual(new[]
        {
            new KeyValuePair<ParameterParser.ParameterType, string>(ParameterParser.ParameterType.Name, "Foo=Bar")
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
    [DataRow("name=")]
    [DataRow("server=foo;name=")]
    [DataRow("socket=/tmp/foo")]
    [DataRow("socket=/tmp/foo;name=bar")]
    public void GetPipeFromParameters_WithInvalidParameters_ThrowsLoggerException(string parameters)
    {
        Assert.Throws<LoggerException>(() => ParameterParser.GetPipeFromParameters(parameters));
    }
}
