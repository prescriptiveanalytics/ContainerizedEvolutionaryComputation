using CEAL.Common;
using CEAL.EvolutionStrategy;
using CEAL.GeneticAlgorithm;
using System.Diagnostics;

namespace CEAL.Runner {
  public class Program {
    static void Main(string[] args) {
      Console.WriteLine("CEAL.Runner\n");
      var swatch = new Stopwatch();

      var problem = new Rastrigin(problemSize: 10);
      var ga1 = new PlugableGeneticAlgorithm<double[]>("ga1", problem, generations: 10000, populationSize: 1000, mutationRate: 0.25, elites: 1);
      var es = new PlugableEvolutionStrategy("es1", problem, generations: 5);
      ga1.LocalSearchRate = 0.001;
      ga1.LocalSearch = es.Execute();

      swatch.Start();
      var ga1Task = ga1.Run();
      var ga1Result = ga1Task.Result;
      swatch.Stop();

      Console.WriteLine("\n\n\n");
      Console.WriteLine($"Time:      {swatch.ElapsedMilliseconds/1000.0:f4} secs");
      Console.WriteLine($"Solution:  {ga1Result.Item2}");
      Console.WriteLine($"\n{string.Join(", ", ga1Result.Item1)}");
    }
  }
}
