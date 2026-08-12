/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.IO;
using System.Linq;
using Nofun.Data;
using Nofun.Data.Model;
using Nofun.DynamicIcons;
using Nofun.Parser;
using Nofun.Services;
using Nofun.Plugins;
using Nofun.Util;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Nofun.UI
{
    public class GameListDocumentController : FlexibleUIDocumentController, IGameProvider
    {
        private static readonly string GameDatabaseFileName = "games.db";
        private string GameDatabasePath => $"{Application.persistentDataPath}/{GameDatabaseFileName}";

        private Button installButton;
        private VisualElement gameList;
        private GameDatabase gameDatabase;
        private TextField searchBar;

        [Header("UI")]
        [SerializeField] private VisualTreeAsset gameEntryTemplate;
        [SerializeField] private GameIconManifest gameIconManifest;
        [SerializeField] private Transform dynamicIconRendererContainer;
        [SerializeField] private GameDetailsDocumentController gameDetailsDocumentController;
        [SerializeField] private Vector2 defaultIconSize = new Vector2(180.0f, 210.0f);
        [SerializeField] private float iconPaddingReservePercentage = 6;

        [Header("Runner")]
        [SerializeField] private NofunRunner runner;

        [Inject] private ITranslationService translationService;
        [Inject] private IDialogService dialogService;
        [Inject] private ILayoutService layoutService;
        private DynamicIconsProvider dynamicIconsProvider;
        private IGameImportService gameImportService;

        private string GamePathRoot => $"{Application.persistentDataPath}/__Games";

        public string GetGamePath(string gameFileName)
        {
            return $"{GamePathRoot}/{gameFileName}";
        }

        private string GetGamePath(GameInfo gameInfo) => GetGamePath(gameInfo.GameFileName);

        public override void Awake()
        {
            base.Awake();

            if (!File.Exists(GameDatabasePath))
            {
                TextAsset asset = Resources.Load(GameDatabaseFileName) as TextAsset;

                if (asset != null)
                {
                    File.WriteAllBytes(GameDatabasePath, asset.bytes);
                }
            }

            gameDatabase = new GameDatabase(GameDatabasePath);
            gameImportService = new GameImportService(Path.Combine(Application.persistentDataPath, "__MophunCache"));
            dynamicIconsProvider = new DynamicIconsProvider(dynamicIconRendererContainer);

            Directory.CreateDirectory(GamePathRoot);
        }

        private void OnEnable()
        {
            layoutService.SetVisibility(false);

            installButton = document.rootVisualElement.Q<Button>("InstallButton");
            gameList = document.rootVisualElement.Q<VisualElement>("GameList");
            searchBar = document.rootVisualElement.Q<TextField>("SearchBar");
            installButton.clicked += OnInstallButtonClicked;
            gameDetailsDocumentController.OnGameInfoChoosen += OnGameIconClicked;
            gameDetailsDocumentController.OnGameRemovalRequested += RemoveGame;

            searchBar.RegisterValueChangedCallback(OnSearchBarContentChanged);

            gameList.RegisterCallback<GeometryChangedEvent>((_) =>
            {
                LoadGameList();
            });
        }

        private void OnDisable()
        {
            layoutService.SetVisibility(true);
            dynamicIconsProvider.Cleanup();

            installButton.clicked -= OnInstallButtonClicked;
            gameDetailsDocumentController.OnGameInfoChoosen -= OnGameIconClicked;
            gameDetailsDocumentController.OnGameRemovalRequested -= RemoveGame;
        }

        private void OnSearchBarContentChanged(ChangeEvent<string> newValue)
        {
            LoadGameList(newValue.newValue);
        }

        private void OnGameIconClicked(string gameFileName)
        {
            if (runner != null)
            {
                string gamePath = GetGamePath(gameFileName);
                if (!File.Exists(gamePath))
                {
                    dialogService.Show(Severity.Error,
                        ButtonType.OK,
                        translationService.Translate("Error"),
                        translationService.Translate("Error_Description_NoGameFileFound"),
                        null);

                    return;
                }

                runner.gameObject.SetActive(true);
                if (runner.Launch(gamePath))
                {
                    ImmediateHide();
                }
            }
        }

        private void LoadGameList(string filter = "")
        {
            var gameInfos = string.IsNullOrEmpty(filter) ? gameDatabase.AllGames : gameDatabase.GamesByKeyword(filter);
            RebuildGameList(gameInfos);
        }

        private void RebuildGameList(GameInfo[] gameInfos)
        {
            foreach (var child in gameList.Children())
            {
                if (child.userData is GameInfoEntryController controller)
                {
                    controller.OnGameInfoChoosen -= OnGameIconClicked;
                }
            }

            gameList.Clear();

            Vector2? sizeIcon = null;

            foreach (var gameInfo in gameInfos)
            {
                var gameInfoEntry = gameEntryTemplate.Instantiate();

                if (sizeIcon == null)
                {
                    float actualResolvedWidth = gameList.resolvedStyle.width * (100.0f - iconPaddingReservePercentage) / 100.0f;
                    int totalIconEachRow = Mathf.RoundToInt(actualResolvedWidth / defaultIconSize.x);
                    float actualWidth = actualResolvedWidth / totalIconEachRow;
                    float scaleFactor = actualWidth / defaultIconSize.x;

                    sizeIcon = new Vector2(actualWidth, defaultIconSize.y * scaleFactor);
                }

                var gameInfoEntryBinder = new GameInfoEntryController(gameIconManifest, dynamicIconsProvider, gameDetailsDocumentController);

                gameInfoEntryBinder.SetVisualElement(gameInfoEntry);
                gameInfoEntryBinder.BindData(gameInfo);
                gameInfoEntryBinder.OnGameInfoChoosen += OnGameIconClicked;

                gameInfoEntry.style.width = sizeIcon.Value.x;
                gameInfoEntry.style.height = sizeIcon.Value.y;

                gameList.Add(gameInfoEntry);
            }
        }

        private void RemoveGame(GameInfo gameInfo)
        {
            string gamePath = GetGamePath(gameInfo);
            if (File.Exists(gamePath))
            {
                File.Delete(gamePath);
            }

            DeleteDirectory(GetGameResourcePath(gameInfo));

            gameDatabase.RemoveGame(gameInfo);
            LoadGameList();
        }

        private void InstallGame(string path)
        {
            InstallGame(string.IsNullOrEmpty(path) ? null : new[] { path });
        }

        private string GetGameResourcePath(GameInfo gameInfo)
        {
            return Path.Combine(Application.persistentDataPath, gameInfo.Name.ToValidFileName());
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                Util.Logging.Logger.Warning(Util.Logging.LogClass.Loader,
                    $"Could not remove game resource directory '{path}': {ex}");
            }
        }

        private void InstallGame(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            string stagedPath = Path.Combine(GamePathRoot, $".{Guid.NewGuid():N}.mpn");
            string stagedResourceDirectory = Path.Combine(GamePathRoot, $".{Guid.NewGuid():N}.resources");
            GameImportResult importResult = gameImportService.ImportBundle(paths, stagedPath, stagedResourceDirectory);
            if (!importResult.Succeeded)
            {
                Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                    $"Game import failed ({importResult.ErrorCode}): {importResult.Message}\n{importResult.Exception}");
                try
                {
                    if (File.Exists(stagedPath))
                    {
                        File.Delete(stagedPath);
                    }
                    DeleteDirectory(stagedResourceDirectory);
                }
                catch (Exception cleanupException)
                {
                    Util.Logging.Logger.Warning(Util.Logging.LogClass.Loader,
                        $"Could not remove failed staged game import: {cleanupException}");
                }
                dialogService.Show(Severity.Error,
                    ButtonType.OK,
                    translationService.Translate("Error"),
                    importResult.Message,
                    null);
                return;
            }

            if (importResult.WasDecrypted)
            {
                Util.Logging.Logger.Debug(Util.Logging.LogClass.Loader,
                    $"Encrypted Mophun code was decrypted locally (source SHA-256 {importResult.SourceSha256}).");
            }

            if (importResult.WasMultipart)
            {
                Util.Logging.Logger.Debug(Util.Logging.LogClass.Loader,
                    $"Assembled multipart MPN set ({importResult.MultipartPartCount} parts).");
            }

            if (importResult.ImportedResourcePaths != null && importResult.ImportedResourcePaths.Length > 0)
            {
                Util.Logging.Logger.Debug(Util.Logging.LogClass.Loader,
                    $"Imported {importResult.ImportedResourcePaths.Length} related MPC resource(s).");
            }

            try
            {
                GameInfo gameInfo;
                using (var executableFile = File.OpenRead(stagedPath))
                using (VMGPExecutable executable = new VMGPExecutable(executableFile))
                {
                    VMMetaInfoReader metaInfoReader = executable.GetMetaInfo();
                    if (metaInfoReader == null)
                    {
                        dialogService.Show(Severity.Error,
                            ButtonType.OK,
                            translationService.Translate("Error"),
                            translationService.Translate("Error_Description_NoGameInfo"),
                            null);

                        return;
                    }

                    string titleName = metaInfoReader.Get("Title");
                    string vendor = metaInfoReader.Get("Vendor");
                    string version = metaInfoReader.Get("Program version");

                    Debug.Log($"Title: {titleName}, Vendor: {vendor}, Version: {version}");

                    if (titleName == null)
                    {
                        dialogService.Show(Severity.Error,
                            ButtonType.OK,
                            translationService.Translate("Error"),
                            translationService.Translate("Error_Description_NoGameTitle"),
                            null);

                        return;
                    }

                    int[] versionNumbers;

                    try
                    {
                        versionNumbers = (version == null)
                            ? null
                            : version.Split(".", StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                    }
                    catch (Exception ex)
                    {
                        Util.Logging.Logger.Warning(Util.Logging.LogClass.Loader,
                            $"Game metadata contains an invalid version '{version}': {ex}");
                        versionNumbers = new[] { 0, 0, 0 };
                    }

                    gameInfo = new GameInfo(titleName, vendor ?? null,
                        versionNumbers != null && versionNumbers.Length >= 1 ? versionNumbers[0] : 0,
                        versionNumbers != null && versionNumbers.Length >= 2 ? versionNumbers[1] : 0,
                        versionNumbers != null && versionNumbers.Length >= 3 ? versionNumbers[2] : 0);
                }

                GameInfo previousGameInfo = gameDatabase.FindByName(gameInfo.Name);
                bool databaseChanged;
                if (previousGameInfo != null)
                {
                    // Re-importing an already installed title replaces its private
                    // working copy. This also upgrades an older encrypted copy
                    // after the importer has normalized it.
                    gameInfo.Id = previousGameInfo.Id;
                    databaseChanged = gameDatabase.UpdateGame(gameInfo);
                }
                else
                {
                    databaseChanged = gameDatabase.AddGame(gameInfo);
                }

                if (!databaseChanged)
                {
                    dialogService.Show(Severity.Error,
                        ButtonType.OK,
                        translationService.Translate("Error"),
                        translationService.Translate("Error_Description_GameAlreadyInstalled"),
                        null);

                    return;
                }

                string gamePath = GetGamePath(gameInfo);
                string gameResourcePath = GetGameResourcePath(gameInfo);
                bool shouldMoveResources = importResult.ImportedResourcePaths != null &&
                    importResult.ImportedResourcePaths.Length > 0;
                bool gameWasMoved = false;
                bool resourcesWereMoved = false;
                try
                {
                    if (File.Exists(gamePath))
                    {
                        File.Delete(gamePath);
                    }

                    File.Move(stagedPath, gamePath);
                    gameWasMoved = true;

                    if (shouldMoveResources)
                    {
                        DeleteDirectory(gameResourcePath);
                        Directory.Move(importResult.ImportedResourceDirectory, gameResourcePath);
                        resourcesWereMoved = true;
                    }
                }
                catch (Exception ex)
                {
                    if (resourcesWereMoved)
                    {
                        DeleteDirectory(gameResourcePath);
                    }
                    if (gameWasMoved && File.Exists(gamePath))
                    {
                        try
                        {
                            File.Delete(gamePath);
                        }
                        catch
                        {
                            // Keep the original finalization exception as the user-facing error.
                        }
                    }

                    if (previousGameInfo != null)
                    {
                        gameDatabase.UpdateGame(previousGameInfo);
                    }
                    else
                    {
                        gameDatabase.RemoveGame(gameInfo);
                    }
                    throw new IOException("Could not finalize the private game copy.", ex);
                }

                dialogService.Show(Severity.Info,
                    ButtonType.OK,
                    translationService.Translate("Success"),
                    translationService.Translate("Success_Description_Install"),
                    null);

                LoadGameList();
            }
            catch (Exception ex)
            {
                Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                    $"Game metadata parsing failed for private import: {ex}");
                dialogService.Show(Severity.Error,
                    ButtonType.OK,
                    translationService.Translate("Error"),
                    translationService.Translate("Error_Description_NotMophun"),
                    null);
            }
            finally
            {
                try
                {
                    if (File.Exists(stagedPath))
                    {
                        File.Delete(stagedPath);
                    }

                    DeleteDirectory(stagedResourceDirectory);
                }
                catch (Exception ex)
                {
                    Util.Logging.Logger.Warning(Util.Logging.LogClass.Loader,
                        $"Could not remove staged game import: {ex}");
                }
            }
        }

        private void OnInstallButtonClicked()
        {
            bool permissionGranted = FilePicker.OpenPickFilesDialog(new FilterItem[]
            {
                #if UNITY_EDITOR || !UNITY_ANDROID
                new FilterItem
                {
                    name = "Mophun game",
                    spec = "mpn"
                },
                new FilterItem
                {
                    name = "Mophun resource",
                    spec = "mpc"
                }
                #else
                new FilterItem
                {
                    name = "Mophun game",
                    spec = "application/octet-stream"
                },
                new FilterItem
                {
                    name = "Mophun game (unknown type)",
                    spec = "*/*"
                }
                #endif
            }, (string[] paths) =>
            {
                if (paths != null && paths.Length > 0)
                {
                    InstallGame(paths);
                }
            });

            if (!permissionGranted)
            {
                Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                    "The system file picker did not grant access to the selected game.");
                dialogService.Show(Severity.Error,
                    ButtonType.OK,
                    translationService.Translate("Error"),
                    "Onlyfun could not access the selected file. Please choose it again.",
                    null);
            }
        }

        public void ImmediateShow()
        {
            gameObject.SetActive(true);
        }

        public void ImmediateHide()
        {
            gameObject.SetActive(false);
        }
    }
}
