namespace ProposalSystem.Api.Tests;

public sealed class StartupTests
{
    [Fact]
    public void A_fresh_copy_gets_its_database_folder_created_next_to_the_application()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fresh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var cs = Program.PrepareSqlite("Data Source=App_Data/proposals.db", root);

            Assert.True(Directory.Exists(Path.Combine(root, "App_Data")));
            Assert.Contains(Path.Combine(root, "App_Data", "proposals.db"), cs, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Absolute_and_in_memory_paths_are_left_as_they_are()
    {
        var absolute = Path.Combine(Path.GetTempPath(), $"abs-{Guid.NewGuid():N}", "x.db");
        Assert.Contains(absolute, Program.PrepareSqlite($"Data Source={absolute}", "/ignored"), StringComparison.Ordinal);
        Assert.Contains(":memory:", Program.PrepareSqlite("Data Source=:memory:", "/ignored"), StringComparison.Ordinal);
    }
}
