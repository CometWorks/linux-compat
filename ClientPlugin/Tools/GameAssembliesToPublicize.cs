using System.Runtime.CompilerServices;

// Assemblies that need to be publicized for accessing internal/protected/private members
// Used by Harmony patches in this plugin

[assembly: IgnoresAccessChecksTo("Sandbox.Game")]
[assembly: IgnoresAccessChecksTo("Sandbox.Graphics")]
[assembly: IgnoresAccessChecksTo("Sandbox.ObjectBuilders")]
[assembly: IgnoresAccessChecksTo("SpaceEngineers")]
[assembly: IgnoresAccessChecksTo("SpaceEngineers.Game")]
[assembly: IgnoresAccessChecksTo("VRage")]
[assembly: IgnoresAccessChecksTo("VRage.Audio")]
[assembly: IgnoresAccessChecksTo("VRage.Game")]
[assembly: IgnoresAccessChecksTo("VRage.Input")]
[assembly: IgnoresAccessChecksTo("VRage.EOS")]
[assembly: IgnoresAccessChecksTo("VRage.Network")]
[assembly: IgnoresAccessChecksTo("VRage.Platform.Windows")]
[assembly: IgnoresAccessChecksTo("VRage.Render")]
[assembly: IgnoresAccessChecksTo("VRage.Render11")]
[assembly: IgnoresAccessChecksTo("VRage.Scripting")]
[assembly: IgnoresAccessChecksTo("VRage.Steam")]
