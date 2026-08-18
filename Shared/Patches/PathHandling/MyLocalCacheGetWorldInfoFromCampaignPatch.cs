using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Sandbox.Engine.Networking;
using Sandbox.Game;
#if !DEDICATED
using Sandbox.Game.Screens;
#endif
using Sandbox.Game.World;
using VRage.FileSystem;
using VRage.Game.ObjectBuilders.Campaign;
using VRage.Library.Utils;

namespace ClientPlugin.Patches.PathHandling;

[HarmonyPatch(typeof(MyLocalCache), nameof(MyLocalCache.GetWorldInfoFromCampaign))]
[HarmonyPatchCategory("Finish")]
static class MyLocalCacheGetWorldInfoFromCampaignPatch
{
    static bool Prefix(MyObjectBuilder_Campaign campaign, ref List<string> foldersToSearchForCampaigns, ref List<MyWorldInfo> __result)
    {
        List<MyWorldInfo> list = new();
        MyObjectBuilder_CampaignSM myObjectBuilder_CampaignSM = MyPlatformGameSettings.CONSOLE_COMPATIBLE ? campaign.GetCrossPlatformStateMachine() : campaign.GetStateMachine();
        if (myObjectBuilder_CampaignSM != null)
        {
            MyObjectBuilder_CampaignSMNode myObjectBuilder_CampaignSMNode = MyCampaignManager.Static.FindStartingState(myObjectBuilder_CampaignSM);
            if (myObjectBuilder_CampaignSMNode != null)
            {
                string saveFileDirectory = Path.GetDirectoryName(PathCache.ResolveAbsolute(myObjectBuilder_CampaignSMNode.SaveFilePath));
                string fullName = Directory.GetParent(Path.Combine(MyFileSystem.ContentPath, saveFileDirectory)).FullName;
                if ((campaign.IsLocalMod || !campaign.IsVanilla) && !string.IsNullOrEmpty(campaign.ModFolderPath) && !Directory.Exists(fullName))
                {
                    fullName = Directory.GetParent(Path.Combine(campaign.ModFolderPath, saveFileDirectory)).FullName;
                }
                if (!foldersToSearchForCampaigns.Contains(fullName))
                {
                    foldersToSearchForCampaigns.Add(fullName);
                    List<Tuple<string, MyWorldInfo>> list2 = new();
                    MyLocalCache.GetWorldInfoFromDirectory(fullName, list2, configOnly: true);
                    if (list2.Count > 0)
                    {
                        foreach (Tuple<string, MyWorldInfo> item in list2)
                        {
                            if (item.Item2 != null)
                            {
                                item.Item2.ScfPath = campaign.CampaignPath;
                                list.Add(item.Item2);
                            }
                        }
                    }
                }
            }
        }

        __result = list;
        return false;
    }
}

#if !DEDICATED
[HarmonyPatch(typeof(MyGuiScreenNewGameScenarioSelection), "AddModCampaignCarouselItem")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenNewGameScenarioSelectionAddModCampaignCarouselItemPatch
{
    static bool Prefix(MyGuiScreenNewGameScenarioSelection __instance, MyObjectBuilder_Campaign cItem, ref List<string> foldersToSearchForCampaigns)
    {
        List<MyWorldInfo> worldInfoFromCampaign = MyLocalCache.GetWorldInfoFromCampaign(cItem, ref foldersToSearchForCampaigns);
        MyObjectBuilder_CampaignSM myObjectBuilder_CampaignSM = MyPlatformGameSettings.CONSOLE_COMPATIBLE ? cItem.GetCrossPlatformStateMachine() : cItem.GetStateMachine();
        if (myObjectBuilder_CampaignSM != null)
        {
            MyObjectBuilder_CampaignSMNode myObjectBuilder_CampaignSMNode = MyCampaignManager.Static.FindStartingState(myObjectBuilder_CampaignSM);
            if (myObjectBuilder_CampaignSMNode != null)
            {
                string saveFileDirectory = Path.GetDirectoryName(PathCache.ResolveAbsolute(myObjectBuilder_CampaignSMNode.SaveFilePath));
                string text = Path.Combine(MyFileSystem.ContentPath, saveFileDirectory);
                if ((cItem.IsLocalMod || !cItem.IsVanilla) && !string.IsNullOrEmpty(cItem.ModFolderPath) && !Directory.Exists(text))
                {
                    text = Path.Combine(cItem.ModFolderPath, saveFileDirectory);
                }
                MyWorldInfo myWorldInfo = MyLocalCache.LoadWorldInfoFromFile(text);
                if (myWorldInfo != null)
                {
                    __instance.AddWInfoToCarousel(myWorldInfo, cItem);
                    return false;
                }
            }
        }

        foreach (MyWorldInfo item in worldInfoFromCampaign)
        {
            __instance.AddWInfoToCarousel(item, cItem);
        }

        return false;
    }
}
#endif
