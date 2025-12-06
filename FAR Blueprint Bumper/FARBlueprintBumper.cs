using System;
using System.IO;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("FAR: Blueprint Bumper", "miniMe", "1.0.0")]
    [Description("Copies and bumps the current blueprint files for the next wipe.")]
    public class FARBlueprintBumper : CovalencePlugin
    {
        private const string BlueprintsBaseName = "player.blueprints";
        private const string DbExtension = ".db";
        private const string WalExtension = ".db-wal";

        private void Init() =>
            Puts($"Loaded successfully. Use 'blueprint.bump' command in RCON to bump the blueprint files to the next version.");

        [ConsoleCommand("blueprint.bump")]
        private void ConsoleCmdBlueprintBump(ConsoleSystem.Arg arg)
        {
            // 1. Determine the path to the server root directory (e.g., server/rust/)
            string serverStoragePath = BasePlayer.storageDirectory;

            // 2. Find the current blueprint version number
            int currentVersion = FindCurrentBlueprintVersion(serverStoragePath);

            if (currentVersion == -1)
            {
                Puts($"Error: Could not find any existing blueprint file matching the pattern '{BlueprintsBaseName}.<Number>{DbExtension}' in '{serverStoragePath}'. Aborting.");
                return;
            }

            // 3. Increment version number
            int newVersion = currentVersion + 1;

            // 4. Create paths (Streamlined: file names created only for the .db files needed for output)
            string oldDbFileName = $"{BlueprintsBaseName}.{currentVersion}{DbExtension}";
            string newDbFileName = $"{BlueprintsBaseName}.{newVersion}{DbExtension}";

            string oldDbPath = Path.Combine(serverStoragePath, oldDbFileName);
            string oldWalPath = Path.Combine(serverStoragePath, $"{BlueprintsBaseName}.{currentVersion}{WalExtension}");

            string newDbPath = Path.Combine(serverStoragePath, newDbFileName);
            string newWalPath = Path.Combine(serverStoragePath, $"{BlueprintsBaseName}.{newVersion}{WalExtension}");

            try
            {
                // 5. Copy files
                // Copy the main .db file
                File.Copy(oldDbPath, newDbPath, true);

                // Copy the associated .db-wal file
                File.Copy(oldWalPath, newWalPath, true);

                // 6. Confirmation output
                Puts($"Successfully bumped \"{oldDbFileName}\" and \"{oldDbFileName}{WalExtension}\" to the new version \"{newDbFileName}\" and \"{newDbFileName}{WalExtension}\".");
            }
            catch (FileNotFoundException)
            {
                Puts($"Error: One of the source files was not found. Please ensure both \"{oldDbFileName}\" and \"{oldDbFileName}{WalExtension}\" exist in '{serverStoragePath}'. Aborting copy.");
            }
            catch (Exception ex)
            {
                Puts($"Critical Error during file copy: {ex.Message}");
            }
        }

        /// <summary>
        /// Searches for the highest existing blueprint version number in the server storage directory.
        /// </summary>
        private int FindCurrentBlueprintVersion(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Puts($"Error: Directory not found: {directoryPath}");
                return -1;
            }

            try
            {
                // Fetch all .db files that begin with "player.blueprints."
                var files = Directory.GetFiles(directoryPath, $"{BlueprintsBaseName}.*{DbExtension}");

                int maxVersion = -1;

                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string versionString = fileName.Replace($"{BlueprintsBaseName}.", "");

                    if (int.TryParse(versionString, out int version) && version > maxVersion)
                        maxVersion = version;
                }

                return maxVersion;
            }
            catch (Exception ex)
            {
                Puts($"Error during file search: {ex.Message}");
                return -1;
            }
        }
    }
}