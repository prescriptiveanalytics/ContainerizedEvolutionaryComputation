using Ai.Hgb.Common.Entities;
using Ai.Hgb.Dat.Communication;
using Ai.Hgb.Dat.Configuration;
using CEAL.Common;
using System.Collections.Concurrent;

namespace CEAL.GeneticAlgorithm {

  public interface IGeneticAlgorithm<T> : IAlgorithm<T> where T : ICloneable {

    // Hyperparameter
    int PopulationSize { get; set; }
    int Generations { get; set; }
    double MutationRate { get; set; }
  }

  public class PlugableGeneticAlgorithm<T> : IGeneticAlgorithm<T> where T : ICloneable {
    public string Id { get => id; }

    // Problem
    public IProblem<T> Problem { get; set; }

    // Hyperparameters
    public int PopulationSize { get; set; }
    public int Generations { get; set; }
    public double MutationRate { get; set; }

    // Additional hyperparameters
    public int Elites { get; set; }
    public double MaximumSelectionPressure { get; set; }

    // MEM parameters
    public Func<T, Tuple<T, double>> LocalSearch { get; set; }
    public double LocalSearchRate { get; set; }

    // Island GA Parameters
    public Func<int, Tuple<T[], double[]>> Immigrate { get; set; }
    public double EpochTriggeringFailureRate { get; set; }
    public double ImmigrationRate { get; set; }
    public Func<T[], double[], CancellationToken, Task> Emigrate { get; set; }



    private string id;
    private Random rnd;
    private int seed;
    private CancellationTokenSource cts;
    private object locker;
    private Task<Tuple<T, double>> runner;

    public PlugableGeneticAlgorithm(string id, int generations = 5000, int populationSize = 1000, double mutationRate = 0.1, int seed = -1) {
      this.id = id;
      rnd = seed < 0 ? new Random() : new Random(seed);
      this.seed = seed;
      this.cts = new CancellationTokenSource();
      this.locker = new object();

      PopulationSize = populationSize;
      Generations = generations;
      MutationRate = mutationRate;
      Elites = 1;
    }

    public PlugableGeneticAlgorithm(string id, IProblem<T> problem, int generations = 5000, int populationSize = 1000, double mutationRate = 0.1, int elites = 1, int seed = -1)
      : this(id, generations, populationSize, mutationRate, seed) {
      Problem = problem;
    }

    public object Clone() {
      return new PlugableGeneticAlgorithm<T>(this.id, seed);
    }

    public Task<Tuple<T, double>> Run() {
      if (runner == null) {
        var token = cts.Token;
        runner = Task.Run(() => {
          return GA(token);
          //return OSGA(token);
        }, token);
      }

      return runner;
    }

    public Task<Tuple<T, double>> RunParallel() {
      if (runner == null) {
        var token = cts.Token;
        runner = Task.Run(() => {
          return PGA(token);          
        }, token);
      }

      return runner;
    }

    private Tuple<T, double> GA(CancellationToken token) {
      // generate initial population
      T[] population = Enumerable.Range(0, PopulationSize).Select(_ => Problem.Creator()).ToArray();
      // evaluate initial population
      double[] fit = population.Select(p => Problem.Evaluator(p)).ToArray();

      // start optional migration service
      if (Emigrate != null) {
        try { 
          Emigrate(population, fit, token); 
        } catch(Exception exc) {
          Console.WriteLine("Emigration service stopped.");
        }
      }

      // setup array for next population (clone to enable elitism in generation 0)
      T[] populationNew = population.Select(p => (T)p.Clone()).ToArray();
      double[] fitNew = new double[PopulationSize];

      // take a random solution as current best
      var bestSolution = (T)population.First().Clone();
      double bestFit = fit.First();

      double failureCount = 0;

      // run generations
      for (int g = 0; g < Generations && !Problem.Terminator(bestSolution, bestFit); g++) {
        if (token.IsCancellationRequested) break;

        // keep the first <property Elites> elements as elites
        int i = Elites;
        int candidates = 0;

        failureCount++;
        do {
          if (token.IsCancellationRequested) break;

          // select parents
          var p1Idx = Problem.Selector(fit);
          var p2Idx = Problem.Selector(fit);
          var p1 = population[p1Idx];
          var p2 = population[p2Idx];
          var f1 = fit[p1Idx];
          var f2 = fit[p2Idx];

          // generate candidate solution
          Problem.Crossover(p1, p2, populationNew[i]);
          // optional mutation
          if (rnd.NextDouble() < MutationRate) {
            Problem.Mutator(populationNew[i]);
          }

          // evaluate candidate solution
          double f = Problem.Evaluator(populationNew[i]);

          // optional local search
          if (LocalSearch != null && rnd.NextDouble() < LocalSearchRate) {
            try {
              var optimizedCandidate = LocalSearch(populationNew[i]);
              if (optimizedCandidate.Item2 < f) {
                // lamarck evolution:
                populationNew[i] = optimizedCandidate.Item1;
                // baldwin evolution:
                f = optimizedCandidate.Item2;
              }
            } catch(Exception e) {
              Console.WriteLine("Local search stopped.");
              LocalSearch = null;
            }
          }

          candidates++;
          if (f < bestFit) {
            bestFit = f;
            bestSolution = (T)populationNew[i].Clone(); // overall best
            failureCount = 0;
          }
          fitNew[i] = f;
          i++;

        } while (i < populationNew.Length);
        //Console.WriteLine("generation {0} obj {1}", g, bestFit);

        // optional immigration
        if (Immigrate != null && failureCount / Generations >= EpochTriggeringFailureRate) {
          try {
            var immigrants = Immigrate((int)(PopulationSize * ImmigrationRate));
            for (int im = 0; im < immigrants.Item1.Length; im++) {
              var immigrationIdx = rnd.Next(Elites, populationNew.Length);
              populationNew[immigrationIdx] = immigrants.Item1[im];
              fitNew[immigrationIdx] = immigrants.Item2[im];
            }
          } catch(Exception exc) {
            Console.WriteLine("Immigration stopped.");
            Immigrate = null;
          }
        }

        // swap
        var tmpPopulation = population;
        var tmpFit = fit;
        population = populationNew;
        fit = fitNew;
        populationNew = tmpPopulation;
        fitNew = tmpFit;

        // keep elite
        populationNew[0] = (T)bestSolution.Clone();
        fitNew[0] = bestFit;
      }

      //cts.Cancel(); // TODO: move or include
      return Tuple.Create(bestSolution, bestFit);
    }

    private Tuple<T, double> PGA(CancellationToken token) {
      object locker = new object();

      // generate initial population
      T[] population = Enumerable.Range(0, PopulationSize).Select(_ => Problem.Creator()).ToArray();
      // evaluate initial population
      double[] fit = population.Select(p => Problem.Evaluator(p)).ToArray();

      // start optional migration service
      if (Emigrate != null) {
        try {
          Emigrate(population, fit, token);
        }
        catch (Exception exc) {
          Console.WriteLine("Emigration service stopped.");
        }
      }

      // setup array for next population (clone to enable elitism in generation 0)
      T[] populationNew = population.Select(p => (T)p.Clone()).ToArray();
      double[] fitNew = new double[PopulationSize];

      // take a random solution as current best
      var bestSolution = (T)population.First().Clone();
      double bestFit = fit.First();

      double failureCount = 0;

      // run generations      
      for (int g = 0; g < Generations && !Problem.Terminator(bestSolution, bestFit); g++) {
        if (token.IsCancellationRequested) break;

        // keep the first <property Elites> elements as elites
        int i = Elites;
        int candidates = 0;

        failureCount++;

        var rp = Partitioner.Create(i, populationNew.Length);
        Parallel.ForEach(rp, (range, loopState) => {
          if (token.IsCancellationRequested) return;

          for (int cnt = range.Item1; cnt < range.Item2; cnt++) {
            // select parents
            var p1Idx = Problem.Selector(fit);
            var p2Idx = Problem.Selector(fit);
            var p1 = population[p1Idx];
            var p2 = population[p2Idx];
            var f1 = fit[p1Idx];
            var f2 = fit[p2Idx];

            // generate candidate solution
            Problem.Crossover(p1, p2, populationNew[cnt]);
            // optional mutation
            if (rnd.NextDouble() < MutationRate) {
              Problem.Mutator(populationNew[cnt]);
            }

            // evaluate candidate solution
            double f = Problem.Evaluator(populationNew[cnt]);

            // optional local search
            if (LocalSearch != null && rnd.NextDouble() < LocalSearchRate) {
              try {
                var optimizedCandidate = LocalSearch(populationNew[cnt]);
                if (optimizedCandidate.Item2 < f) {
                  // lamarck evolution:
                  populationNew[i] = optimizedCandidate.Item1;
                  // baldwin evolution:
                  f = optimizedCandidate.Item2;
                }
              }
              catch (Exception e) {
                Console.WriteLine("Local search stopped.");
                LocalSearch = null;
              }
            }

            candidates++;
            if (f < bestFit) {
              lock(locker) {
                bestFit = f;
                bestSolution = (T)populationNew[cnt].Clone(); // overall best
                failureCount = 0;
              }
            }
            fitNew[cnt] = f;            
          }

        });
        //Console.WriteLine("generation {0} obj {1}", g, bestFit);

        // optional immigration
        if (Immigrate != null && failureCount / Generations >= EpochTriggeringFailureRate) {
          try {
            var immigrants = Immigrate((int)(PopulationSize * ImmigrationRate));
            for (int im = 0; im < immigrants.Item1.Length; im++) {
              var immigrationIdx = rnd.Next(Elites, populationNew.Length);
              populationNew[immigrationIdx] = immigrants.Item1[im];
              fitNew[immigrationIdx] = immigrants.Item2[im];
            }
          }
          catch (Exception exc) {
            Console.WriteLine("Immigration stopped.");
            Immigrate = null;
          }
        }

        // swap
        var tmpPopulation = population;
        var tmpFit = fit;
        population = populationNew;
        fit = fitNew;
        populationNew = tmpPopulation;
        fitNew = tmpFit;

        // keep elite
        populationNew[0] = (T)bestSolution.Clone();
        fitNew[0] = bestFit;
      }

      //cts.Cancel(); // TODO: move or include
      return Tuple.Create(bestSolution, bestFit);
    }


    private Tuple<T, double> OSGA(CancellationToken token) {
      // generate initial population
      T[] population = Enumerable.Range(0, PopulationSize).Select(_ => Problem.Creator()).ToArray();
      // evaluate initial population
      double[] fit = population.Select(p => Problem.Evaluator(p)).ToArray();

      // setup array for next population (clone to enable elitism in generation 0)
      T[] populationNew = population.Select(p => (T)p.Clone()).ToArray();
      double[] fitNew = new double[PopulationSize];

      // take a random solution as current best
      var bestSolution = (T)population.First().Clone();
      double bestFit = fit.First();

      // run generations
      double currentSelectionPressure = 0.0;
      MaximumSelectionPressure = MaximumSelectionPressure > 0 ? MaximumSelectionPressure : double.MaxValue;
      for (int g = 0; g < Generations && currentSelectionPressure < MaximumSelectionPressure && !Problem.Terminator(bestSolution, bestFit); g++) {
        if (token.IsCancellationRequested) break;

        // keep the first <property Elites> elements as elites
        int i = Elites;
        int candidates = 0;

        do {
          if (token.IsCancellationRequested) break;

          // select parents
          var p1Idx = Problem.Selector(fit);
          var p2Idx = Problem.Selector(fit);
          var p1 = population[p1Idx];
          var p2 = population[p2Idx];
          var f1 = fit[p1Idx];
          var f2 = fit[p2Idx];

          // generate candidate solution
          Problem.Crossover(p1, p2, populationNew[i]);
          // optional mutation
          if (rnd.NextDouble() < MutationRate) {
            Problem.Mutator(populationNew[i]);
          }

          // evaluate candidate solution
          double f = Problem.Evaluator(populationNew[i]);

          // optional local search
          if (LocalSearch != null) {
            var optimizedCandidate = LocalSearch(populationNew[i]);
            if (optimizedCandidate.Item2 < f) {
              // lamarck evolution:
              populationNew[i] = optimizedCandidate.Item1;
              // baldwin evolution:
              f = optimizedCandidate.Item2;
            }
          }


          candidates++;
          // offspring selection
          if (f < Math.Min(f1, f2)) {
            if (f < bestFit) {
              bestFit = f;
              bestSolution = (T)populationNew[i].Clone(); // overall best
            }
            // keep offspring
            fitNew[i] = f;
            i++;
          }

          currentSelectionPressure = candidates / (double)PopulationSize;
        } while (i < populationNew.Length && currentSelectionPressure < MaximumSelectionPressure);
        //Console.WriteLine("generation {0} obj {1} sel. pres. {2:###.0}", g, bestFit, currentSelectionPressure);

        // swap
        var tmpPopulation = population;
        var tmpFit = fit;
        population = populationNew;
        fit = fitNew;
        populationNew = tmpPopulation;
        fitNew = tmpFit;

        // keep elite
        populationNew[0] = (T)bestSolution.Clone();
        fitNew[0] = bestFit;
      }

      return Tuple.Create(bestSolution, bestFit);
    }

    public static Tuple<T[], double[]> RandomEmigration(T[] migrants, double[] migrantFits, int count) {
      var emigrants = new T[count];
      var emigrantFits = new double[count];
      var rnd = new Random();

      for (int i = 0; i < count; i++) {
        int position = rnd.Next(0, migrants.Length - 1);
        emigrants[i] = migrants[position];
        emigrantFits[i] = migrantFits[position];
      }

      return Tuple.Create(emigrants, emigrantFits);
    }

    public Task Pause() {
      cts.Cancel();
      return runner;
    }

    public Task Resume() {
      cts = new CancellationTokenSource();
      return Run();
    }

    public Task Stop() {
      cts.Cancel();
      return runner;
    }
    public void Cancel() {
      cts.Cancel();
    }
  }

  public class GeneticAlgorithmService<T> where T : ICloneable {
    private ISocket socket;

    private RequestOptions immigrateOptions;
    private RequestOptions localSearchOptions;
    private SubscriptionOptions emigrateOptions;

    public GeneticAlgorithmService(ISocket socket, RoutingTable rt) {
      this.socket = socket;

      // TODO: init options based on rt
    }

    // requesting a certain number of migrant solution candidates    
    public Tuple<T[], double[]> Immigrate(int count) {
      var task = socket.RequestAsync<Tuple<T[], double[]>, int>(immigrateOptions, count);

      if (task.Wait(10000)) return task.Result;
      else throw new TimeoutException();
    }

    // publishing migrant solution candidates as response to an incoming requests    
    public Task Emigrate (T[] migrants, double[] migrantFits, CancellationToken token) {
      return Task.Run(() => {
        socket.Subscribe<int>(emigrateOptions, (msg, ct) => {
          int count = (int)msg.Content;
          var response = PlugableGeneticAlgorithm<T>.RandomEmigration(migrants, migrantFits, count);
          socket.Publish(msg.ResponseTopic, response);
        }, token);
      }, token);
    }

    // requesting an improved version of the sent solution candidate
    public Tuple<T, double> LocalSearch(T candidate) {
      var task = socket.RequestAsync<Tuple<T, double>, T>(localSearchOptions, candidate);

      if (task.Wait(10000)) return task.Result;
      else throw new TimeoutException();
    }
  }
}
