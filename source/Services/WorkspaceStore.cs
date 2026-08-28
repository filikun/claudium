using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Claudium.Models;

namespace Claudium.Services;

/// <summary>
/// Persists saved workspace directories to a JSON file under the user's local app data
/// folder, so they survive between app launches. Kept intentionally separate from any
/// UI or process-launching code.
/// </summary>
public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public WorkspaceStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Claudium",
            "workspaces.json"))
    {
    }

    public WorkspaceStore(string filePath)
    {
        _filePath = filePath;
    }

    public List<WorkspaceProfile> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<WorkspaceProfile>();
            }

            string json = File.ReadAllText(_filePath);
            List<WorkspaceProfile>? profiles = JsonSerializer.Deserialize<List<WorkspaceProfile>>(json);
            return profiles ?? new List<WorkspaceProfile>();
        }
        catch
        {
            return new List<WorkspaceProfile>();
        }
    }

    public void Save(IEnumerable<WorkspaceProfile> profiles)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(profiles.ToList(), SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
