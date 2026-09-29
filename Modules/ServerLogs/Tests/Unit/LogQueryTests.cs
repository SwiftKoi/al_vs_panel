using AlegacyWebPanel.Modules.ServerLogs.Exceptions;
using AlegacyWebPanel.Modules.ServerLogs.Services;

namespace AlegacyWebPanel.ServerLogs.UnitTests;

public sealed class LogQueryTests
{
    [Fact]
    public void Filters_and_terms_are_separated()
    {
        var query = LogQuery.Parse("player:Orex_ level:error -log:debug \"threw an error\" spile near:100,64,-200~16 player:\"Енот (самка)\"");

        Assert.Equal(["Orex_", "Енот (самка)"], query.Players);
        Assert.Equal(["error"], query.Levels);
        Assert.Equal(["debug"], query.ExcludedLogs);
        Assert.Equal(["threw an error", "spile"], query.Terms);
        Assert.Equal((100, (int?)64, -200, 16), query.Near);
    }

    [Fact]
    public void Unknown_keys_stay_text()
    {
        var query = LogQuery.Parse("game:firewood");
        Assert.Equal(["game:firewood"], query.Terms);
        Assert.Empty(query.Items);
    }

    [Fact]
    public void Fts_expression_quotes_every_term()
    {
        var query = LogQuery.Parse("a\"b OR NEAR( x*");
        Assert.Equal("\"a\"\"b\"* \"OR\"* \"NEAR(\"* \"x*\"*", query.ToFtsExpression());
    }

    [Fact]
    public void Empty_query_has_no_fts_expression() => Assert.Null(LogQuery.Parse("  ").ToFtsExpression());

    [Theory]
    [InlineData("near:1,2")]
    [InlineData("near:5,6,7")]
    public void Near_accepts_two_or_three_coordinates(string text) => Assert.NotNull(LogQuery.Parse(text).Near);

    [Theory]
    [InlineData("near:abc")]
    [InlineData("near:1,2~0")]
    [InlineData("sig:x")]
    public void Invalid_filters_are_rejected(string text) => Assert.Throws<LogQueryException>(() => LogQuery.Parse(text));
}
