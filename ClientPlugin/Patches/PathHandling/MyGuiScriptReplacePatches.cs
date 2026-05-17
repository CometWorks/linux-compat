using System.IO;
using System.Text;
using HarmonyLib;
using Sandbox;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Game;

namespace ClientPlugin.Patches.PathHandling;

// The editor's replace buttons do not call MyGuiScreenEditor.ScriptSelected.
// They build hardcoded Script.cs paths and use direct System.IO calls, so local
// scripts saved as script.cs need the same PathCache resolution at the write site.
static class LocalScriptReplacePath
{
    public static string ResolveScriptFile(string scriptDirectory)
    {
        var scriptPath = Path.Combine(
            scriptDirectory,
            MyBlueprintUtils.DEFAULT_SCRIPT_NAME + MyBlueprintUtils.SCRIPT_EXTENSION);
        return PathCache.ResolveAbsolute(scriptPath);
    }
}

[HarmonyPatch(typeof(MyGuiIngameScriptsPage), nameof(MyGuiIngameScriptsPage.OnReplaceFromEditor))]
[HarmonyPatchCategory("Finish")]
static class MyGuiIngameScriptsPageReplaceFromEditorPatch
{
    static bool Prefix(MyGuiIngameScriptsPage __instance)
    {
        if (__instance.m_selectedItem == null || __instance.GetCodeFromEditor == null || !Directory.Exists(__instance.m_localScriptFolder))
            return false;

        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
            MyMessageBoxStyleEnum.Info,
            MyMessageBoxButtonsType.YES_NO,
            messageCaption: MyTexts.Get(MySpaceTexts.ProgrammableBlock_ReplaceScriptNameDialogTitle),
            messageText: MyTexts.Get(MySpaceTexts.ProgrammableBlock_ReplaceScriptDialogText),
            okButtonText: null,
            cancelButtonText: null,
            yesButtonText: null,
            noButtonText: null,
            callback: callbackReturn =>
            {
                if (callbackReturn != MyGuiScreenMessageBox.ResultEnum.YES)
                    return;

                var itemInfo = __instance.m_selectedItem.UserData as MyBlueprintItemInfo;
                if (itemInfo == null)
                    return;

                var scriptPath = LocalScriptReplacePath.ResolveScriptFile(
                    Path.Combine(__instance.m_localScriptFolder, itemInfo.Data.Name));
                if (File.Exists(scriptPath))
                    File.WriteAllText(scriptPath, __instance.GetCodeFromEditor(), Encoding.UTF8);
            }));

        return false;
    }
}

[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "OnButton_Replace")]
[HarmonyPatchCategory("Finish")]
static class MyGuiBlueprintScreenReplaceScriptPatch
{
    static bool Prefix(MyGuiBlueprintScreen_Reworked __instance)
    {
        if (__instance.SelectedBlueprint == null || __instance.m_content != Content.Script)
            return true;

        if (__instance.SelectedBlueprint.Type == MyBlueprintTypeEnum.CLOUD ||
            __instance.SelectedBlueprint.Type == MyBlueprintTypeEnum.WORKSHOP ||
            (__instance.SelectedBlueprint.Type == MyBlueprintTypeEnum.LOCAL && MySandboxGame.Config.EnableCloud))
        {
            return true;
        }

        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
            MyMessageBoxStyleEnum.Info,
            MyMessageBoxButtonsType.YES_NO,
            messageCaption: MyTexts.Get(MySpaceTexts.ProgrammableBlock_ReplaceScriptNameDialogTitle),
            messageText: MyTexts.Get(MySpaceTexts.ProgrammableBlock_ReplaceScriptDialogText),
            okButtonText: null,
            cancelButtonText: null,
            yesButtonText: null,
            noButtonText: null,
            callback: callbackReturn =>
            {
                if (callbackReturn != MyGuiScreenMessageBox.ResultEnum.YES)
                    return;

                var scriptPath = LocalScriptReplacePath.ResolveScriptFile(Path.Combine(
                    MyBlueprintUtils.SCRIPT_FOLDER_LOCAL,
                    __instance.GetCurrentLocalDirectory(),
                    __instance.SelectedBlueprint.Data.Name));

                if (File.Exists(scriptPath))
                    File.WriteAllText(scriptPath, __instance.m_getCodeFromEditor(), Encoding.UTF8);
            }));

        return false;
    }
}
