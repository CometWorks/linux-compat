using System.Runtime.CompilerServices;

// Assemblies that need to be publicized for accessing internal/protected/private members
// Used by Harmony patches in this plugin

[assembly: IgnoresAccessChecksTo("Sandbox.Game")]
[assembly: IgnoresAccessChecksTo("Sandbox.ObjectBuilders")]
[assembly: IgnoresAccessChecksTo("SpaceEngineers")]
[assembly: IgnoresAccessChecksTo("SpaceEngineers.Game")]
[assembly: IgnoresAccessChecksTo("VRage")]
[assembly: IgnoresAccessChecksTo("VRage.Platform.Windows")]
[assembly: IgnoresAccessChecksTo("VRage.Scripting")]
[assembly: IgnoresAccessChecksTo("VRage.Steam")]
