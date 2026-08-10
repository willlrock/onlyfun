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

using Nofun.VM;
using Nofun.Driver.UI;
using Nofun.Util;
using Nofun.Util.Logging;
using Nofun.Services;

namespace Nofun.Module.VMGP
{
    [Module]
    public partial class VMGP
    {

        [ModuleCall]
        private int vMsgBox(int flags, VMString message, VMString optionalTitle)
        {
            return ShowMessageBox(flags, message, optionalTitle, false);
        }

        [ModuleCall]
        private int vMsgBoxU(int flags, VMString message, VMString optionalTitle)
        {
            return ShowMessageBox(flags, message, optionalTitle, true);
        }

        private static int ToMophunButtonValue(int uiButtonValue)
        {
            // Onlyfun's dialogs report the right-hand OK/Yes button as 0, while
            // Mophun specifies OK/Yes as 1 and No/Cancel as 0.
            return uiButtonValue == 0 ? (int)MessageBoxFlags.OK : (int)MessageBoxFlags.Cancel;
        }

        private int ShowMessageBox(int flags, VMString message, VMString optionalTitle, bool isUnicode)
        {
            uint flagBits = unchecked((uint)flags);
            Severity boxSeverity;
            switch (true)
            {
                case true when BitUtil.FlagSet(flagBits, MessageBoxFlags.Error):
                    boxSeverity = Severity.Error;
                    break;

                case true when BitUtil.FlagSet(flagBits, MessageBoxFlags.Warning):
                    boxSeverity = Severity.Warning;
                    break;

                case true when BitUtil.FlagSet(flagBits, MessageBoxFlags.Info):
                    boxSeverity = Severity.Info;
                    break;

                case true when BitUtil.FlagSet(flagBits, MessageBoxFlags.Question):
                    boxSeverity = Severity.Question;
                    break;

                default:
                    Logger.Warning(LogClass.VMGPSystem, "Unknown message box severity, defaulting to info");
                    boxSeverity = Severity.Info;
                    break;
            }

            ButtonType buttonType;
            switch (true)
            {
                case true when BitUtil.FlagSet(flagBits, MessageBoxFlags.OKCancel):
                    buttonType = ButtonType.OKCancel;
                    break;

                case true when BitUtil.FlagSet(flagBits, MessageBoxFlags.YesNo):
                    buttonType = ButtonType.YesNo;
                    break;

                default:
                    Logger.Warning(LogClass.VMGPSystem, "Unknown message box button type, defaulting to OK");
                    buttonType = ButtonType.OK;
                    break;
            }

            string title = null;

            if (BitUtil.FlagSet(flagBits, MessageBoxFlags.Title))
            {
                title = optionalTitle.Get(system.Memory, isUnicode);
            }

            string content = message.Get(system.Memory, isUnicode);
            int buttonValue = 0;

            // 0 is already cancel
            system.UIDriver.Show(boxSeverity, title, content, buttonType, (int button) =>
            {
                buttonValue = ToMophunButtonValue(button);
            });

            return buttonValue;
        }
    }
}
