using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddRazorPages();
    // Keep this starter app from persisting keys outside its directory.
    builder.Services.AddDataProtection()
        .AddKeyManagementOptions(options => options.XmlRepository = new InMemoryKeyRepository());

    var app = builder.Build();
    app.MapRazorPages();
    app.Run();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

sealed class InMemoryKeyRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (elements)
            return elements.Select(element => new XElement(element)).ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (elements)
            elements.Add(new XElement(element));
    }
}
