using AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;

namespace AlegacyWebPanel.ServerLogs.UnitTests;

public sealed class ParsingTests
{
    [Fact]
    public void Header_parses_seconds_and_milliseconds_as_utc()
    {
        Assert.True(LogLineParser.TryParseHeader("27.9.2026 22:00:06 [Notification] Server logger started.", out var ts, out var level, out var message));
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 6, TimeSpan.Zero).ToUnixTimeMilliseconds(), ts);
        Assert.Equal("Notification", level);
        Assert.Equal("Server logger started.", message);

        Assert.True(LogLineParser.TryParseHeader("7.10.2026 03:04:05.07 [VerboseDebug] x", out var debugTs, out _, out _));
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 3, 4, 5, 70, TimeSpan.Zero).ToUnixTimeMilliseconds(), debugTs);
    }

    [Theory]
    [InlineData("   at Vintagestory.Server.Foo()")]
    [InlineData("ModID                      Version")]
    [InlineData("32.13.2026 22:00:06 [Error] impossible date")]
    public void Non_header_lines_are_rejected(string line) =>
        Assert.False(LogLineParser.TryParseHeader(line, out _, out _, out _));

    [Fact]
    public void Continuation_lines_join_the_entry_above_and_leading_ones_are_returned_separately()
    {
        var chunk = LogLineParser.Parse("main", string.Join('\n',
            "   at Leftover.FromPreviousChunk()",
            "27.9.2026 22:00:16 [Error] [GondolaCableCar] Exception: Index was outside the bounds of the array.",
            "   at A.B()",
            "   at C.D()",
            "27.9.2026 22:00:17 [Warning] Server overloaded. A tick took 1234ms to complete.",
            ""));

        Assert.Equal("   at Leftover.FromPreviousChunk()", chunk.LeadingContinuation);
        Assert.Equal(2, chunk.Entries.Count);
        Assert.Equal("GondolaCableCar", chunk.Entries[0].Source);
        Assert.Equal("   at A.B()\n   at C.D()", chunk.Entries[0].Extra);
        Assert.Null(chunk.Entries[1].Extra);
        Assert.Equal("Server overloaded. A tick took Nms to complete.", chunk.Entries[1].Signature);
    }

    [Fact]
    public void Informational_levels_have_no_signature()
    {
        var chunk = LogLineParser.Parse("main", "27.9.2026 22:00:07 [Event] It begins...\n");
        Assert.Null(chunk.Entries[0].Signature);
    }

    [Fact]
    public void Signature_replaces_positions_numbers_and_ids()
    {
        Assert.Equal(
            "At position <pos> for block aculinaryartillery:spile-copper-east a BlockEntitySpile threw an error",
            MessageSignature.Normalize("At position 480668, 166, 527807 for block aculinaryartillery:spile-copper-east a BlockEntitySpile threw an error"));
        Assert.Equal("item <id> slot N", MessageSignature.Normalize("item 6dea5fc6-3c8f-11f0-8e08-525400122165 slot 20"));
        Assert.Equal(
            "[StackSyncSafety] Join-scan failed for <name>: Object reference not set",
            MessageSignature.Normalize("[StackSyncSafety] Join-scan failed for MaxMeals: Object reference not set"));
    }

    [Fact]
    public void Audit_other_party_is_the_attacker_killer_or_receiver()
    {
        Assert.Equal("Hikkalibur", AuditParser.Parse("nPOCTAK at 5, 6, 7 got 5.5/5.5 damage bluntattack x:y by Hikkalibur ♣").Other);
        Assert.Equal("Пепельный Ползун", AuditParser.Parse("EDoss умер. Сообщение о смерти: Игрок EDoss убит Пепельный Ползун").Other);
        Assert.Null(AuditParser.Parse("Игрок Urlixis умер.").Other);
        Assert.Equal("Warlord", AuditParser.Parse("GrEmperor Gave to Warlord 1xgame:gear-rusty at 1, 2, 3.").Other);
    }

    [Fact]
    public void Victim_of_a_player_kill_comes_from_the_killers_last_hit()
    {
        var chunk = LogLineParser.Parse("audit", string.Join('\n',
            "23.9.2026 14:13:51 [Audit] nPOCTAK at 1, 2, 3 got 5.5/5.5 damage bluntattack x:y by Hikkalibur ♣",
            "23.9.2026 14:13:53 [Audit] Player Hikkalibur ♣ killed game:player at 1, 2, 3",
            "23.9.2026 14:20:00 [Audit] Player Hikkalibur ♣ killed game:player at 1, 2, 3",
            ""));
        Assert.Equal("nPOCTAK", chunk.Entries[1].Audit!.Other);
        Assert.Null(chunk.Entries[2].Audit!.Other); // too long after the last hit
    }

    [Theory]
    [InlineData("LittleJester left clicked slot 20 in backpack-6dea. Before: (mouse: empty)", "click", "LittleJester", null, null)]
    [InlineData("Енот (самка) Took 6xgame:fruit-cranberry from game:fruitingbush-wild-cranberry-free at 480668, 166, 527807.", "take", "Енот (самка)", "game:fruit-cranberry", 480668)]
    [InlineData("Missgame took 2x game:egg-chicken-raw from game:henbox at 1, 2, 3", "take", "Missgame", "game:egg-chicken-raw", 1)]
    [InlineData("DezertirN Put 8xgame:firewood into Ground storage at 10, 20, 30.", "put", "DezertirN", "game:firewood", 10)]
    [InlineData("Orex_ Put 1xgame:workitem-copper on to Anvil at 5, 6, 7.", "put", "Orex_", "game:workitem-copper", 5)]
    [InlineData("Player [RAC] RiddlE9 killed albase:sand-skeleton at 1, 2, 3", "kill", "RiddlE9", "albase:sand-skeleton", 1)]
    [InlineData("[AKV] LittleJester killed game:drifter-normal at 4, 5, 6", "kill", "LittleJester", "game:drifter-normal", 4)]
    [InlineData("command for Orex_ /we g s 5", "command", "Orex_", "/we", null)]
    [InlineData("Handling command for Verius /land claim load 1", "command", "Verius", "/land", null)]
    [InlineData("Rejected player position update for RiddlE9. Client sent 1,2,3, server pos was XYZ", "position-rejected", "RiddlE9", null, null)]
    [InlineData("Player nPOCTAK sent a packet to chisel at 1, 2, 3 but is too far away. Rejected.", "packet-rejected", "nPOCTAK", null, 1)]
    [InlineData("Orex_ placed a chute at 7, 8, 9", "place", "Orex_", "chute", 7)]
    [InlineData("nPOCTAK broke container game:trunk-north at 1, 2, 3 dropped: 1x game:soil", "break", "nPOCTAK", "game:trunk-north", 1)]
    [InlineData("EDoss умер. Сообщение о смерти: Игрок EDoss убит Пепельный Ползун", "death", "EDoss", null, null)]
    [InlineData("Teleporting player [AKV] YattochkaN from 1, 2, 3 to x=4.5, y=6, z=7.5", "teleport", "YattochkaN", null, 1)]
    [InlineData("Client Verius disconnected.", "leave", "Verius", null, null)]
    [InlineData("LittleJester joined.", "join", "LittleJester", null, null)]
    [InlineData("Player Hikkalibur ♣ killed game:player at 1, 2, 3", "kill", "Hikkalibur", "game:player", 1)]
    [InlineData("nPOCTAK at 5, 6, 7 got 5.5/5.5 damage bluntattack ancientarmory:aa-blade by Hikkalibur ♣", "damage", "nPOCTAK", "ancientarmory:aa-blade", 5)]
    [InlineData("Something the parser has never seen", "other", null, null, null)]
    public void Audit_lines_are_classified(string message, string action, string? player, string? item, int? x)
    {
        var info = AuditParser.Parse(message);
        Assert.Equal(action, info.Action);
        Assert.Equal(player, info.Player);
        Assert.Equal(item, info.Item);
        Assert.Equal(x, info.X);
    }
}
