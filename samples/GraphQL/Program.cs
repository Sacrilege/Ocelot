﻿using GraphQL;
using GraphQL.Types;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Samples.Web;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OcelotGraphQL;

public class Hero
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class Query
{
    private readonly List<Hero> _heroes = new()
    {
        new Hero { Id = 1, Name = "R2-D2" },
        new Hero { Id = 2, Name = "Batman" },
        new Hero { Id = 3, Name = "Wonder Woman" },
        new Hero { Id = 4, Name = "Tom Pallister" }
    };

    [GraphQLMetadata("hero")]
    public Hero GetHero(int id)
    {
        return _heroes.FirstOrDefault(x => x.Id == id);
    }
}

public class GraphQlDelegatingHandler : DelegatingHandler
{
    //private readonly ISchema _schema;
    private readonly IDocumentExecuter _executer;
    private readonly IDocumentWriter _writer;

    public GraphQlDelegatingHandler(IDocumentExecuter executer, IDocumentWriter writer)
    {
        _executer = executer;
        _writer = writer;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        //try get query from body, could check http method :)
        var query = await request.Content.ReadAsStringAsync(cancellationToken);

        //if not body try query string, dont hack like this in real world..
        if (query.Length == 0)
        {
            var decoded = WebUtility.UrlDecode(request.RequestUri.Query);
            query = decoded.Replace("?query=", string.Empty);
        }

        var result = await _executer.ExecuteAsync(_ =>
        {
            _.Query = query;
        });

        var responseBody = await _writer.WriteToStringAsync(result);

        //maybe check for errors and headers etc in real world?
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody)
        };

        //ocelot will treat this like any other http request...
        return response;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var schema = Schema.For(@"
                type Hero {
                    id: Int
                    name: String
                }

                type Query {
                    hero(id: Int): Hero
                }
            ", _ =>
        {
            _.Types.Include<Query>();
        });

        OcelotHostBuilder.Create(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config
                    .SetBasePath(hostingContext.HostingEnvironment.ContentRootPath)
                    .AddJsonFile("appsettings.json", true, true)
                    .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true)
                    .AddJsonFile("ocelot.json", false, false)
                    .AddEnvironmentVariables();
            })
            .ConfigureServices(s =>
            {
                s.AddSingleton<ISchema>(schema);
                s.AddOcelot()
                    .AddDelegatingHandler<GraphQlDelegatingHandler>();
            })
            .ConfigureLogging((hostingContext, logging) =>
            {
                logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                logging.AddConsole();
            })
            .UseIISIntegration()
            .Configure(app =>
            {
                app.UseOcelot().Wait();
            })
            .Build()
            .Run();
    }
}
