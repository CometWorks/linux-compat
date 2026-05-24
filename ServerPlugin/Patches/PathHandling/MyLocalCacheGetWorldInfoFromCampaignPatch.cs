using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Sandbox.Engine.Networking;
using Sandbox.Game;
using Sandbox.Game.World;
using VRage.FileSystem;
using VRage.Game.ObjectBuilders.Campaign;
using VRage.Library.Utils;

namespace ServerPlugin.Patches.PathHandling;

// FIXME: These should be converted to transpilers

// Ports dotnet-game-local commit fca05c8a: normalize scenario SaveFilePath
// before Path.GetDirectoryName runs on Linux.

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

// Note: the client-side counterpart MyGuiScreenNewGameScenarioSelection
// AddModCampaignCarouselItem patch is intentionally omitted on the server
// — the dedicated server has no campaign-selection GUI.