using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Helpers;

public sealed class EntityDiffTests
{
    [Fact]
    public void Serialize_Null_ReturnsNull()
    {
        EntityDiff.Serialize(null).Should().BeNull();
    }

    [Fact]
    public void Serialize_SimpleObject_ReturnsValidJson()
    {
        var obj = new { Name = "Alice", Code = "EMP001" };

        var json = EntityDiff.Serialize(obj);

        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"Alice\"");
        json.Should().Contain("\"code\"");
        json.Should().Contain("\"EMP001\"");
    }

    [Fact]
    public void Serialize_ObjectWithNullProperty_OmitsNullField()
    {
        var obj = new { Name = "Bob", Code = (string?)null };

        var json = EntityDiff.Serialize(obj);

        json.Should().NotBeNull();
        json.Should().NotContain("\"code\"", "null properties should be omitted");
    }

    [Fact]
    public void Serialize_String_ReturnsQuotedString()
    {
        var json = EntityDiff.Serialize("hello");

        json.Should().Be("\"hello\"");
    }

    [Fact]
    public void Serialize_IsDeterministic()
    {
        var obj = new { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Value = 42 };

        var first = EntityDiff.Serialize(obj);
        var second = EntityDiff.Serialize(obj);

        first.Should().Be(second);
    }
}
