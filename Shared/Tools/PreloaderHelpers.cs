using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ClientPlugin.Tools;

public static class PreloaderHelpers
{
    public static string Hash(this Collection<Instruction> instructions)
    {
        return instructions.HashInstructions().CombineHashCodes().ToString("x8");
    }

    public static void VerifyCodeHash(
        this Collection<Instruction> il,
        MethodDefinition patchedMethod,
        string expected
    )
    {
        var actual = il.Hash();
        if (actual != expected)
        {
            throw new Exception(
                $"Prepatch: Detected code change in {patchedMethod.Name}: expected {expected}, actual {actual}"
            );
        }
    }

    private static string FormatCode(this Collection<Instruction> instructions)
    {
        var sb = new StringBuilder();

        var hash = instructions.Hash();
        sb.Append($"// {hash}\n");

        foreach (var instr in instructions)
        {
            sb.Append(instr.ToCodeLine());
            sb.Append("\n");
        }

        return sb.ToString();
    }

    private static string ToCodeLine(this Instruction instr)
    {
        var sb = new StringBuilder();

        sb.Append($"IL_{instr.Offset:0000}: {instr.OpCode}");

        var arg = FormatOperand(instr.Operand);
        if (arg.Length > 0)
        {
            sb.Append(' ');
            sb.Append(arg);
        }

        return sb.ToString();
    }

    private static string FormatOperand(object operand)
    {
        switch (operand)
        {
            case null:
                return "";

            case MethodReference methodRef:
                return FormatMethodReference(methodRef);

            case TypeReference typeRef:
                return FormatTypeReference(typeRef);

            case FieldReference fieldRef:
                return FormatFieldReference(fieldRef);

            case Instruction targetInstr:
                return $"IL_{targetInstr.Offset:x4}";

            case Instruction[] instructions:
                return string.Join(", ", instructions.Select(i => $"IL_{i.Offset:x4}"));

            case VariableDefinition varDef:
                return $"{varDef.Index} ({FormatTypeReference(varDef.VariableType)})";

            case ParameterDefinition paramDef:
                return $"{paramDef.Name} ({FormatTypeReference(paramDef.ParameterType)})";

            case string s:
                return s.ToLiteral();

            case float f:
                return f.ToString(CultureInfo.InvariantCulture);

            case double d:
                return d.ToString(CultureInfo.InvariantCulture);

            case int i:
                return i.ToString(CultureInfo.InvariantCulture);

            case long l:
                return l.ToString(CultureInfo.InvariantCulture);

            case sbyte sb:
                return sb.ToString(CultureInfo.InvariantCulture);

            case byte b:
                return b.ToString(CultureInfo.InvariantCulture);

            default:
                return operand.ToString()?.Trim() ?? "null";
        }
    }

    private static string FormatMethodReference(MethodReference methodRef)
    {
        var sb = new StringBuilder();

        sb.Append(FormatTypeReference(methodRef.ReturnType));
        sb.Append(' ');

        if (methodRef.DeclaringType != null)
        {
            sb.Append(FormatTypeReference(methodRef.DeclaringType));
            sb.Append("::");
        }

        sb.Append(methodRef.Name);

        if (
            methodRef is GenericInstanceMethod genericMethod
            && genericMethod.GenericArguments.Count > 0
        )
        {
            sb.Append('<');
            sb.Append(
                string.Join(", ", genericMethod.GenericArguments.Select(FormatTypeReference))
            );
            sb.Append('>');
        }

        sb.Append('(');
        if (methodRef.HasParameters)
            sb.Append(
                string.Join(
                    ", ",
                    methodRef.Parameters.Select(p => FormatTypeReference(p.ParameterType))
                )
            );
        sb.Append(')');

        return sb.ToString();
    }

    private static string FormatTypeReference(TypeReference typeRef)
    {
        if (typeRef == null)
            return "void";

        if (typeRef is GenericInstanceType genericType)
        {
            var baseName = genericType.ElementType.Name;
            var tickIndex = baseName.IndexOf('`');
            if (tickIndex > 0)
                baseName = baseName.Substring(0, tickIndex);

            return $"{genericType.Namespace}.{baseName}<{string.Join(", ", genericType.GenericArguments.Select(FormatTypeReference))}>";
        }

        if (typeRef is ArrayType arrayType)
            return FormatTypeReference(arrayType.ElementType) + "[]";

        if (typeRef is ByReferenceType byRefType)
            return FormatTypeReference(byRefType.ElementType) + "&";

        if (typeRef is PointerType pointerType)
            return FormatTypeReference(pointerType.ElementType) + "*";

        if (!string.IsNullOrEmpty(typeRef.Namespace))
            return $"{typeRef.Namespace}.{typeRef.Name}";

        return typeRef.Name;
    }

    private static string FormatFieldReference(FieldReference fieldRef)
    {
        var sb = new StringBuilder();
        sb.Append(FormatTypeReference(fieldRef.FieldType));
        sb.Append(' ');
        if (fieldRef.DeclaringType != null)
        {
            sb.Append(FormatTypeReference(fieldRef.DeclaringType));
            sb.Append("::");
        }

        sb.Append(fieldRef.Name);
        return sb.ToString();
    }

    public static void RecordOriginalCode(
        this Collection<Instruction> instructions,
        MethodDefinition method,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = ""
    )
    {
        RecordCode(instructions, method, callerFilePath, callerMemberName, "original");
    }

    public static void RecordPatchedCode(
        this Collection<Instruction> instructions,
        MethodDefinition method,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = ""
    )
    {
        RecordCode(instructions, method, callerFilePath, callerMemberName, "patched");
    }

    public static void RecordCustomCode(
        this Collection<Instruction> instructions,
        MethodDefinition method,
        string suffix,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = ""
    )
    {
        RecordCode(instructions, method, callerFilePath, callerMemberName, suffix);
    }

    private static void RecordCode(
        Collection<Instruction> instructions,
        MethodDefinition method,
        string callerFilePath,
        string callerMemberName,
        string suffix
    )
    {
#if DEBUG
        Debug.Assert(callerFilePath.Length > 0);

        if (!File.Exists(callerFilePath))
            return;

        var dir = Path.GetDirectoryName(callerFilePath);
        if (dir == null)
            return;

        var name =
            method == null
                ? callerMemberName.EndsWith("Prepatch")
                    ? callerMemberName.Substring(0, callerMemberName.Length - "Prepatch".Length)
                    : callerMemberName
                : method.DeclaringType.Name.Split('`')[0]
                    + "."
                    + method
                        .Name.Replace(".ctor", "Constructor")
                        .Replace(".cctor", "StaticConstructor");

        // Reduce compiler-generated names to their local function name.
        if (name.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '.'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"g__(\w+)");
            name = match.Success
                ? match.Groups[1].Value
                : new string(
                    name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.').ToArray()
                );
        }

        var path = Path.Combine(dir, $"{name}.{suffix}.il");

        var text = instructions.FormatCode();

        if (File.Exists(path) && File.ReadAllText(path) == text)
            return;

        File.WriteAllText(path, text);
#endif
    }

    /// <summary>
    /// Wraps the whole body of <paramref name="method"/> in <c>try { ... } catch (Exception e)</c>.
    /// The catch calls <paramref name="filter"/>, a static <c>bool (Exception)</c> or, for an
    /// instance method, <c>bool (Exception, object instance)</c>. True swallows the exception and
    /// the method returns the default of its return type; false rethrows it.
    /// <para>
    /// This is a Harmony finalizer written into the IL. Unlike a Harmony patch it can't be lost at
    /// runtime: no detour has to survive the JIT recompiling the method or another plugin
    /// patching it (SE #1073). The filter's signature takes no game types, because importing one
    /// here would load the unpatched game assembly.
    /// </para>
    /// </summary>
    public static void WrapInExceptionFilter(MethodDefinition method, MethodReference filter)
    {
        var body = method.Body;
        var il = body.GetILProcessor();
        var module = method.Module;
        var exceptionType = module.ImportReference(typeof(Exception));

        // Inserted instructions can push a short branch out of its range.
        foreach (var instruction in body.Instructions)
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                instruction.OpCode = LongBranch(instruction.OpCode);

        var isVoid = method.ReturnType.MetadataType == MetadataType.Void;
        var result = isVoid ? null : new VariableDefinition(method.ReturnType);
        if (result != null)
            body.Variables.Add(result);
        var exception = new VariableDefinition(exceptionType);
        body.Variables.Add(exception);
        body.InitLocals = true;

        var end = isVoid ? il.Create(OpCodes.Ret) : il.Create(OpCodes.Ldloc, result);
        var tryStart = body.Instructions[0];
        foreach (var ret in body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // Reuse the ret so that branches to it stay attached.
            if (isVoid)
            {
                ret.OpCode = OpCodes.Leave;
                ret.Operand = end;
            }
            else
            {
                ret.OpCode = OpCodes.Stloc;
                ret.Operand = result;
                il.InsertAfter(ret, il.Create(OpCodes.Leave, end));
            }
        }
        foreach (var tail in body.Instructions.Where(i => i.OpCode == OpCodes.Tail).ToList())
            il.Remove(tail); // no tail calls out of a protected region

        var handler = il.Create(OpCodes.Stloc, exception);
        var swallow = il.Create(OpCodes.Leave, end);
        // A null end means "to the end of the body", which is about to move.
        foreach (var existing in body.ExceptionHandlers)
        {
            existing.TryEnd ??= handler;
            existing.HandlerEnd ??= handler;
        }
        il.Append(handler);
        il.Append(il.Create(OpCodes.Ldloc, exception));
        if (filter.Parameters.Count == 2)
            il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, filter));
        il.Append(il.Create(OpCodes.Brtrue, swallow));
        il.Append(il.Create(OpCodes.Rethrow));
        il.Append(swallow);
        il.Append(end);
        if (!isVoid)
            il.Append(il.Create(OpCodes.Ret));

        // Appended last, so that it encloses every handler the body already has.
        body.ExceptionHandlers.Add(
            new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = handler,
                HandlerStart = handler,
                HandlerEnd = end,
                CatchType = exceptionType,
            }
        );
    }

    private static OpCode LongBranch(OpCode shortBranch) =>
        shortBranch.Code switch
        {
            Mono.Cecil.Cil.Code.Br_S => OpCodes.Br,
            Mono.Cecil.Cil.Code.Brfalse_S => OpCodes.Brfalse,
            Mono.Cecil.Cil.Code.Brtrue_S => OpCodes.Brtrue,
            Mono.Cecil.Cil.Code.Beq_S => OpCodes.Beq,
            Mono.Cecil.Cil.Code.Bge_S => OpCodes.Bge,
            Mono.Cecil.Cil.Code.Bgt_S => OpCodes.Bgt,
            Mono.Cecil.Cil.Code.Ble_S => OpCodes.Ble,
            Mono.Cecil.Cil.Code.Blt_S => OpCodes.Blt,
            Mono.Cecil.Cil.Code.Bne_Un_S => OpCodes.Bne_Un,
            Mono.Cecil.Cil.Code.Bge_Un_S => OpCodes.Bge_Un,
            Mono.Cecil.Cil.Code.Bgt_Un_S => OpCodes.Bgt_Un,
            Mono.Cecil.Cil.Code.Ble_Un_S => OpCodes.Ble_Un,
            Mono.Cecil.Cil.Code.Blt_Un_S => OpCodes.Blt_Un,
            Mono.Cecil.Cil.Code.Leave_S => OpCodes.Leave,
            _ => shortBranch,
        };
}
