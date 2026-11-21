# Plugin and API exception

This is an additional permission under AGPL section 7, given by the copyright
holders applying it for code they can license. The AGPL itself and its version
choices stay unchanged.

## Your own plugins can stay closed

You may build, use, modify, link, run, host and distribute independent plugins,
mods, UI extensions and services under your own license, including a proprietary
license, without a fee or registration. You do not have to provide their source
just because they run with this project, even in the same process. They may
exchange API data and depend on the host to work.

## Use the documented APIs

An independent extension must not copy or adapt this project's AGPL implementation
code in a way that requires copyright permission. Separately MIT-licensed SDK,
contract and example code can be used under MIT.

Its access to this project's implementation, including through other components,
must stay within APIs documented for third-party use by the upstream CometWorks
project for that version. A method being public, technically accessible or
labelled an API by a private fork is not enough. Your extension's own interfaces
and its use of separately licensed game components or third-party services are
not restricted by this rule.

Normal API adapters and documented replacement points are allowed. Our API's own
use of private internals does not turn your API call into unsupported access.
Independently written code is not disqualified just because it offers similar
functionality.

## A plugin label does not exempt core changes

This permission does not cover copied, adapted, translated or decompiled AGPL
implementation code, internal patches, or access outside the documented APIs,
such as replacing internal methods, detouring, rewriting IL or inspecting private
state through reflection. Routing that access through a wrapper, protocol or
modified host does not make it independent. Neither does splitting covered core
changes across plugins, processes or repositories. The actual code and interaction
matter, not the label or packaging.

Falling outside this exception does not by itself decide whether copyright law
makes a work AGPL-covered. A separate host patch does not remove this permission
from an otherwise independent plugin.

## Other terms still apply

The AGPL still applies to covered code, including notices, modification details,
source and required build/install information. Source must match the version
actually distributed or used remotely, including for section 13 offers. You do
not have to submit changes upstream; submitting them does not replace required
source sharing.

Earlier grants, legal rights and the AGPL's warranty and liability terms stay
unchanged. Section 7 still governs notices, removal and contributions. This file
does not grant permission for another contributor's code without their agreement,
or grant trademark or third-party rights.

## Space Engineers and its libraries

We also permit combining, running and distributing this project with separately
licensed Space Engineers client/server binaries, VRage and their supplied runtime
libraries. Their source is not required merely because of that combination.
Their own licenses still apply. This does not exempt our AGPL code copied into
those components or give other plugins a way around the rules above.
