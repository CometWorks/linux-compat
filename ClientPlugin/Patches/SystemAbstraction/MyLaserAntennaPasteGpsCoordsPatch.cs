using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch(typeof(MyLaserAntenna), "CreateTerminalControls")]
[HarmonyPatchCategory("Finish")]
static class MyLaserAntennaPasteGpsCoordsPatch
{
    static void Postfix()
    {
        var control = MyLaserAntenna.PasteGpsCoords;
        if (control == null)
            return;

        control.Action = PasteCoordinates;
        if (control.Actions == null)
            return;

        foreach (var action in control.Actions)
            action.Action = PasteCoordinates;
    }

    private static void PasteCoordinates(MyLaserAntenna antenna)
    {
        SdlClipboard.RequestText(text =>
        {
            if (antenna == null || antenna.Closed || antenna.MarkedForClose)
                return;

            antenna.PasteCoordinates(text ?? string.Empty);
        });
    }
}
