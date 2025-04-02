using Ai.Hgb.Common.Entities;
using Ai.Hgb.Dat.Communication;
using Ai.Hgb.Dat.Configuration;
using CEAL.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CEAL.EvolutionStrategy {
  public class Program {
    static void Main(string[] args) {
      Run(args);
    }

    static void Run(string[] args) {
      Console.WriteLine("CEAL.EvolutionStrategy\n");

      // load internal config
      var internalConfig = Parser.Parse<SocketConfiguration>("./configurations/CEAL.GeneticAlgorithm.yml");
      Parameters parameters = null;
      RoutingTable routingTable = null;

      // parse parameters and routing table
      try {
        if (args.Length > 0) parameters = JsonSerializer.Deserialize<Parameters>(args[0]);
        if (args.Length > 1) routingTable = JsonSerializer.Deserialize<RoutingTable>(args[1]);

        Console.WriteLine("Parameters:");
        Console.WriteLine("-------");
        Console.WriteLine(parameters);
        Console.WriteLine("-------\n");
      }
      catch (Exception ex) { Console.WriteLine(ex.Message); }

      // setup socket and converter
      var address = new HostAddress(parameters.ApplicationParametersNetworking.HostName, parameters.ApplicationParametersNetworking.HostPort);
      var converter = new JsonPayloadConverter();
      var cts = new CancellationTokenSource();
      ISocket socket = null;
      var swatch = new Stopwatch();

      // main
      try {
        var rndName = "_" + new Guid().ToString();
        socket = new MqttSocket(parameters.Name + rndName, parameters.Name, address, converter, connect: true);

        // setup publishing routes
        var routes = routingTable.Routes.Where(x => x.Source.Id == parameters.Name && x.SourcePort.Id == "imp");

        var problem = new Rastrigin(problemSize: 1000);
        var es = new PlugableEvolutionStrategy(parameters.Name, problem, generations: parameters.Generations);
        es.Mu = parameters.Mu;
        es.Lambda = parameters.Lambda;
        es.Strategy = parameters.Strategy;
       
        swatch.Start();
        var esTask = es.Run();
        var esResult = esTask.Result;
        swatch.Stop();

        Console.WriteLine("\n\n\n");
        Console.WriteLine($"Time:      {swatch.ElapsedMilliseconds / 1000.0:f4} secs");
        Console.WriteLine($"Solution:  {esResult.Item2}");
        Console.WriteLine($"\n{string.Join(", ", esResult.Item1)}");
      }
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
      }
      finally {
        socket.Disconnect();
        socket = null;
        Task.Delay(3000).Wait(); // wait a minute...
      }

    }
  }


  public class Parameters : IApplicationParametersBase, IApplicationParametersNetworking {
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("description")]
    public string Description { get; set; }
    [JsonPropertyName("gens")]
    public int Generations { get; set; }
    [JsonPropertyName("mu")]
    public int Mu { get; set; }
    [JsonPropertyName("lambda")]
    public int Lambda { get; set; }
    [JsonPropertyName("strategy")]
    public string Strategy { get; set; }
    [JsonPropertyName("applicationParametersBase")]
    public ApplicationParametersBase ApplicationParametersBase { get; set; }
    [JsonPropertyName("applicationParametersNetworking")]
    public ApplicationParametersNetworking ApplicationParametersNetworking { get; set; }

    public override string ToString() {
      return $"{Name}: gens={Generations}, mu={Mu}, lambda={Lambda}, strategy={Strategy}";
    }
  }
}
