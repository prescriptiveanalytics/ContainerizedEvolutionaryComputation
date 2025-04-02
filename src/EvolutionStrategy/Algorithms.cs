using Ai.Hgb.Common.Entities;
using Ai.Hgb.Dat.Communication;
using Ai.Hgb.Dat.Configuration;
using CEAL.Common;

namespace CEAL.EvolutionStrategy {
  // Info: https://algorithmafternoon.com/strategies/mu_plus_lambda_evolution_strategy/#:~:text=The%20(%CE%BC%2B%CE%BB)%2DEvolution%20Strategy%20maintains%20a%20population%20of,applying%20mutation%20to%20the%20parents.

  public class PlugableEvolutionStrategy : IAlgorithm<double[]> {
    public string Id { get => id; }

    public IProblem<double[]> Problem { get; set; }

    public double[] Candidate { get => candidate; set { candidate = value; } }


    // Hyperparameters
    public int Generations { get; set; }
    public int Mu { get; set; }
    public int Lambda { get; set; }
    public string Strategy { get; set; }

    // MEM (Local Search)
    public Func<double[], double[], CancellationToken, Task> LocalSearch { get; set; }

    private string id;
    private Random rnd;
    private int seed;
    private CancellationTokenSource cts;
    private object locker;
    private Task<Tuple<double[], double>> runner;

    private double[] candidate;

    public PlugableEvolutionStrategy(string id, IProblem<double[]> problem, int generations = 100, int seed = -1) {
      this.id = id;
      rnd = seed < 0 ? new Random() : new Random(seed);
      this.seed = seed;
      this.cts = new CancellationTokenSource();
      this.locker = new object();

      Problem = problem;
      Generations = generations;
    }

    public PlugableEvolutionStrategy(string id, IProblem<double[]> problem, double[] candidate, int generations = 100, int seed = -1)
      : this(id, problem, generations, seed) {
      this.candidate = candidate;
    }

    public Task<Tuple<double[], double>> Run() {
      var token = cts.Token;
      runner = Task.Run(() => {

        // start optional local search service
        //if(LocalSearch != null) {
        //  LocalSearch(candidate, fit, token);
        //}

        //return OnePlusOneES_OneHotSelfAdaptiveMutation(this.candidate);
        return OnePlusOneES_IncrementalSelfAdaptiveMutation(this.candidate);
        //return OnePlusOneES_AdaptiveMutation(this.candidate);        

      }, token);

      return runner;
    }    

    public Func<double[], Tuple<double[], double>> Execute() {
      //return OnePlusOneES_OneHotSelfAdaptiveMutation;
      return OnePlusOneES_IncrementalSelfAdaptiveMutation;
      //return OnePlusOneES_AdaptiveMutation;
    }

    // "one hot" mutation
    public Tuple<double[], double> OnePlusOneES_OneHotSelfAdaptiveMutation(double[] candidate) {
      if (candidate == null) candidate = Problem.Creator();
      double fit = Problem.Evaluator(candidate);

      // initialize individual mutation rates (i.e. per gene inside the chromosome (i.e. solution candidate))
      double[] mutationRates = Enumerable.Range(0, candidate.Length).Select(x => 0.1).ToArray();

      for (int g = 0; g < Generations && fit != 0.0; g++) {
        var candidateNew = (double[])candidate.Clone();
        double fitNew = fit;

        for (int i = 0; i < candidateNew.Length; i++) {
          var candidateMutated = (double[])candidate.Clone();
          candidateMutated[i] *= mutationRates[i];
          var fitMutated = Problem.Evaluator(candidateMutated);

          if (fitMutated < fit) {
            candidateNew[i] = candidateMutated[i];
            mutationRates[i] *= 1.5;
          }
          else {
            mutationRates[i] *= Math.Pow(1.5, -0.25); // 1.5^-(1/4)
          }
        }

        // check if combined mutations are better than original candidate
        fitNew = Problem.Evaluator(candidateNew);
        if (fitNew < fit) {
          candidate = candidateNew;
          fit = fitNew;
        }
      }

      return Tuple.Create(candidate, fit);
    }

    public Tuple<double[], double> OnePlusOneES_IncrementalSelfAdaptiveMutation(double[] candidate) {
      if (candidate == null) candidate = Problem.Creator();
      double fit = Problem.Evaluator(candidate);

      // initialize individual mutation rates (i.e. per gene inside the chromosome (i.e. solution candidate))
      double[] mutationRates = Enumerable.Range(0, candidate.Length).Select(x => 0.1).ToArray();
      int[] executionOrder = Enumerable.Range(0, candidate.Length).ToArray();

      var candidateNew = (double[])candidate.Clone();
      double fitNew = fit;

      for (int g = 0; g < Generations && fitNew > 0.0; g++) {
        executionOrder = executionOrder.Shuffle(rnd).ToArray();
        for (int i = 0; i < candidateNew.Length; i++) {
          var idx = executionOrder[i];

          var candidateMutated = (double[])candidateNew.Clone();
          candidateMutated[idx] *= mutationRates[idx];
          var fitMutated = Problem.Evaluator(candidateMutated);

          if (fitMutated < fitNew) {
            candidateNew[idx] = candidateMutated[idx];
            mutationRates[idx] *= 1.5;
            fitNew = fitMutated;
          }
          else {
            mutationRates[idx] *= Math.Pow(1.5, -0.25); // 1.5^-(1/4)
          }
        }
      }

      // sanity check
      fitNew = Problem.Evaluator(candidateNew);
      if (fitNew < fit) {
        candidate = candidateNew;
        fit = fitNew;
      }

      return Tuple.Create(candidate, fit);
    }

    public Tuple<double[], double> OnePlusOneES_AdaptiveMutation(double[] candidate) {
      if (candidate == null) candidate = Problem.Creator();
      double fit = Problem.Evaluator(candidate);

      // initialize individual mutation rates (i.e. per gene inside the chromosome (i.e. solution candidate))
      double[] mutationRates = Enumerable.Range(0, candidate.Length).Select(x => rnd.NextGaussian_BoxMuller(0.0, 1.0)).ToArray();

      var candidateNew = (double[])candidate.Clone();
      double fitNew = fit;

      for (int g = 0; g < Generations && Problem.Terminator(candidateNew, fitNew); g++) {
        for (int i = 0; i < candidateNew.Length; i++) {
          candidateNew[i] *= mutationRates[i];
        }
        fitNew = Problem.Evaluator(candidateNew);
        if (fitNew < fit) {
          candidate = candidateNew;
          fit = fitNew;
          for (int r = 0; r < mutationRates.Length; r++) mutationRates[r] *= 1.5;
        }
        else {
          for (int r = 0; r < mutationRates.Length; r++) mutationRates[r] = rnd.NextGaussian_BoxMuller(0.0, 1.0);// Math.Pow(1.5, -0.25);
        }
      }

      return Tuple.Create(candidate, fit);
    }

    public void Cancel() {
      cts.Cancel();
    }

    public object Clone() {
      throw new NotImplementedException();
    }

    public Task Pause() {
      cts.Cancel();
      return runner;
    }

    public Task Resume() {
      throw new NotImplementedException();
    }

    public Task Stop() {
      cts.Cancel();
      return runner;
    }
  }

  public class EvolutionStrategyService {
    private ISocket socket;
    private SubscriptionOptions localSearchOptions;

    public EvolutionStrategyService(ISocket socket, RoutingTable rt) {
      this.socket = socket;
    }

    public Task LocalSearch(PlugableEvolutionStrategy es, CancellationToken token) {
      return Task.Run(() => {
        socket.Subscribe<double[]>(localSearchOptions, (msg, ct) => {
          var candidate = (double[])msg.Content;
          var response = es.OnePlusOneES_IncrementalSelfAdaptiveMutation(candidate);
          socket.Publish(msg.ResponseTopic, response);
        }, token);
      }, token);
    }
  }
}
