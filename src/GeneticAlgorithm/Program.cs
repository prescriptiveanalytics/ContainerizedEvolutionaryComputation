using Ai.Hgb.Common.Entities;
using Ai.Hgb.Dat.Communication;
using Ai.Hgb.Dat.Configuration;
using CEAL.Common;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CEAL.GeneticAlgorithm {
  public class Program {
    static void Main(string[] args) {
      Run(args);
      //RunExperiment(args);
    }

    static void Run(string[] args) {
      Console.WriteLine("CEAL.GeneticAlgorithm\n");

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

        var problem = new Rastrigin(problemSize: 10);
        var ga = new PlugableGeneticAlgorithm<double[]>(parameters.Name, problem, generations: parameters.Generations, populationSize: parameters.PopulationSize, mutationRate: parameters.MutationRate);

        swatch.Start();
        var gaTask = ga.Run();
        var gaResult = gaTask.Result;
        swatch.Stop();

        Console.WriteLine("\n\n\n");
        Console.WriteLine($"Time:      {swatch.ElapsedMilliseconds / 1000.0:f4} secs");
        Console.WriteLine($"Solution:  {gaResult.Item2}");
        Console.WriteLine($"\n{string.Join(", ", gaResult.Item1)}");
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

    static void RunExperiment(string[] args) {
      Console.WriteLine("Run Experiment:\n");

      string env = "local", mode = "process";
      if (args.Length == 2) {
        env = args[0];
        mode = args[1];
      }

      string resultsFile = @"results.csv";
      var experiments = new List<Experiment>();

      string[] executions = { "single", "parallel" };
      int repetitions = 10; // 10
      int[] problemSizes = { 1000 }; // 1000 = 4kb, 100000 = 400kb
      int[] populationSizes = { 1000 }; // 1000, 10000
      int[] generations = { 1, 10, 100, 1000, 10000, 100000 }; // 1, 10, 100, 1000, 10000, 100000 // 10^4, 10^5, 10^6, 10^7, 10^8            

      //warm up
      Console.WriteLine("warming up...");
      var warumup = new PlugableGeneticAlgorithm<double[]>($"ga_warmup", new Rastrigin(1000), generations: 1000, populationSize: 1000, mutationRate: 0.1);
      var warumupResult = warumup.Run().Result;
      Console.WriteLine("starting...");


      int c = 0;
      for (int exc = 0; exc < executions.Length; exc++) { // single, parallel
        for (int ps = 0; ps < populationSizes.Length; ps++) {
          for (int pr = 0; pr < problemSizes.Length; pr++) {
            for (int g = 0; g < generations.Length; g++) {
              var problem = new Rastrigin(problemSize: problemSizes[pr]);
              var results = new List<double>();
              long evalCount = populationSizes[ps] * generations[g];
              c++;

              for (int r = 0; r < repetitions; r++) {

                var swatch = new Stopwatch();

                // configure          
                var ga = new PlugableGeneticAlgorithm<double[]>($"ga_{r}_{generations[g]}", problem, generations: generations[g], populationSize: populationSizes[ps], mutationRate: 0.1);
                Tuple<double[], double> gaResult;

                // run
                if (executions[exc] == "single") {
                  swatch.Start();
                  gaResult = ga.Run().Result;
                  swatch.Stop();
                }
                else {
                  swatch.Start();
                  gaResult = ga.RunParallel().Result;
                  swatch.Stop();
                }

                Console.WriteLine($"{executions[exc]};{r:D2};{generations[g]};{swatch.Elapsed.TotalMilliseconds}");
                results.Add(swatch.Elapsed.TotalMilliseconds);
              }
              Console.WriteLine();
              var e = new Experiment(c, $"E{c}_{evalCount}", env, mode, executions[exc], generations[g], populationSizes[ps], problemSizes[pr], evalCount, repetitions, results.Sum(), results.Average(), results.Median(), results.Min(), results.Max(), results.StandardDeviation());
              experiments.Add(e);
              if (c == 1) using (var sw = new StreamWriter(resultsFile, false)) sw.WriteLine(e.GetCSVTitleline());
              using (var sw = new StreamWriter(resultsFile, true)) {
                sw.WriteLine(e.ToCSVString());
              }
            }
          }
        }
      }


      Console.WriteLine("\n\n\nSummary begin\n");

      Console.WriteLine(experiments[0].GetCSVTitleline());
      foreach (var e in experiments) {
        Console.WriteLine(e.ToCSVString());
      }

      Console.WriteLine("\nSummary end");

      Task.Delay(1000 * 3600 * 24).Wait(); // 24h delay
    }
  }


  public class Parameters : IApplicationParametersBase, IApplicationParametersNetworking {
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("description")]
    public string Description { get; set; }
    [JsonPropertyName("gens")]
    public int Generations { get; set; }
    [JsonPropertyName("pops")]
    public int PopulationSize { get; set; }
    [JsonPropertyName("mutr")]
    public float MutationRate { get; set; }
    [JsonPropertyName("migr")]
    public float MigrationRate { get; set; }
    [JsonPropertyName("applicationParametersBase")]
    public ApplicationParametersBase ApplicationParametersBase { get; set; }
    [JsonPropertyName("applicationParametersNetworking")]
    public ApplicationParametersNetworking ApplicationParametersNetworking { get; set; }

    public override string ToString() {
      return $"{Name}: gens={Generations}, pops={PopulationSize}, mutr={MutationRate}, migr={MigrationRate}";
    }
  }

  public class Experiment {
    public int Nr { get; set; }
    public string Name { get; set; }
    public string Environment { get; set; }
    public string ExecutionMode { get; set; }
    public string ExecutionMode2 { get; set; }
    public int Generations { get; set; }
    public int PopulationSize { get; set; }
    public int ProblemSize { get; set; }
    public long EvaluationCount { get; set; }    
    public int Repetitions { get; set; }  

    public double Runtime { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double StdDev { get; set; }

    public Experiment() { }

    public Experiment(int nr, string name, string environment, string executionMode, string executionMode2, int generations, int populationSize, int problemSize, long evaluationCount, int repetitions, double runtime, double mean, double median, double min, double max, double stddev) {
      Nr = nr;
      Name = name;
      Environment = environment;
      ExecutionMode = executionMode;
      ExecutionMode2 = executionMode2;
      EvaluationCount = evaluationCount;
      Repetitions = repetitions;
      Generations = generations;
      PopulationSize = populationSize;
      ProblemSize = problemSize;

      Runtime = runtime;
      Mean = mean;
      Median = median;
      Min = min;
      Max = max;
      StdDev = stddev;
    }

    public string GetTitleline() {
      return "Nr   Name\tEnvironment/ExecutionMode/ExecutionMode2/PopulationSize\tCount\tmRuntime";
    }

    public override string ToString() {
      return $"{Nr:0000} {Name}\t{Environment}/{ExecutionMode}\t/{ExecutionMode2}/{PopulationSize}\t/{ProblemSize}\t{EvaluationCount}\t{Repetitions}\t{Mean:00000000}";
    }

    public string GetCSVTitleline() {
      return "Nr;Name;Environment;ExecutionMode;ExecutionMode2;Generations;PopulationSize;ProblemSize;EvaluationCount;Repetitions;Runtime;Mean;Median;Min;Max;StdDev";
    }

    public string ToCSVString() {
      return $"{Nr};{Name};{Environment};{ExecutionMode};{ExecutionMode2};{Generations};{PopulationSize};{ProblemSize};{EvaluationCount};{Repetitions};{Runtime};{Mean};{Median};{Min};{Max};{StdDev}";
    }
  }

}
