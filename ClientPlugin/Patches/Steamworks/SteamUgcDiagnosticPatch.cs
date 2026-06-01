using System;
using System.IO;
using HarmonyLib;
using Steamworks;
using VRage.Steam;
using VRage.Steam.Steamworks;

namespace ClientPlugin.Patches.Steamworks;

// DIAGNOSTIC ONLY (not a fix). Blueprint workshop upload hangs after
// "New workshop item with id ... created successfully" — the
// SubmitItemUpdateResult callback never fires and PublishItemBlocking
// stays parked in WaitOne(). Steam's content_log shows zero upload
// activity, so SubmitItemUpdate either returns k_uAPICallInvalid or
// the request never reaches Steam.
//
// This patch logs the inputs and return values of every Steam UGC call
// involved in the publish flow:
//
//   StartItemUpdate    -> UGCUpdateHandle_t (invalid = ulong.MaxValue)
//   SetItemTitle       -> bool
//   SetItemTags        -> bool
//   SetItemVisibility  -> bool
//   SetItemDescription -> bool
//   SetItemContent     -> bool  (folder path)
//   SetItemPreview     -> bool  (thumbnail path)
//   SetItemMetadata    -> bool  (metadata length)
//   SubmitItemUpdate   -> SteamAPICall_t (invalid = 0)
//
// One reproduction will pinpoint where the chain breaks. Output goes to
// /tmp/linuxcompat_ugc.log so it is independent of the game log buffer.
//
// Remove once the underlying issue is fixed.
static class SteamUgcDiagnosticPatch
{
    private const string LogPath = "/tmp/linuxcompat_ugc.log";

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
        }
        catch
        {
            // Diagnostic only.
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.StartItemUpdate))]
    [HarmonyPatchCategory("Finish")]
    static class StartItemUpdate_Patch
    {
        static void Postfix(AppId_t nConsumerAppId, PublishedFileId_t nPublishedFileID, UGCUpdateHandle_t __result)
        {
            Log($"StartItemUpdate(app={nConsumerAppId.m_AppId}, item={nPublishedFileID.m_PublishedFileId}) " +
                $"=> handle=0x{__result.m_UGCUpdateHandle:X16} valid={__result.m_UGCUpdateHandle != ulong.MaxValue}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemTitle))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemTitle_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, string pchTitle, bool __result)
        {
            Log($"SetItemTitle(handle=0x{handle.m_UGCUpdateHandle:X16}, title='{pchTitle}') => {__result}");
        }
    }

    // The preloader rewrites the IL of MySteamUgcClient.SetItemTags so that
    // the inner call site targets the 3-arg SteamUGC.SetItemTags overload
    // (with bAllowAdminTags=false). After that rewrite Harmony can read the
    // method body cleanly, so this Postfix is safe to install again.
    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemTags))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemTags_Patch
    {
        static void Postfix(UGCUpdateHandle_t updateHandle, System.Collections.Generic.IList<string> pTags, bool __result)
        {
            string tags = pTags == null ? "<null>" : string.Join(",", pTags);
            Log($"SetItemTags(handle=0x{updateHandle.m_UGCUpdateHandle:X16}, tags=[{tags}]) => {__result}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemVisibility))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemVisibility_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, ERemoteStoragePublishedFileVisibility eVisibility, bool __result)
        {
            Log($"SetItemVisibility(handle=0x{handle.m_UGCUpdateHandle:X16}, vis={eVisibility}) => {__result}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemDescription))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemDescription_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, string pchDescription, bool __result)
        {
            int len = pchDescription?.Length ?? -1;
            Log($"SetItemDescription(handle=0x{handle.m_UGCUpdateHandle:X16}, descLen={len}) => {__result}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemContent))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemContent_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, string pszContentFolder, bool __result)
        {
            bool exists = false;
            bool isAbsolute = false;
            int fileCount = -1;
            try
            {
                if (!string.IsNullOrEmpty(pszContentFolder))
                {
                    isAbsolute = Path.IsPathRooted(pszContentFolder);
                    exists = Directory.Exists(pszContentFolder);
                    if (exists)
                        fileCount = Directory.GetFiles(pszContentFolder).Length;
                }
            }
            catch
            {
            }

            Log($"SetItemContent(handle=0x{handle.m_UGCUpdateHandle:X16}, folder='{pszContentFolder}', " +
                $"absolute={isAbsolute}, exists={exists}, files={fileCount}) => {__result}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemPreview))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemPreview_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, string pszPreviewFile, bool __result)
        {
            bool exists = false;
            bool isAbsolute = false;
            long size = -1;
            try
            {
                if (!string.IsNullOrEmpty(pszPreviewFile))
                {
                    isAbsolute = Path.IsPathRooted(pszPreviewFile);
                    var fi = new FileInfo(pszPreviewFile);
                    exists = fi.Exists;
                    if (exists)
                        size = fi.Length;
                }
            }
            catch
            {
            }

            Log($"SetItemPreview(handle=0x{handle.m_UGCUpdateHandle:X16}, file='{pszPreviewFile}', " +
                $"absolute={isAbsolute}, exists={exists}, size={size}) => {__result}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SetItemMetadata))]
    [HarmonyPatchCategory("Finish")]
    static class SetItemMetadata_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, string pchMetaData, bool __result)
        {
            int len = pchMetaData?.Length ?? -1;
            Log($"SetItemMetadata(handle=0x{handle.m_UGCUpdateHandle:X16}, metadataLen={len}) => {__result}");
        }
    }

    [HarmonyPatch(typeof(MySteamUgcClient), nameof(MySteamUgcClient.SubmitItemUpdate))]
    [HarmonyPatchCategory("Finish")]
    static class SubmitItemUpdate_Patch
    {
        static void Postfix(UGCUpdateHandle_t handle, string pchChangeNote, SteamAPICall_t __result)
        {
            ulong call = __result.m_SteamAPICall;
            Log($"SubmitItemUpdate(handle=0x{handle.m_UGCUpdateHandle:X16}, note='{pchChangeNote}') " +
                $"=> apiCall=0x{call:X16} valid={call != 0UL}");
        }
    }

    // High-level entry point: record the Folder/Thumbnail/Title/Tags as the
    // publisher sees them, immediately before the chain of Set* calls. Useful
    // in case CheckModFolder rewrote the path to /tmp.
    [HarmonyPatch(typeof(MySteamWorkshopItemPublisher), "UpdatePublishedItem")]
    [HarmonyPatchCategory("Finish")]
    static class UpdatePublishedItem_Patch
    {
        static void Prefix(MySteamWorkshopItemPublisher __instance)
        {
            try
            {
                string folder = __instance.Folder;
                string thumb = __instance.Thumbnail;
                string title = __instance.Title;
                ulong id = __instance.Id;
                Log($"UpdatePublishedItem ENTER id={id} title='{title}' folder='{folder}' thumb='{thumb}'");
            }
            catch (Exception ex)
            {
                Log($"UpdatePublishedItem ENTER (read failed): {ex.Message}");
            }
        }

        static void Postfix()
        {
            Log("UpdatePublishedItem EXIT (synchronous portion)");
        }
    }
}
