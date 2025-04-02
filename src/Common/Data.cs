namespace CEAL.Common.Data {
  public record DmonItem(string id, string group, int rank, string title, double value, string timestamp, string systemTimestamp);

  public record Individual(double[] values, double fit);
}
