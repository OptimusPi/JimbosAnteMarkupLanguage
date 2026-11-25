using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// The YAML↔JSON bridge is what lets a web app hold a typed filter without parsing YAML. These
/// cases cover the awkward scalars, empty documents and JSON errors that are easy to regress.
/// </summary>
public sealed class JamlTextConversionMoreTests
{
    [Fact]
    public void EmptyYaml_IsAnEmptyJsonObject()
    {
        Assert.Equal("{}", JamlConfigLoader.ToJson("").Trim());
        Assert.Equal("{}\n", JamlConfigLoader.ToJson("{}"));
    }

    [Fact]
    public void PlainBooleansAndNumbers_KeepTheirJsonTypes()
    {
        string json = JamlConfigLoader.ToJson("yes: false\nno: True\ncount: 010\n");
        Assert.Contains("\"yes\": false", json);
        Assert.Contains("\"no\": true", json);
        // JSON has no octal: a leading zero stays a string rather than changing the value.
        Assert.Contains("\"count\": \"010\"", json);
    }

    [Fact]
    public void RootScalarsBecomeFlowYaml()
    {
        Assert.Equal("42\n", JamlConfigLoader.ToYaml("42"));
        Assert.Equal("null\n", JamlConfigLoader.ToYaml("null"));
        Assert.Equal("hi\n", JamlConfigLoader.ToYaml("\"hi\""));
    }

    [Fact]
    public void DuplicateJsonKeys_AreRefusedWithALine()
    {
        var ex = Assert.Throws<JamlFilterException>(() => JamlConfigLoader.ToYaml("{\"a\":1,\n\"a\":2}"));
        Assert.Contains("JSON line", ex.Message);
    }

    [Fact]
    public void NestedArraysAndQuotedStrings_RoundTripReadably()
    {
        const string json = """
            {"a":[[{"b":1}]],"nil":null,"empty":[],"emptyMap":{},"text":"a\\b\"c\nd\te\u0001"}
            """;
        string yaml = JamlConfigLoader.ToYaml(json);

        Assert.Contains("    - b: 1", yaml);
        Assert.Contains("nil: null", yaml);
        Assert.Contains("empty: []", yaml);
        Assert.Contains("emptyMap: {}", yaml);
        Assert.Contains("\\\\", yaml);
        Assert.Contains("\\\"", yaml);
        Assert.Contains("\\n", yaml);
        Assert.Contains("\\t", yaml);
        Assert.Contains("\\u0001", yaml);
    }
}
