using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using System.Text.Json.Nodes;

namespace FunctionalTests;

public sealed class InstanceConfigurationTests
{
    [Theory]
    [InlineData("[\"203.0.113.10\"]", 1)]
    [InlineData("[]", 0)]
    public async Task Partial_instance_overrides_preserve_defaults_and_replace_entire_lists(string clients, int count)
    {
        await using var world = new TestWorld();
        var defaults = Defaults();
        defaults["AuthorAccess"]!["AllowedClients"] = new JsonArray("198.51.100.1", "198.51.100.2");
        world.Write("site/appsettings.json", defaults.ToJsonString());
        world.Write("site/config/instance.json", """
            {"UploadPage":{"AuthorName":"Instance author"},"AuthorAccess":{"AllowedClients":CLIENTS}}
            """.Replace("CLIENTS", clients));
        using var configuration = Load(world, "Production");
        Assert.Equal("Instance author", configuration["UploadPage:AuthorName"]);
        Assert.Equal(20, configuration.GetValue<int>("Storage:MaxFileMiB"));
        Assert.Equal(count, (configuration.GetSection("AuthorAccess:AllowedClients").Get<string[]>() ?? []).Length);
        Assert.Null(configuration["AuthorAccess:AllowedClients:1"]);
    }

    [Fact]
    public async Task Development_only_overrides_its_environment_settings_and_explicit_providers_remain_last()
    {
        await using var world = new TestWorld();
        world.Write("site/appsettings.json", Defaults().ToJsonString());
        world.Write("site/.private-config/instance.json", """
            {"UploadPage":{"AuthorName":"Instance author"},"Tls":{"Domain":"private.example.net","Enabled":true},
             "ReverseProxy":{"Enabled":true},
             "AuthorAccess":{"AllowedClients":["203.0.113.10","203.0.113.11","203.0.113.12"]}}
            """);
        using var configuration = Load(world, "Development", new() { ["Storage:MaxFileMiB"] = "10" });
        Assert.Equal("Instance author", configuration["UploadPage:AuthorName"]);
        Assert.Equal("private.example.net", configuration["Tls:Domain"]);
        Assert.False(configuration.GetValue<bool>("Tls:Enabled"));
        Assert.False(configuration.GetValue<bool>("ReverseProxy:Enabled"));
        Assert.Equal(new[] { "127.0.0.1", "::1" }, configuration.GetSection("AuthorAccess:AllowedClients").Get<string[]>());
        Assert.Equal(10, configuration.GetValue<int>("Storage:MaxFileMiB"));
        Assert.Equal("/images", configuration["ImageHosting:BaseUrl"]);

        using var explicitSettings = Load(world, "Development", new()
        {
            ["Tls:Enabled"] = "true", ["ReverseProxy:Enabled"] = "true",
            ["ImageHosting:BaseUrl"] = "https://assets.example.net"
        });
        Assert.True(explicitSettings.GetValue<bool>("Tls:Enabled"));
        Assert.True(explicitSettings.GetValue<bool>("ReverseProxy:Enabled"));
        Assert.Equal("https://assets.example.net", explicitSettings["ImageHosting:BaseUrl"]);
    }

    [Fact]
    public async Task Private_development_overrides_can_replace_the_entire_local_allowlist()
    {
        await using var world = new TestWorld();
        world.Write("site/appsettings.json", Defaults().ToJsonString());
        world.Write("site/.private-config/instance.json", """
            {"AuthorAccess":{"AllowedClients":["203.0.113.10"]},
             "Development":{"AuthorAccess":{"AllowedClients":[]}}}
            """);
        using var configuration = Load(world, "Development");
        Assert.Empty(configuration.GetSection("AuthorAccess:AllowedClients").Get<string[]>() ?? []);
        Assert.Null(configuration["AuthorAccess:AllowedClients:0"]);
        Assert.Null(configuration["AuthorAccess:AllowedClients:1"]);
    }

    [Fact]
    public async Task Obsolete_development_file_does_not_override_consolidated_settings()
    {
        await using var world = new TestWorld();
        world.Write("site/appsettings.json", Defaults().ToJsonString());
        world.Write("site/appsettings.Development.json", """
            {"AuthorAccess":{"AllowedClients":["*"]},"Tls":{"Enabled":true},"ReverseProxy":{"Enabled":true}}
            """);
        using var configuration = Load(world, "Development");
        Assert.False(configuration.GetValue<bool>("Tls:Enabled"));
        Assert.False(configuration.GetValue<bool>("ReverseProxy:Enabled"));
        Assert.Equal(new[] { "127.0.0.1", "::1" }, configuration.GetSection("AuthorAccess:AllowedClients").Get<string[]>());
    }

    [Fact]
    public async Task Production_loads_only_the_deployed_instance_and_keeps_public_defaults()
    {
        await using var world = new TestWorld();
        world.Write("site/appsettings.json", Defaults().ToJsonString());
        world.Write("site/.private-config/instance.json", "not JSON; production must not read a developer checkout");
        using (var defaults = Load(world, "Production"))
        {
            Assert.Equal("Mod Author", defaults["UploadPage:AuthorName"]);
            Assert.Equal("https://your-images-project.pages.dev", defaults["ImageHosting:BaseUrl"]);
            Assert.Empty(defaults.GetSection("AuthorAccess:AllowedClients").Get<string[]>() ?? []);
        }
        world.Write("site/config/instance.json", """
            {"UploadPage":{"AuthorName":"Deployed author"},"ImageHosting":{"BaseUrl":"https://assets.example.net"},
             "Tls":{"Enabled":true},"AuthorAccess":{"AllowedClients":["203.0.113.10"]},
             "Development":{"AuthorAccess":{"AllowedClients":["*"]}}}
            """);
        using var deployed = Load(world, "Production");
        Assert.Equal("Deployed author", deployed["UploadPage:AuthorName"]);
        Assert.Equal("https://assets.example.net", deployed["ImageHosting:BaseUrl"]);
        Assert.Equal(900, deployed.GetValue<int>("TrafficMonitor:MonthlyTrafficLimitGB"));
        Assert.True(deployed.GetValue<bool>("Tls:Enabled"));
        Assert.Equal(new[] { "203.0.113.10" }, deployed.GetSection("AuthorAccess:AllowedClients").Get<string[]>());
    }

    [Theory]
    [InlineData("{\"Storage\":{\"MaxFileMiB\":\"wrong type\"}}")]
    [InlineData("{\"Storage\":{\"MaxFileMB\":20}}")]
    [InlineData("[]")]
    [InlineData("not JSON")]
    public async Task Invalid_instance_configuration_fails_instead_of_using_the_default_author(string patch)
    {
        await using var world = new TestWorld();
        world.Write("site/appsettings.json", Defaults().ToJsonString());
        world.Write("site/.private-config/instance.json", patch);
        Assert.ThrowsAny<Exception>(() => { using var _ = Load(world, "Development"); });
    }

    [Fact]
    public void Explicit_false_and_zero_are_retained_and_case_insensitive_keys_do_not_duplicate()
    {
        var settings = InstanceConfiguration.Parse("""{"Tls":{"Enabled":true},"Storage":{"MaxFileMiB":20}}""");
        InstanceConfiguration.Merge(settings, InstanceConfiguration.Parse("""{"tls":{"enabled":false},"Storage":{"MaxFileMiB":0}}"""));
        Assert.False(settings["Tls"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(0, settings["Storage"]!["MaxFileMiB"]!.GetValue<int>());
        Assert.Equal(2, settings.Count);
    }

    private static JsonObject Defaults() => InstanceConfiguration.Read(Path.Combine(TestWorld.Project, "appsettings.json"));

    private static ConfigurationManager Load(TestWorld world, string environment, Dictionary<string, string?>? explicitValues = null)
    {
        var root = Path.Combine(world.Root, "site");
        var configuration = new ConfigurationManager();
        configuration.SetBasePath(root);
        configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        configuration.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);
        if (explicitValues is not null) configuration.AddInMemoryCollection(explicitValues);
        try
        {
            InstanceConfiguration.Apply(configuration, new SiteEnvironment(root, environment));
            return configuration;
        }
        catch
        {
            ((IDisposable)configuration).Dispose();
            throw;
        }
    }

    private sealed class SiteEnvironment(string root, string environment) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "FunctionalTests";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
