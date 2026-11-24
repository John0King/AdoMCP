using AdoMcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

namespace AdoMcp.Tests;

#pragma warning disable MCPEXP005 // Exercise the experimental Skills wire protocol and verified reads.
public class SkillsIntegrationTests
{
    [Fact]
    public async Task StdioServerServesBundledSkillAndExistingTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // Start outside the repository to catch accidental dependencies on the working directory.
        var transport = new StdioClientTransport(new()
        {
            Command = "dotnet",
            Arguments = [typeof(DatabaseTools).Assembly.Location, "--stdio"],
            WorkingDirectory = Path.GetTempPath(),
            EnvironmentVariables = new Dictionary<string, string?> { ["ADOMCP_MODE"] = "stdio" },
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        Assert.True(client.ServerCapabilities.SupportsSkills());
        var listed = Assert.Single(await client.ListSkillsAsync(cancellationToken: timeout.Token));
        Assert.Equal("skill://adomcp/SKILL.md", listed.Uri);
        Assert.Equal("adomcp", listed.Frontmatter["name"]!.GetValue<string>());

        var skill = await client.GetSkillAsync(listed.Uri, cancellationToken: timeout.Token);
        var result = await client.ReadSkillResourceAsync(skill, skill.Uri, cancellationToken: timeout.Token);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "skills", "adomcp", "SKILL.md");
        Assert.Equal(await File.ReadAllTextAsync(bundledPath, timeout.Token), text.Text);
        Assert.Contains("execute_sql", text.Text);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(8, tools.Count);
        Assert.Contains(tools, tool => tool.Name == "query_sql");
        Assert.Contains(tools, tool => tool.Name == "execute_sql");
    }
}
#pragma warning restore MCPEXP005
