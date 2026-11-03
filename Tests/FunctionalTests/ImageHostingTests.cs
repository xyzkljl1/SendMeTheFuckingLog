using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using SendMeTheFuckingLog.Models;

namespace FunctionalTests;

public sealed class ImageHostingTests
{
    [Theory]
    [InlineData("/images", "Development", "/images/")]
    [InlineData("/images/", "Development", "/images/")]
    [InlineData("https://assets.example.test", "Development", "https://assets.example.test/")]
    [InlineData("https://assets.example.test/", "Production", "https://assets.example.test/")]
    public void Image_source_comes_from_configuration_and_localization_and_hashes_are_preserved(string value, string environment, string expected)
    {
        var images = Images(value, environment);
        var paths = images.ResolveLocalized("guides/steam-drag-phantom-blade-zero-demo-{lang}.gif");
        Assert.StartsWith(expected + "guides/steam-drag-phantom-blade-zero-demo-zh.gif?v=", paths.Zh);
        Assert.StartsWith(expected + "guides/steam-drag-phantom-blade-zero-demo-en.gif?v=", paths.En);
        Assert.Equal(64, paths.Zh.Split("?v=")[1].Length);
        if (value.StartsWith('/'))
        {
            Assert.Null(images.Origin);
            Assert.Equal("/images", images.LocalPathPrefix);
        }
        else
        {
            Assert.Equal("https://assets.example.test", images.Origin);
            Assert.Null(images.LocalPathPrefix);
        }
    }

    [Theory]
    [InlineData("/images", "Production")]
    [InlineData("/images", "Staging")]
    [InlineData("//assets.example.test", "Development")]
    [InlineData("/images/../other", "Development")]
    [InlineData("/images/%2e%2e/other", "Development")]
    [InlineData("/images?query=1", "Development")]
    [InlineData("/images\\other", "Development")]
    [InlineData("images", "Development")]
    [InlineData("http://assets.example.test", "Development")]
    [InlineData("https://assets.example.test/path", "Production")]
    public void Invalid_image_bases_and_non_development_local_sources_are_rejected(string value, string environment)
        => Assert.Throws<InvalidOperationException>(() => Images(value, environment));

    [Fact]
    public async Task Local_images_load_without_preconnect_and_replacing_a_file_updates_the_same_url()
    {
        await using var world = new TestWorld();
        const string relative = "preview/wwwroot/images/preview.svg";
        const string initial = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>";
        world.Write(relative, initial);
        var app = world.Start(new() { ["ImageHosting:BaseUrl"] = "/images" });
        app.EnvironmentName = "Development";
        app.WebRoot = Path.Combine(world.Root, "preview", "wwwroot");
        using var client = app.Client();
        var page = await client.GetStringAsync("/", TestContext.Current.CancellationToken);
        Assert.Contains("rel=\"preload\" as=\"image\" href=\"/images/", page);
        Assert.DoesNotContain("rel=\"preconnect\"", page);
        var url = app.Services.GetRequiredService<HostedImageUrls>().Resolve("preview.svg");
        using var first = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl!.NoStore);
        Assert.Equal(initial, await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        const string updated = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2\" height=\"2\"><title>updated</title></svg>";
        world.Write(relative, updated);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var second = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.Headers.CacheControl!.NoStore);
        Assert.Equal(updated, await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static HostedImageUrls Images(string value, string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ImageHosting:BaseUrl"] = value
        }).Build();
        return new(configuration, new ImageEnvironment(environment));
    }

    private sealed class ImageEnvironment(string environment) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "FunctionalTests";
        public string ContentRootPath { get; set; } = TestWorld.Project;
        public string WebRootPath { get; set; } = Path.Combine(TestWorld.Project, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
