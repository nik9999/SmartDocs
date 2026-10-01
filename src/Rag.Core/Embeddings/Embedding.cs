namespace Rag.Core.Embeddings;

/// <summary>
/// Represents a numeric embedding vector.
/// </summary>
public sealed class Embedding
{
    private readonly float[] _vector;

    public float[] Vector => _vector;
    public string Model { get; }
    public int Dimensions { get; }

    public Embedding(float[] vector, string model)
    {
        if (vector is null)
            throw new ArgumentNullException(nameof(vector));

        if (vector.Length == 0)
            throw new ArgumentException("Vector must not be empty.", nameof(vector));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must not be null or empty.", nameof(model));

        _vector = (float[])vector.Clone();
        Model = model;
        Dimensions = _vector.Length;
    }
}
