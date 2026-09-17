using System;
using System.IO;
using UnityEngine;

namespace TCGCollector.Save
{
    /// <summary>
    /// Reads/writes PlayerSaveData as JSON in Application.persistentDataPath.
    /// V0.1 = local file only. A future online/cloud save can swap this class's internals
    /// without touching CollectionManager, since it only talks to Load()/Save().
    /// </summary>
    public static class SaveManager
    {
        private const string SaveFileName = "save.json";

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static PlayerSaveData Load()
        {
            try
            {
                if (!File.Exists(SavePath))
                    return new PlayerSaveData();

                string json = File.ReadAllText(SavePath);
                var data = JsonUtility.FromJson<PlayerSaveData>(json);
                return data ?? new PlayerSaveData();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Failed to load save file at '{SavePath}': {e}. Starting with a fresh save.");
                return new PlayerSaveData();
            }
        }

        public static void Save(PlayerSaveData data)
        {
            try
            {
                // Stamped on every save so CloudSyncService can tell which save is newer
                // (see PlayerSaveData.lastModifiedUtcTicks).
                data.lastModifiedUtcTicks = DateTime.UtcNow.Ticks;

                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Failed to write save file at '{SavePath}': {e}");
            }
        }

        /// <summary>Deletes the save file. Useful for testing "new player" flows from the editor.</summary>
        public static void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
