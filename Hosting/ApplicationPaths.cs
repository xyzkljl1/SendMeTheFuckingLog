namespace SendMeTheFuckingLog.Hosting;

// A host owns its runtime files independently of the source/content directory.
public sealed record ApplicationPaths(string Root)
{
    public string Data => Path.Combine(Root, "data");
}
