using Microsoft.Data.Sqlite;

namespace OcrSearch.Core.Indexing;

public sealed record SearchHit(string Path, string Snippet);

public sealed class IndexStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public IndexStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        using var create = _connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS files(
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE,
                size INTEGER NOT NULL,
                mtime INTEGER NOT NULL,
                engine_id TEXT NOT NULL);
            CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(text);
            """;
        create.ExecuteNonQuery();
    }

    /// <summary>File is already indexed by this same engine and hasn't changed since.</summary>
    public bool IsUpToDate(string path, long size, long mtimeTicks, string engineId)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM files WHERE path = $path AND size = $size AND mtime = $mtime AND engine_id = $engineId";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$size", size);
            cmd.Parameters.AddWithValue("$mtime", mtimeTicks);
            cmd.Parameters.AddWithValue("$engineId", engineId);
            return cmd.ExecuteScalar() is not null;
        }
    }

    public void Upsert(string path, long size, long mtimeTicks, string engineId, string text)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using var upsert = _connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO files(path, size, mtime, engine_id) VALUES($path, $size, $mtime, $engineId)
                ON CONFLICT(path) DO UPDATE SET size = $size, mtime = $mtime, engine_id = $engineId
                RETURNING id
                """;
            upsert.Parameters.AddWithValue("$path", path);
            upsert.Parameters.AddWithValue("$size", size);
            upsert.Parameters.AddWithValue("$mtime", mtimeTicks);
            upsert.Parameters.AddWithValue("$engineId", engineId);
            var id = (long)upsert.ExecuteScalar()!;

            using var replaceText = _connection.CreateCommand();
            replaceText.Transaction = transaction;
            replaceText.CommandText = """
                DELETE FROM files_fts WHERE rowid = $id;
                INSERT INTO files_fts(rowid, text) VALUES($id, $text)
                """;
            replaceText.Parameters.AddWithValue("$id", id);
            replaceText.Parameters.AddWithValue("$text", text);
            replaceText.ExecuteNonQuery();

            transaction.Commit();
        }
    }

    public IReadOnlyList<string> GetAllPaths()
    {
        lock (_gate)
        {
            var paths = new List<string>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT path FROM files";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                paths.Add(reader.GetString(0));
            }
            return paths;
        }
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM files_fts WHERE rowid = (SELECT id FROM files WHERE path = $path);
                DELETE FROM files WHERE path = $path
                """;
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }
    }

    public int FileCount()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM files";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50)
    {
        var match = BuildMatchQuery(query);
        if (match is null)
        {
            return [];
        }

        lock (_gate)
        {
            var hits = new List<SearchHit>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT f.path, snippet(files_fts, 0, '«', '»', ' … ', 8)
                FROM files_fts JOIN files f ON f.id = files_fts.rowid
                WHERE files_fts MATCH $match
                ORDER BY rank
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$match", match);
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // Stored text keeps OCR line breaks; collapse whitespace so the snippet reads as one line.
                var snippet = string.Join(' ',
                    reader.GetString(1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                hits.Add(new SearchHit(reader.GetString(0), snippet));
            }
            return hits;
        }
    }

    /// <summary>
    /// Each query word becomes a prefix match: `print rec` → `"print"* "rec"*`
    /// (search-as-you-type). Quoting neutralizes FTS5 syntax operators in user input.
    /// </summary>
    private static string? BuildMatchQuery(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0
            ? null
            : string.Join(" ", tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\"*"));
    }

    public void Dispose() => _connection.Dispose();
}
