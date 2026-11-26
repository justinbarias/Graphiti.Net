// Ported from graphiti_core/llm_client/cache.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.LlmClient;

using System.Text.Json;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SQLitePCL;

// PORT: R5 puts the logger on the module class. LLMCache is a separate class, so the logger and the log methods are internal.
internal static partial class Cache
{
    internal static ILogger Logger => PortServices.Logger(typeof(Cache));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Corrupted cache entry for key {Key}, ignoring")]
        internal static partial void CorruptedCacheEntry(ILogger logger, string key);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Non-JSON-serializable cache value for key {Key}, skipping")]
        internal static partial void NonJsonSerializableCacheValue(ILogger logger, string key);
    }
}

/// <summary>
/// Simple SQLite + JSON cache for LLM responses.
///
/// Replaces diskcache to avoid unsafe pickle deserialization (CVE in diskcache &lt;= 5.6.3).
/// Only stores JSON-serializable data.
/// </summary>
// PORT: `sqlite3` is Microsoft.Data.Sqlite (port/DECISIONS.md). `self._conn` is the private field `conn`. Python opens
// the connection with the default isolation level and calls commit() after each write. Microsoft.Data.Sqlite runs each
// command in autocommit mode, so each write is stored when the command ends and the port has no commit() call.
internal sealed class LLMCache : IDisposable
{
    private readonly SqliteConnection conn;

    internal LLMCache(string directory)
    {
        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "cache.db");
        // PORT: Pooling is off, so that Close() releases the file as sqlite3's close() does.
        conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE IF NOT EXISTS cache (key TEXT PRIMARY KEY, value TEXT)";
        create.ExecuteNonQuery();
    }

    internal OrderedDictionary<string, object?>? Get(string key)
    {
        // PORT: Microsoft.Data.Sqlite binds named parameters only, and the SQL of upstream has `?`. The statements go
        // through SQLitePCL on the handle of the connection, so that each SQL text stays as it is (R9, J7).
        var db = Handle();
        var rc = raw.sqlite3_prepare_v2(db, "SELECT value FROM cache WHERE key = ?", out var stmt);
        SqliteException.ThrowExceptionForRC(rc, db);
        string? text;
        try
        {
            SqliteException.ThrowExceptionForRC(raw.sqlite3_bind_text(stmt, 1, key), db);
            rc = raw.sqlite3_step(stmt);
            if (rc == raw.SQLITE_DONE)
            {
                return null;
            }

            SqliteException.ThrowExceptionForRC(rc, db);

            // PORT: a NULL value gives json.loads(None), which raises TypeError.
            if (raw.sqlite3_column_type(stmt, 0) == raw.SQLITE_NULL)
            {
                throw new InvalidCastException("the JSON object must be str, bytes or bytearray, not NoneType");
            }

            text = raw.sqlite3_column_text(stmt, 0).utf8_to_string();
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }

        try
        {
            // PORT: Python returns the decoded value as it is. The skeleton signature is a dict, so a JSON text that
            // holds another type throws here.
            return Py.AsDict(PyJson.Loads(text));
        }
        catch (JsonException)
        {
            Cache.Log.CorruptedCacheEntry(Cache.Logger, key);
            return null;
        }
    }

    internal void Set(string key, OrderedDictionary<string, object?> value)
    {
        string serialized;
        try
        {
            serialized = PyJson.Dumps(value);
        }
        catch (InvalidCastException)
        {
            Cache.Log.NonJsonSerializableCacheValue(Cache.Logger, key);
            return;
        }

        var db = Handle();
        var rc = raw.sqlite3_prepare_v2(db, "INSERT OR REPLACE INTO cache (key, value) VALUES (?, ?)", out var stmt);
        SqliteException.ThrowExceptionForRC(rc, db);
        try
        {
            SqliteException.ThrowExceptionForRC(raw.sqlite3_bind_text(stmt, 1, key), db);
            SqliteException.ThrowExceptionForRC(raw.sqlite3_bind_text(stmt, 2, serialized), db);
            rc = raw.sqlite3_step(stmt);
            if (rc != raw.SQLITE_DONE)
            {
                SqliteException.ThrowExceptionForRC(rc, db);
            }
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }
    }

    // sqlite3 raises ProgrammingError for a call on a closed connection.
    private sqlite3 Handle() =>
        conn.Handle ?? throw new InvalidOperationException("Cannot operate on a closed database.");

    internal void Close()
    {
        conn.Close();
    }

    // PORT: __del__ is not ported (R16). The class holds a SqliteConnection, and analyzer CA1001 fails the build for a
    // class that owns a disposable field and is not disposable, so the class has Dispose. It does what close() does.
    public void Dispose() => Close();
}
