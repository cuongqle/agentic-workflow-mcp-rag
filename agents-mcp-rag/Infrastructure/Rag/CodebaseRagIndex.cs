using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel.Text;

sealed class CodebaseRagIndex
{
    private readonly List<RagChunk> _chunks;
    private readonly EmbeddingRuntime? _embeddingRuntime;
    private readonly double _lexicalWeight;
    private readonly double _vectorWeight;

    private CodebaseRagIndex(
        List<RagChunk> chunks,
        int totalFiles,
        EmbeddingRuntime? embeddingRuntime,
        double lexicalWeight,
        double vectorWeight)
    {
        _chunks = chunks;
        TotalFiles = totalFiles;
        _embeddingRuntime = embeddingRuntime;
        _lexicalWeight = lexicalWeight;
        _vectorWeight = vectorWeight;
    }

    public int TotalFiles { get; }
    public int TotalChunks => _chunks.Count;

    public static Task<CodebaseRagIndex> BuildAsync(string repoPath)
        => BuildAsync(repoPath, RagBuildOptions.Default, contract: null);

    public static async Task<CodebaseRagIndex> BuildAsync(string repoPath, RagBuildOptions options, RepoContract? contract = null)
    {
        if (!Directory.Exists(repoPath))
        {
            return new CodebaseRagIndex(new List<RagChunk>(), 0, null, options.LexicalWeight, options.VectorWeight);
        }

        var files = RepoCodeFileScanner.EnumerateRelevantFiles(repoPath, contract).ToList();
        var chunks = new List<RagChunk>();
        var chunkTexts = new List<string>();
        var chunkSources = new List<string>();
        foreach (var file in files)
        {
            string relativePath = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
            string text = await File.ReadAllTextAsync(file);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var lines = TextChunker.SplitPlainTextLines(text, 120);
            var paragraphs = TextChunker.SplitPlainTextParagraphs(lines, 1800);
            for (int i = 0; i < paragraphs.Count; i++)
            {
                string source = $"File: {relativePath} | chunk: {i + 1}/{paragraphs.Count}";
                string chunkText = paragraphs[i];
                chunkSources.Add(source);
                chunkTexts.Add(chunkText);
            }
        }

        List<float[]?>? embeddings = null;
        EmbeddingRuntime? embeddingRuntime = null;
        if (options.UseHybridEmbeddings && !string.IsNullOrWhiteSpace(options.OpenAIApiKey))
        {
            embeddingRuntime = new EmbeddingRuntime(options.OpenAIApiKey!, options.EmbeddingModel);
            embeddings = await embeddingRuntime.CreateEmbeddingsAsync(chunkTexts);
        }

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            string text = chunkTexts[i];
            chunks.Add(new RagChunk(
                chunkSources[i],
                text,
                Tokenize(text),
                embeddings is not null && i < embeddings.Count ? embeddings[i] : null));
        }

        return new CodebaseRagIndex(chunks, files.Count, embeddingRuntime, options.LexicalWeight, options.VectorWeight);
    }

    public IReadOnlyList<RagChunkMatch> Search(string query, int limit)
    {
        if (_chunks.Count == 0 || string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return Array.Empty<RagChunkMatch>();
        }

        var queryTokens = Tokenize(query);
        float[]? queryEmbedding = _embeddingRuntime?.GetOrCreateQueryEmbedding(query);
        var scored = _chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                LexicalScore = ScoreChunkLexical(chunk, queryTokens),
                VectorScore = ScoreChunkVector(chunk, queryEmbedding)
            })
            .Select(x => new
            {
                x.Chunk,
                Score = (_lexicalWeight * x.LexicalScore) + (_vectorWeight * x.VectorScore),
                x.LexicalScore,
                x.VectorScore
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.VectorScore)
            .ThenBy(x => x.Chunk.Source, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => new RagChunkMatch(x.Chunk.Source, x.Chunk.Text, x.Score))
            .ToList();

        return scored;
    }

    private static double ScoreChunkLexical(RagChunk chunk, HashSet<string> queryTokens)
    {
        double score = 0;
        foreach (var token in queryTokens)
        {
            if (chunk.Tokens.Contains(token))
            {
                score += token.Length >= 8 ? 6 : 3;
            }
        }

        return score <= 0 ? 0 : Math.Min(1.0, score / 50.0);
    }

    private static double ScoreChunkVector(RagChunk chunk, float[]? queryEmbedding)
    {
        if (queryEmbedding is null || chunk.Embedding is null)
        {
            return 0;
        }

        return Math.Max(0, CosineSimilarity(queryEmbedding, chunk.Embedding));
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        int length = Math.Min(a.Length, b.Length);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double magA = 0;
        double magB = 0;
        for (int i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA <= 0 || magB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private static HashSet<string> Tokenize(string value)
    {
        var tokens = Regex.Matches(value, "[A-Za-z0-9_]{3,}")
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return tokens;
    }

    private readonly record struct RagChunk(string Source, string Text, HashSet<string> Tokens, float[]? Embedding);

    private sealed class EmbeddingRuntime
    {
        private static readonly HttpClient Http = new();
        private readonly string _apiKey;
        private readonly string _model;
        private readonly Dictionary<string, float[]> _queryCache = new(StringComparer.Ordinal);

        public EmbeddingRuntime(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "text-embedding-3-small" : model;
        }

        public async Task<List<float[]?>> CreateEmbeddingsAsync(IReadOnlyList<string> inputs)
        {
            var result = new List<float[]?>(inputs.Count);
            const int batchSize = 32;
            try
            {
                for (int i = 0; i < inputs.Count; i += batchSize)
                {
                    var batch = inputs.Skip(i).Take(batchSize).ToList();
                    var embeddings = await RequestEmbeddingsAsync(batch);
                    result.AddRange(embeddings);
                }
            }
            catch
            {
                // Fallback to lexical-only if embedding API is unavailable.
                return Enumerable.Repeat<float[]?>(null, inputs.Count).ToList();
            }

            if (result.Count < inputs.Count)
            {
                result.AddRange(Enumerable.Repeat<float[]?>(null, inputs.Count - result.Count));
            }

            return result;
        }

        public float[]? GetOrCreateQueryEmbedding(string query)
        {
            if (_queryCache.TryGetValue(query, out var cached))
            {
                return cached;
            }

            try
            {
                var embedding = RequestEmbeddingsAsync(new List<string> { query }).GetAwaiter().GetResult().FirstOrDefault();
                if (embedding is not null)
                {
                    _queryCache[query] = embedding;
                }
                return embedding;
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<float[]?>> RequestEmbeddingsAsync(IReadOnlyList<string> inputs)
        {
            var requestPayload = new
            {
                model = _model,
                input = inputs
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string raw = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var dataNode) || dataNode.ValueKind != JsonValueKind.Array)
            {
                return Enumerable.Repeat<float[]?>(null, inputs.Count).ToList();
            }

            var vectors = new List<float[]?>();
            foreach (var item in dataNode.EnumerateArray())
            {
                if (!item.TryGetProperty("embedding", out var embeddingNode) || embeddingNode.ValueKind != JsonValueKind.Array)
                {
                    vectors.Add(null);
                    continue;
                }

                var vector = embeddingNode.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                vectors.Add(vector);
            }

            return vectors;
        }
    }
}

readonly record struct RagChunkMatch(string Source, string Text, double Score);

readonly record struct RagBuildOptions(
    bool UseHybridEmbeddings,
    string? OpenAIApiKey,
    string EmbeddingModel,
    double LexicalWeight,
    double VectorWeight)
{
    public static readonly RagBuildOptions Default = new(
        UseHybridEmbeddings: false,
        OpenAIApiKey: null,
        EmbeddingModel: "text-embedding-3-small",
        LexicalWeight: 1.0,
        VectorWeight: 0.0);
}
