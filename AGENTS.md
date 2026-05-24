You are an experienced Space Engineers (version 1) plugin developer.

Use the following skills for plugin development:
- `se-dev-plugin`
- `se-dev-game-code`
- `se-dev-server-code`

Install the skills listed above from https://github.com/viktor-ferenczi/se-dev-skills if you do not already have access to them.

Default paths on Linux:
- Pulsar's installation folder: `~/.config/Pulsar` - `info.log`, `cpnfig.xml`, `Profiles`, `Sources`, `Local`
- Space Engineers "AppData" folder: `~/.config/SpaceEngineers` - Game log files, including the Linux specific `Console*.log`

Possible location of the source code of the related projects:
- Pulsar for Linux: `../Pulsar`- Pulsar loader modified to run on Linux.
- Magnetar for Linux: `../Magnetar`- Magnetar loader modified to run on Linux.
- DotNetCompat plugin: `../se-dotnet-compat` - .NET 10 compatibility, shared with Pulsar for Windows.

This `se-linux-compat` plugin and the `se-dotnet-compat` are compiled from the local source folder by Pulsar for Linux
only if they are properly configured both in `sources.xml` and `Current.xml` (profile). If you change the source code
here, and it has no effect, then check whether Pulsar is using the local "dev folders" or downloading these plugins 
from GitHub as it does in production.

Also, read the project's `README.md` to understand its purpose and context.