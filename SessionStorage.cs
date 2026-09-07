using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AppTime
{
    /// Persists recorded app sessions to a JSON file in the user's AppData folder.
    /// Same atomic-write pattern as LibraryStorage - write to a temp file, then swap
    /// it in, so an interrupted write can't leave a corrupted sessions file behind,
    /// and fall back to the backup if the primary file ever fails to read.
    public static class SessionStorage
    {
        private static readonly string SessionsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AppTime",
            "sessions.json");

        // Written as a side effect of SaveSessions's atomic File.Replace - see below.
        private static readonly string SessionsBackupFilePath = SessionsFilePath + ".bak";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        /// Loads the saved sessions, or an empty list on first run (no file yet).
        public static List<AppSession> LoadSessions()
        {
            if (!File.Exists(SessionsFilePath))
            {
                return new List<AppSession>();
            }

            try
            {
                return ReadSessionsFile(SessionsFilePath);
            }
            catch (Exception primaryEx)
            {
                if (!File.Exists(SessionsBackupFilePath))
                {
                    throw;
                }

                try
                {
                    return ReadSessionsFile(SessionsBackupFilePath);
                }
                catch (Exception)
                {
                    // Backup didn't work either - surface the original failure, since
                    // that's the file the user actually expected to be loaded.
                    throw primaryEx;
                }
            }
        }

        public static void SaveSessions(List<AppSession> sessions)
        {
            var json = JsonSerializer.Serialize(sessions, SerializerOptions);

            var directory = Path.GetDirectoryName(SessionsFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempFilePath = SessionsFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);

            if (File.Exists(SessionsFilePath))
            {
                File.Replace(tempFilePath, SessionsFilePath, SessionsBackupFilePath);
            }
            else
            {
                // First save ever - nothing to atomically replace yet.
                File.Move(tempFilePath, SessionsFilePath);
            }
        }

        private static List<AppSession> ReadSessionsFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<AppSession>>(json) ?? new List<AppSession>();
        }
    }
}