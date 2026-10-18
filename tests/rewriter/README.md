# Rewriter tests

Runs `CompilationRewriter` on small mod sources and checks what the rewritten
compilation reports: its errors, and the rewritten text where the shape of a
rewrite matters.

The suite is standalone. It compiles `Shared/Rewriter/CompilationRewriter.cs`
and `Shared/Rewriter/WindowsSemanticsRewriter.cs` verbatim against Roslyn 2.9.0,
the version the game ships in `Bin64`. `Stubs.cs` stands in for the rest:
`MyApiTarget`, a `ShimRegistration` without the script compiler, and the shim
members the cases need. `Shared/Patches/PathHandling/PathSecurity.cs` is
compiled verbatim too, for the config switches. As in the plugin, the shim reference is the assembly
that holds the rewriter, here the test assembly. The game's definition types
are a small in-memory assembly with the same full names.

It is not part of `LinuxCompat.sln`, and its own `Directory.Build.props`
shadows the repository root's, like `tests/path-translation`.

```bash
cd tests/rewriter && dotnet run -c Release
```

Exit code 0 means every check passed. Covered:

| Case | What it pins down |
| --- | --- |
| `clean mod` | A mod that reaches the shims only through the rewriter compiles, and its definition read is rewritten |
| `object initializer` | `new BuildProgressModel { File = x }` keeps its members as assignments, with the value translated; wrapping one in `FromGameText` failed with CS0747 |
| `assignment value` | A definition assignment used as a value, simple or compound, gives back the Windows form; as a statement it is left alone |
| `refused` | A mod naming a shim fails to compile with the refusal and the symbol: a direct call, `using static`, an alias, a method group and an extension method (SE1-0263 B1) |
| `other target untouched` | Only mod compilations are checked and rewritten |
| `switch off` | With `RefuseShimUse` and `TranslateAssignmentValues` off in `PathSecurity`, a mod naming a shim compiles and an assignment used as a value is left as written |
