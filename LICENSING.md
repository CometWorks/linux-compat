# Licensing

Linux compatibility uses the AGPL in [LICENSE](LICENSE), with the additional
permission below. Existing AGPL version choices are unchanged.

## CometWorks Independent Extension Exception

Version 1.0

This additional permission is granted under section 7 of the GNU Affero General
Public License, version 3 (the "AGPL"). It permits certain independent works to
be combined with the Program without requiring those works to be licensed under
the AGPL. It does not exempt modifications of the Program merely because they
are supplied as plugins or other separately packaged components.

The AGPL text in `LICENSE` is not amended by this exception. The applicable AGPL
version designation of each covered work is unchanged.

### 0. Definitions.

"Program", "modify", "covered work", "convey" and "Corresponding Source" have
the meanings given in the AGPL. "Grantors" means the copyright holders who have
applied this exception to material they are entitled to license. This exception
applies only to their interests in that material.

"Covered Implementation" means the Program's implementation code, in source or
object form, to which the AGPL applies. It does not include material expressly
made available under a separate permissive license, or separately licensed
third-party software. A permissive license on an SDK, interface definition or
example does not extend to implementation code merely because that code is
called by, implements, or accompanies the permissively licensed material.

"Supported Interface" means an SDK, API, protocol or extension point which the
applicable upstream CometWorks project expressly designates for third-party use
in its published SDK or interface documentation for the version used. It includes
the documented callbacks, types, data structures and operations of that interface.
A type's public access modifier, its presence in published source, or its
technical accessibility does not by itself make it a Supported Interface.
An interface added or redesignated solely by a downstream fork is not thereby a
Supported Interface for purposes of this exception.

Use of a Supported Interface is assessed at that interface's boundary. The
Program's own implementation of a documented operation does not constitute
unsupported access by the caller merely because that implementation uses private
methods or state. A separately supplied adapter which confines its access to
Supported Interfaces is not excluded merely because it is an adapter.

An "Independent Extension" is a work for which all of the following are true:

a) It does not incorporate, reproduce or adapt Covered Implementation in a manner
requiring copyright permission, whether as source code, object code, translated
code or another form. Separately permissively licensed material may be used in
accordance with its own license.

b) Its interaction with Covered Implementation, directly or through intervening
modules, processes or services, is confined to Supported Interfaces and their
documented operation. Its own interfaces and its interaction with separately
licensed game components or third-party services are not restricted by this
paragraph.

c) It does not supply, or depend upon, any of the arrangements excluded by
section 2 as the means of its interaction with Covered Implementation.

A work need not be capable of useful execution without the Program to be an
Independent Extension. Host-specific functionality, loading into the same
process, callbacks and the exchange of documented API data do not, by themselves,
disqualify it.

### 1. Additional permission for Independent Extensions.

Notwithstanding those conditions of the AGPL which would otherwise require the
combination to be licensed as a whole under the AGPL, you may link or combine
the Program with an Independent Extension, load and execute that combination,
permit users to interact with it over a network, and convey it. You may license
the Independent Extension under terms of your choice, including proprietary
terms, provided that you comply with the AGPL for the covered parts of the
combination and with this exception to the extent you rely upon it.

Solely by reason of a qualifying combination or interaction, you are not required
to license the Independent Extension under the AGPL or to include its source in
the Corresponding Source required for the Program. This permission applies to
qualifying server plugins, UI plugins, companion plugins, client mods and
external services, whether supplied together with the Program or separately.

No fee, registration or separate agreement with the Grantors is required to
exercise this additional permission. This section grants no permission to
license Covered Implementation under proprietary terms.

### 2. Excluded arrangements; substance of the work.

The additional permission in section 1 does not apply to any of the following:

a) A work which contains or adapts Covered Implementation, including code copied
from source, reconstructed from object code, translated into another language,
or otherwise transformed while retaining expression for which copyright
permission is required. This exclusion does not restrict rights independently
available under a permissive license or applicable law.

b) A work which modifies or accesses the internal operation of Covered
Implementation outside a Supported Interface, including by runtime patching,
method replacement, detouring, IL rewriting, reflection into internal state, or
reliance on undocumented implementation details. A replacement or override
expressly provided for by a Supported Interface is not excluded merely because
it replaces or overrides a feature through that interface.

c) An arrangement in which a wrapper, shim, proxy, adapter, private protocol or
modified host provides access to Covered Implementation outside Supported
Interfaces on behalf of another component, supplies internal patches for that
component, or replaces a covered internal component outside a Supported
Interface. Placing a Supported Interface or a permissively licensed wrapper in
one part of that arrangement does not qualify an otherwise unsupported path of
interaction for this permission.

d) A modification of Covered Implementation divided among assemblies, plugins,
processes, containers, repositories or services and presented as independent
works. Separation of files, a network transport, dynamic loading, a plugin
manifest, an author's description, or the name "plugin" does not establish
independence.

Eligibility shall be determined from the work and its actual interaction with
Covered Implementation, including interaction through intermediary components.
There is no exception based on the size of a patch, the proportion of original
code, the importance of a feature, or the absence of changes to files on disk.
No source code or documentation supplied as part of a covered modification is
excluded from its Corresponding Source merely by being assigned to a plugin or
another separately named component.

These exclusions define the limits of the additional permission. They do not
create a presumption that every excluded work is a covered work, determine
whether copyright permission is required, or impose restrictions on rights
available under the AGPL, a separate license or applicable law.

### 3. Obligations retained for covered works.

The Program and modifications of Covered Implementation remain subject to the
AGPL. Nothing in section 1 dispenses with the applicable requirements for
conveying covered source or object code, preserving legal notices, identifying
modifications, providing Installation Information, or offering Corresponding
Source under section 13 of the AGPL.

Where the AGPL requires Corresponding Source, it must correspond to the covered
version actually conveyed or made available for remote interaction and include
the covered modifications and the build and installation material required by
the AGPL. A link to an upstream version which omits those modifications does not
supply that source. The exclusion for an Independent Extension is limited to
section 1; it does not exclude source for an arrangement described in section 2
where that source is otherwise required by the AGPL.

Combining an Independent Extension with a modified version of the Program does
not, by itself, withdraw the permission for that extension. The extension must
still satisfy section 0, and the modified covered parts remain subject to the
AGPL. Neither submission of a contribution to the upstream project nor its
acceptance by the maintainers is a substitute for, or an additional condition of,
complying with the applicable source requirements.

### 4. Permission for separately licensed game components.

The Grantors also permit you to link or combine the Program with the separately
licensed Space Engineers client or dedicated server binaries, VRage, and the
third-party runtime libraries supplied with them, and to run and convey the
resulting combination, insofar as permission from the Grantors is required.
Solely by reason of this combination, the source of those separately licensed
components need not be included in the Corresponding Source of the Program.

This section grants no rights in those components, waives none of their own
license requirements, and does not apply to Covered Implementation incorporated
into or packaged with them. It is separate from section 1 and does not qualify
another plugin, service or component for that section's permission.

### 5. Other rights, notices and downstream copies.

This exception does not replace the AGPL, revoke a license or additional
permission previously granted for a particular version or copy, or enlarge
copyright protection to cover ideas, functionality, unprotectable interfaces or
conduct not requiring copyright permission. Rights under an earlier grant are
determined by that grant; publication of different terms does not of itself
withdraw those rights. In particular, it does not restrict a work's use of SDK,
contract or example code under an applicable MIT or other permissive license. Similarity of functionality
alone does not establish that a work contains or adapts Covered Implementation.

The AGPL's provisions governing legal notices and additional permissions
continue to apply. You may remove this additional permission from a copy, or
from a part of it, as section 7 of the AGPL permits; the AGPL and any other
applicable permissions then govern that material without this exception. You
may apply this exception to your own contributions if you have the necessary
rights. A downstream contributor's adoption of the exception shall not be
presumed solely from the presence of this document elsewhere in a distribution.

The AGPL's warranty disclaimers and limitations of liability continue to apply.
Nothing in this exception grants trademark rights or authority to grant rights
owned by third parties.

## Source availability in operation and distribution

The AGPL permits commercial use. Merely running an unmodified version does not
itself trigger section 13. If users interact remotely with a modified version,
section 13 requires a prominent opportunity for those users to obtain its
Corresponding Source, free of charge, through a standard means of copying it.
An upstream link is sufficient only to the extent it actually supplies the
matching source required for the version in use. Distribution of binaries or
containers must separately satisfy section 6 and the other applicable terms.

For a modified game-server stack, our compliance guidance is to include players
who interact with that modified software in the source offer. A conspicuous join
notice linked to the matching source is one practical method. Keep applicable
source links and legal notices available; a private source archive offered only
to the upstream maintainers does not replace an offer required for users.

These are explanations of the AGPL and the exception, not additional conditions
on the rights the AGPL grants. There is no exemption for a small patch. A local
change around a plugin update call is still a modification of the covered host;
an otherwise qualifying Independent Extension remains eligible under section 3.

The following applications illustrate the distinction without enlarging the
exception:

- Independently written gameplay, administration or performance functionality
  using only Supported Interfaces may qualify even when it is useful only with
  the Program. Its importance or similarity to an existing feature is not the
  test of whether it incorporates or adapts Covered Implementation.
- A UI extension using a documented replacement point may qualify. A module
  which patches the host's internal methods or extracts covered UI/service code
  does not obtain the permission merely by being loaded as a UI plugin.
- A client or backend exchanging documented HTTP, WebSocket or mod messages may
  qualify. Changing the transport or interposing a wrapper does not make copied
  or internally coupled implementation code independent.
- A covered core fix or implementation component moved into another assembly,
  repository or service remains subject to the AGPL where that license applies.
  The exception does not exempt it by reason of that packaging.

Open-source extensions and contributions upstream are encouraged. The AGPL does
not require a maintainer to accept your changes, and this exception does not
require you to submit them. Previously granted MIT permissions and the terms of
separately licensed dependencies remain in effect.

## Alternative licensing

If your proposed use is not accommodated by the AGPL and this exception, you may
contact [CometWorks](https://github.com/CometWorks) to request a separate written
license. This includes requests concerning proprietary modifications of covered
implementation code, to the extent the relevant copyright holders have the
rights necessary to offer such terms.

Alternative licenses are considered individually. Any applicable fee, licensed
entities, products, components, versions, deployments, duration and source
obligations must be expressly agreed in writing. No alternative license, fixed
price or entitlement to an offer is granted by this invitation. A request or
negotiation does not suspend obligations under the license you currently rely on.

An alternative license is not required merely to run the Program commercially,
or to develop, use or distribute an Independent Extension within the permission
above. That permission remains available without a license fee or registration.
