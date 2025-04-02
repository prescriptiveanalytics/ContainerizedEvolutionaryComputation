namespace CEAL.Common {

  public interface IAlgorithm<T> : ICloneable where T : ICloneable {
    string Id { get; }

    // Problem
    IProblem<T> Problem { get; set; }

    Task<Tuple<T, double>> Run();
    Task Pause();
    Task Resume();
    Task Stop();
    void Cancel();
  }
}
