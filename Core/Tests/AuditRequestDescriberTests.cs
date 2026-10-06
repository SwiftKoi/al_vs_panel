using AlegacyWebPanel.Core.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AlegacyWebPanel.Core.Tests;

public sealed record SendCommandRequest(string Command);

public sealed record SampleRequest(string PlayerName, string? Reason, string Password, string Content);

public sealed class AuditRequestDescriberTests
{
    [Fact]
    public void Server_comes_from_the_route_and_other_route_and_query_values_become_details()
    {
        var context = Context(route: new() { ["serverId"] = "main", ["root"] = "data" }, query: "?path=Mods%2Fa.zip&permanent=true");

        var description = AuditRequestDescriber.Describe(context, []);

        Assert.Equal("main", description.ServerId);
        Assert.Equal("Mods/a.zip", description.Target);
        Assert.Equal("{\"root\":\"data\",\"path\":\"Mods/a.zip\",\"permanent\":\"true\"}", description.DetailsJson);
    }

    [Fact]
    public void Body_dto_fields_are_recorded_but_secrets_and_file_contents_are_removed()
    {
        var context = Context();
        var body = new SampleRequest("Orex_", "griefing", "hunter2-hunter2", "line one\nsecret = abc");

        var description = AuditRequestDescriber.Describe(context, [body]);

        Assert.Equal("Orex_", description.Target);
        Assert.Equal("{\"playerName\":\"Orex_\",\"reason\":\"griefing\"}", description.DetailsJson);
        Assert.DoesNotContain("hunter2", description.DetailsJson);
        Assert.DoesNotContain("abc", description.DetailsJson);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("apiKey")]
    [InlineData("code")]
    [InlineData("newPassword")]
    public void Sensitive_query_and_route_values_are_never_recorded(string name)
    {
        var context = Context(route: new() { [name] = "s3cret" }, query: $"?{name}=s3cret");

        var description = AuditRequestDescriber.Describe(context, []);

        Assert.Null(description.DetailsJson);
    }

    [Fact]
    public void Long_values_are_cut_and_oversized_details_are_replaced_by_a_marker()
    {
        var longValue = new string('a', 500);
        var single = AuditRequestDescriber.Describe(Context(query: $"?path={longValue}"), []);
        Assert.Equal(AuditRequestDescriber.MaximumValueLength + 1, single.Target!.Length);
        Assert.EndsWith("…", single.Target);

        var many = string.Join("&", Enumerable.Range(0, 60).Select(i => $"k{i}={new string('b', 190)}"));
        var large = AuditRequestDescriber.Describe(Context(query: "?" + many), []);
        Assert.Equal("{\"truncated\":true}", large.DetailsJson);
    }

    [Theory]
    [InlineData("/stats", "/stats")]
    [InlineData("/gamemode Orex_ 1", "/gamemode Orex_ 1")]
    [InlineData("/serverconfig password hunter2", "/serverconfig [redacted]")]
    [InlineData("/serverconfig token abc", "/serverconfig [redacted]")]
    public void Console_commands_are_kept_but_secret_looking_arguments_are_redacted(string command, string expected)
    {
        var description = AuditRequestDescriber.Describe(Context(), [new SendCommandRequest(command)]);

        Assert.Equal($"{{\"command\":\"{expected}\"}}", description.DetailsJson);
    }

    [Fact]
    public void Requests_with_nothing_to_describe_have_no_details()
    {
        var description = AuditRequestDescriber.Describe(Context(), []);

        Assert.Null(description.ServerId);
        Assert.Null(description.Target);
        Assert.Null(description.DetailsJson);
    }

    [Fact]
    public void Target_falls_back_to_route_ids()
    {
        var description = AuditRequestDescriber.Describe(Context(route: new() { ["serverId"] = "main", ["modId"] = "carryon" }), []);

        Assert.Equal("carryon", description.Target);
    }

    private static DefaultHttpContext Context(Dictionary<string, string>? route = null, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        foreach (var (key, value) in route ?? [])
        {
            context.Request.RouteValues[key] = value;
        }

        return context;
    }
}
