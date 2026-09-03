using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AppTime
{
    /// Persists the application library to a JSON file in the user's AppData folder.
    /// No encryption, unlike the Password Manager's vault - there's nothing sensitive
    /// here (just app names, paths and usage numbers), so plain JSON is enough.
    ///
    /// Follows the same atomic-write pattern as VaultStorage: write to a temp file
    /// first, then swap it in, so an interrupted write can never leave a corrupted
    /// library file behind - and if the primary file ever fails to read, fall back to
    /// the backup left behind by the previous save rather than losing the library.
    public static class LibraryStorage
    {
        private static readonly string LibraryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AppTime",
            "library.json");

        // Written as a side effect of SaveLibrary's atomic File.Replace - see below.
        private static readonly string LibraryBackupFilePath = LibraryFilePath + ".bak";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        /// Loads the saved library, or an empty list on first run (no file yet).
        public static List<AppEntry> LoadLibrary()
        {
            if (!File.Exists(LibraryFilePath))
            {
                return new List<AppEntry>();
            }

            try
            {
                return ReadLibraryFile(LibraryFilePath);
            }
            catch (Exception primaryEx)
            {
                if (!File.Exists(LibraryBackupFilePath))
                {
                    throw;
                }

                try
                {
                    return ReadLibraryFile(LibraryBackupFilePath);
                }
                catch (Exception)
                {
                    // Backup didn't work either - surface the original failure, since
                    // that's the file the user actually expected to be loaded.
                    throw primaryEx;
                }
            }
        }

        public static void SaveLibrary(List<AppEntry> apps)
        {
            var json = JsonSerializer.Serialize(apps, SerializerOptions);

            var directory = Path.GetDirectoryName(LibraryFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempFilePath = LibraryFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);

            if (File.Exists(LibraryFilePath))
            {
                // File.Replace performs the swap as a single atomic filesystem
                // operation, taking a backup of the previous version along the way -
                // LoadLibrary falls back to that backup if the primary file ever fails
                // to read.
                File.Replace(tempFilePath, LibraryFilePath, LibraryBackupFilePath);
            }
            else
            {
                // First save ever - nothing to atomically replace yet.
                File.Move(tempFilePath, LibraryFilePath);
            }
        }

        private static List<AppEntry> ReadLibraryFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<AppEntry>>(json) ?? new List<AppEntry>();
        }
    }
}