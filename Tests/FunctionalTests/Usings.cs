global using Xunit;
global using System.Net;
global using System.Text;
global using System.Text.Json;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Configuration;
global using SendMeTheFuckingLog.Hosting;
global using SendMeTheFuckingLog.Storage;
global using SendMeTheFuckingLog.Monitoring;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
