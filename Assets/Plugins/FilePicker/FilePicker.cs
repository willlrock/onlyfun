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
using System.Collections.Generic;
using System.Linq;

#if UNITY_ANDROID
using UnityEngine;
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
using Nofun.Plugins.Private;
#endif

namespace Nofun.Plugins
{
    public class FilePicker
    {
#if UNITY_EDITOR
        public static bool OpenPickFileDialog(FilterItem[] filters, Action<string> onPathReceived, string defaultPath = null)
        {
            List<string> filterMapped = new();
            foreach (FilterItem filter in filters)
            {
                filterMapped.Add(filter.name);
                filterMapped.Add(filter.spec);
            }

            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Select file", defaultPath ?? "", filterMapped.ToArray());
            onPathReceived(path);

            return true;
        }
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
        public static bool OpenPickFileDialog(FilterItem[] filters, Action<string> onPathReceived, string defaultPath = null)
        {
            string path = NativeFileDialog.OpenPickFileDialog(filters, defaultPath);
            onPathReceived(path);

            return true;
        }
#elif UNITY_ANDROID
        public static bool OpenPickFileDialog(FilterItem[] filters, Action<string> onPathReceived, string defaultPath = null)
        {
            NativeFilePicker.PickFile(
                (string path) =>
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.LogWarning("Open file picker was cancelled or permission was denied.");
                    }

                    onPathReceived(path);
                },
                filters.Select(item => item.spec).ToArray()
            );

            return true;
        }
#endif

        /// <summary>
        /// Opens a picker for one game set. Android uses the native multi-file
        /// picker when available; desktop/editor fall back to a single selection.
        /// </summary>
        public static bool OpenPickFilesDialog(FilterItem[] filters, Action<string[]> onPathsReceived, string defaultPath = null)
        {
#if UNITY_EDITOR
            List<string> filterMapped = new();
            foreach (FilterItem filter in filters)
            {
                filterMapped.Add(filter.name);
                filterMapped.Add(filter.spec);
            }

            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Select game files", defaultPath ?? "", filterMapped.ToArray());
            onPathsReceived?.Invoke(string.IsNullOrEmpty(path) ? new string[0] : new[] { path });
            return true;
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            string path = NativeFileDialog.OpenPickFileDialog(filters, defaultPath);
            onPathsReceived?.Invoke(string.IsNullOrEmpty(path) ? new string[0] : new[] { path });
            return true;
#elif UNITY_ANDROID
            string[] allowedTypes = filters == null
                ? new[] { "*/*" }
                : filters.Select(item => item.spec).ToArray();

            if (NativeFilePicker.CanPickMultipleFiles())
            {
                NativeFilePicker.PickMultipleFiles(
                    (string[] paths) =>
                    {
                        if (paths == null || paths.Length == 0)
                        {
                            Debug.LogWarning("Open multi-file picker was cancelled or permission was denied.");
                            onPathsReceived?.Invoke(new string[0]);
                        }
                        else
                        {
                            onPathsReceived?.Invoke(paths);
                        }
                    },
                    allowedTypes
                );
            }
            else
            {
                NativeFilePicker.PickFile(
                    (string path) => onPathsReceived?.Invoke(string.IsNullOrEmpty(path) ? new string[0] : new[] { path }),
                    allowedTypes
                );
            }

            return true;
#else
            onPathsReceived?.Invoke(new string[0]);
            return false;
#endif
        }

        public static void ExportLog(string sourcePath, Action<bool> onFinished)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.SaveFilePanel("Save Log", "", "onlyfun.log", "log");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    System.IO.File.Copy(sourcePath, path, true);
                    onFinished?.Invoke(true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to export log: {ex}");
                    onFinished?.Invoke(false);
                }
            }
            else
            {
                onFinished?.Invoke(false);
            }
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            FilterItem[] filters = new FilterItem[] { new FilterItem { name = "Log file", spec = "log" } };
            string path = NativeFileDialog.OpenSaveFileDialog(filters, null, "onlyfun.log");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    System.IO.File.Copy(sourcePath, path, true);
                    onFinished?.Invoke(true);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to export log: {ex}");
                    onFinished?.Invoke(false);
                }
            }
            else
            {
                onFinished?.Invoke(false);
            }
#elif UNITY_ANDROID
            NativeFilePicker.ExportFile(sourcePath, success => onFinished?.Invoke(success));
#else
            onFinished?.Invoke(false);
#endif
        }
    }
}
