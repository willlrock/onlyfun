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
using Nofun.Driver.Unity.Audio;
using Nofun.Driver.Unity.Graphics;
using Nofun.Driver.Unity.Input;
using Nofun.Driver.Unity.Time;
using Nofun.Driver.Unity.UI;
using Nofun.Parser;
using Nofun.Util.Unity;
using Nofun.VM;
using System.IO;
using UnityEngine;

using System.Threading;
using Nofun.UI;
using Nofun.Settings;
using System.Collections;
using Nofun.PIP2;
using Nofun.Services;
using VContainer;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Nofun
{
    public class NofunRunner : MonoBehaviour
    {
        [Header("Drivers")]
        [SerializeField] private InputDriver inputDriver;
        [SerializeField] private AudioDriver audioDriver;
        [SerializeField] private GraphicDriver graphicDriver;
        [SerializeField] private UIDriver uiDriver;
        private TimeDriver timeDriver;

        [Header("UI")]
        [SerializeField] private SettingDocumentController settingDocument;
        [SerializeField] private GameDetailsDocumentController gameDetailsDocument;
        [SerializeField] private GameListDocumentController gameListDocumentController;
        [SerializeField] private float waitTimeBeforeNotifyUserOfLLVM = 0.2f;
        [SerializeField] private float llvmPreparationTimeout = 30.0f;

        [Header("Settings")]
        [Range(1, 60)][SerializeField] private int fpsLimit = 30;
        [SerializeField] private string executableFilePath = "E:\\spacebox.mpn";
        [SerializeField] private bool immediatelyRun = false;
        [SerializeField] private bool enableLLVM = false;

        private VMGPExecutable executable;
        private VMSystem system;
        private GameSettingsManager settingManager;
        private Thread systemThread;
        private bool started = false;
        private bool failed = false;
        private bool settingActive = false;
        private bool launchRequested = false;

        private volatile bool llvmPrepared = false;
        private int llvmPreparingDialogId = -1;
        private static FileLogTarget fileLogTarget;
        private volatile bool isDestroying = false;

        [Inject] private ScreenManager screenManager;
        [Inject] private IDialogService dialogService;
        [Inject] private ITranslationService translationService;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll", EntryPoint = "SetWindowText")]
        public static extern bool SetWindowText(System.IntPtr hwnd, System.String lpString);
        [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
        public static extern System.IntPtr GetActiveWindow();

        private System.IntPtr currentWindow;
#endif

        [Inject]
        public void Construct(ScreenManager injectScreenManager)
        {
            screenManager = injectScreenManager;
        }

        private void SetupLogger()
        {
            Util.Logging.Logger.AddTarget(new UnityLogTarget());
            if (fileLogTarget == null)
            {
                fileLogTarget = new FileLogTarget(Application.persistentDataPath);
                Util.Logging.Logger.AddTarget(fileLogTarget);
            }
        }

        private void OnDestroy()
        {
            isDestroying = true;
            settingDocument.Finished -= FinishSettingDocument;
            settingDocument.ExitGameRequested -= HandleExitGame;
            if (StopAndJoinSystemThread())
            {
                Reset();
            }
        }

        private void FinishSettingDocument(bool isCancel)
        {
            if (!isCancel)
            {
                GameSetting? setting = settingManager.Get(system.GameName);
                if (setting != null)
                {
                    graphicDriver.FpsLimit = setting.Value.fps;

                    if (setting.Value.screenMode != ScreenMode.Fullscreen)
                    {
                        screenManager.ScreenOrientation = setting.Value.orientation;
                    }
                }
            }
            else
            {
                if (!started)
                {
                    // Cancel launch
                    launchRequested = false;
                    gameListDocumentController.ImmediateShow();
                }
            }

            settingActive = false;
            JobScheduler.Paused = false;
        }

        private IEnumerator ShowGameListDelay()
        {
            yield return new WaitForSeconds(0.2f);
            gameListDocumentController.ImmediateShow();
        }

        private void HandleExitGame()
        {
            settingActive = false;
            JobScheduler.Paused = false;

            system?.Stop();
        }

        private void OpenGameSetting()
        {
            settingDocument.Show(showExitGameButton: started);

            settingActive = true;
            JobScheduler.Paused = true;
        }

        public void OnGameScreenCogButtonPressed()
        {
            OpenGameSetting();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            SetupLogger();

            settingManager = new(Application.persistentDataPath);
            timeDriver = new TimeDriver();

            gameDetailsDocument.Setup(settingManager, gameListDocumentController);
        }

        private void Start()
        {
            Stream gameStream = null;

#if UNITY_EDITOR
            string targetExecutable = executableFilePath;
#else
            string targetExecutable = null;
#endif

#if !UNITY_EDITOR && NOFUN_PRODUCTION
#if UNITY_ANDROID
            // Normal Android launches always open the library. Imports are copied to private
            // storage by GameImportService before the emulator sees them.
            return;
#else
            string[] cmdLines = System.Environment.GetCommandLineArgs();

            if (cmdLines.Length >= 2)
            {
                targetExecutable = cmdLines[1];
            }
            else
            {
                return;
            }
#endif
#endif

#if UNITY_EDITOR
            if (immediatelyRun)
            {
#endif

#if UNITY_EDITOR || !UNITY_ANDROID
                gameStream = new FileStream(targetExecutable, FileMode.Open, FileAccess.Read,
                    FileShare.Read);
#endif

                launchRequested = true;

                if (StartGameImpl(gameStream, targetExecutable))
                {
                    gameListDocumentController.ImmediateHide();
                }
#if UNITY_EDITOR
            }
#endif
        }

        public bool Launch(string gamePath)
        {
            if (!StopAndJoinSystemThread())
            {
                dialogService.Show(Severity.Error, ButtonType.OK,
                    null,
                    "The previous game is still stopping. Please try again.",
                    null);
                gameListDocumentController.ImmediateShow();
                return false;
            }

            Reset();

            executableFilePath = gamePath;
            launchRequested = true;

            try
            {
                FileStream stream = new FileStream(gamePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return StartGameImpl(stream, gamePath);
            }
            catch (System.Exception ex)
            {
                HandleLoadFailure(null, ex, "opening the private game file");
                return false;
            }
        }

        private void Reset()
        {
            settingDocument.Finished -= FinishSettingDocument;
            settingDocument.ExitGameRequested -= HandleExitGame;

            if (system != null)
            {
                system.Dispose();
                system = null;
            }

            started = false;
            failed = false;
            launchRequested = false;

            if (executable != null)
            {
                executable.Dispose();
                executable = null;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SetWindowText(currentWindow, $"nofun");
#endif
        }

        private bool StopAndJoinSystemThread()
        {
            try
            {
                system?.Stop();
            }
            catch (System.Exception ex)
            {
                Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                    $"Requesting VM worker stop failed: {ex}");
            }

            if (systemThread != null && systemThread.IsAlive && Thread.CurrentThread != systemThread &&
                !systemThread.Join(2000))
            {
                Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                    "VM worker did not stop within two seconds; its resources were left intact.");
                return false;
            }

            systemThread = null;
            return true;
        }

        private void HandleLoadFailure(Stream gameStream, System.Exception ex, string stage)
        {
            Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                $"Game load failed while {stage}: {ex}");

            try
            {
                system?.Dispose();
                system = null;

                if (executable != null)
                {
                    executable.Dispose();
                    executable = null;
                }
                else
                {
                    gameStream?.Dispose();
                }
            }
            catch (System.Exception cleanupException)
            {
                Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                    $"Game cleanup failed after the original load error: {cleanupException}");
            }

            failed = true;
            launchRequested = false;
            settingActive = false;
            JobScheduler.Paused = false;
            settingDocument.Finished -= FinishSettingDocument;
            settingDocument.ExitGameRequested -= HandleExitGame;
            gameListDocumentController.ImmediateShow();

            dialogService.Show(Severity.Error, ButtonType.YesNo,
                null,
                translationService.Translate("Error_Description_GameNotCompatible") + "\n\nDo you want to save the error logs?",
                (int result) =>
                {
                    if (result == 0)
                    {
                        string logPath = System.IO.Path.Combine(Application.persistentDataPath, "onlyfun.log");
                        Nofun.Plugins.FilePicker.ExportLog(logPath, null);
                    }
                });
        }

        public bool StartGameImpl(Stream gameStream, string targetExecutable)
        {
            try
            {
                executable = new VMGPExecutable(gameStream);
                system = new VMSystem(executable, new VMSystemCreateParameters(graphicDriver, inputDriver, audioDriver, timeDriver, uiDriver,
                    Application.persistentDataPath, targetExecutable, enableLLVM));

                settingDocument.Setup(settingManager, system.GameName,
                    GameProfileResolver.Resolve(system.GameName, system.Executable));

                settingDocument.Finished -= FinishSettingDocument;
                settingDocument.ExitGameRequested -= HandleExitGame;
                settingDocument.Finished += FinishSettingDocument;
                settingDocument.ExitGameRequested += HandleExitGame;

                if (settingManager.Get(system.GameName) == null)
                {
                    OpenGameSetting();
                }

                VMSystem runningSystem = system;
                systemThread = new Thread(() =>
                {
                    System.Exception failure = null;
                    try
                    {
                        runningSystem.PostInitialize();
                        llvmPrepared = true;

                        while (!runningSystem.ShouldStop)
                        {
                            runningSystem.Run();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        failure = ex;
                        Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                            $"VM initialization or execution failed: {ex}");
                    }
                    finally
                    {
                        llvmPrepared = true;
                        if (!isDestroying)
                        {
                            JobScheduler.Instance.RunOnUnityThread(() =>
                                HandleSystemThreadFinished(runningSystem, failure));
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "Onlyfun VM"
                };
            }
            catch (System.Exception ex)
            {
                HandleLoadFailure(gameStream, ex, "creating the executable and VM");
                return false;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            currentWindow = GetActiveWindow();
#endif
            return true;
        }

        private void HandleSystemThreadFinished(VMSystem finishedSystem, System.Exception failure)
        {
            if (system != finishedSystem || isDestroying)
            {
                return;
            }

            if (llvmPreparingDialogId >= 0)
            {
                dialogService.CloseBlocked(llvmPreparingDialogId);
                llvmPreparingDialogId = -1;
            }

            Reset();
            StartCoroutine(ShowGameListDelay());

            if (failure != null)
            {
                dialogService.Show(Severity.Error, ButtonType.YesNo,
                    null,
                    translationService.Translate("Error_Description_GameNotCompatible") + "\n\nDo you want to save the error logs?",
                    (int result) =>
                    {
                        if (result == 0)
                        {
                            string logPath = System.IO.Path.Combine(Application.persistentDataPath, "onlyfun.log");
                            Nofun.Plugins.FilePicker.ExportLog(logPath, null);
                        }
                    });
            }
        }

        private IEnumerator InitializeGameRun()
        {
            GameSetting setting;
            try
            {
                GameSetting? storedSetting = settingManager.Get(system.GameName);
                setting = storedSetting ?? GameProfileResolver.Resolve(system.GameName, system.Executable);

                system.GameSetting = setting;

                // Change orientation first
                screenManager.ScreenOrientation = setting.orientation;

                graphicDriver.Initialize((setting.screenMode == ScreenMode.CustomSize) ?
                    new Vector2(setting.screenSizeX, setting.screenSizeY) :
                    Vector2.zero, setting.enableSoftwareScissor);

                graphicDriver.FpsLimit = Mathf.Clamp(setting.fps, 1, 120);
                if (setting.cpuBackend == CPUBackend.LLVM)
                {
                    llvmPrepared = false;
                    llvmPreparingDialogId = -1;
                }

                systemThread.Start();
            }
            catch (System.Exception ex)
            {
                HandleLoadFailure(null, ex, "initializing graphics and starting the VM worker");
                yield break;
            }

            if (setting.cpuBackend == CPUBackend.LLVM)
            {
                float elapsedTime = 0.0f;

                while (!llvmPrepared && elapsedTime < llvmPreparationTimeout)
                {
                    if (waitTimeBeforeNotifyUserOfLLVM <= elapsedTime && llvmPreparingDialogId < 0)
                    {
                        llvmPreparingDialogId = dialogService.OpenBlocked(Severity.Info,
                            translationService.Translate("Info_Title_PreparingLLVM"),
                            translationService.Translate("Info_Description_PreparingLLVM"));
                    }

                    yield return null;

                    elapsedTime += Time.deltaTime;
                }

                if (!llvmPrepared)
                {
                    Util.Logging.Logger.Error(Util.Logging.LogClass.Loader,
                        $"LLVM initialization exceeded {llvmPreparationTimeout:0.0} seconds.");
                    system?.Stop();
                    launchRequested = false;
                    dialogService.Show(Severity.Error, ButtonType.OK,
                        null,
                        "LLVM initialization timed out. Onlyfun will return to the library.",
                        null);
                }

                if (llvmPreparingDialogId >= 0)
                {
                    dialogService.CloseBlocked(llvmPreparingDialogId);
                    llvmPreparingDialogId = -1;
                }
            }

            yield break;
        }

        private void Update()
        {
            if (settingActive || failed || !launchRequested)
            {
                return;
            }

            if (!started)
            {
                StartCoroutine(InitializeGameRun());
                started = true;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (currentWindow != System.IntPtr.Zero)
            {
                SetWindowText(currentWindow, $"nofun - {system.GameName} - {graphicDriver.Fps} FPS");
            }
#endif

            timeDriver.Update();
        }
    }
}
