using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;
using Microsoft.Data.Sqlite;

namespace AnimeFolderOrganizer.Services;

public class HistoryDbService : IHistoryDbService
{
    private const string FileName = "history.db";
    private readonly string _dbPath;
    private readonly string _connectionString;

    public HistoryDbService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "AnimeFolderOrganizer");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, FileName);
        _connectionString = $"Data Source={_dbPath}";
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RenameHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                OriginalPath TEXT NOT NULL,
                NewPath TEXT NOT NULL,
                Status TEXT NOT NULL,
                Message TEXT NOT NULL
            );
            """;

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddAsync(RenameHistoryEntry entry)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RenameHistory (TimestampUtc, OriginalPath, NewPath, Status, Message)
            VALUES ($ts, $orig, $new, $status, $msg);
            """;
        cmd.Parameters.AddWithValue("$ts", entry.TimestampUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$orig", entry.OriginalPath);
        cmd.Parameters.AddWithValue("$new", entry.NewPath);
        cmd.Parameters.AddWithValue("$status", entry.Status);
        cmd.Parameters.AddWithValue("$msg", entry.Message);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<RenameHistoryEntry>> GetRecentAsync(int count)
    {
        var list = new List<RenameHistoryEntry>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TimestampUtc, OriginalPath, NewPath, Status, Message
            FROM RenameHistory
            ORDER BY Id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", count);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ts = DateTime.TryParse(reader.GetString(0), out var parsed)
                ? parsed
                : DateTime.UtcNow;

            list.Add(new RenameHistoryEntry(
                TimestampUtc: ts,
                OriginalPath: reader.GetString(1),
                NewPath: reader.GetString(2),
                Status: reader.GetString(3),
                Message: reader.GetString(4)
            ));
        }

        return list;
    }
}
