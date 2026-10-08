using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// HarmonyScanCheck <dll>...  — used by tools/native_gate.py.
//
// ★★ The game's dec system calls GetCustomAttribute on EVERY type of EVERY loaded mod assembly, unguarded, before any
//   mod code runs (Dec.ParserModular: GetAllUserTypes().Where(t => t.GetCustomAttribute<StaticReferencesAttribute>())).
//   Our Harmony ships in Harmony/, outside the folder the game loads, so at that moment 0Harmony is usually NOT loaded:
//   a type whose attribute, base type or interface lives in 0Harmony makes the read throw, and the game breaks for good
//   (reproduced 2026-10-08: 60,576 exceptions from one [HarmonyPatch] class). Patch classes use the kit's [GamePatch].
//
// Per dll prints "REFS0HARMONY yes|no" and one "VIOLATION ..." line per offending type. Exit 0 = read every dll,
// 1 = a violation, 2 = could not read an input (the gate treats 2 as a refusal, never as a pass).
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("usage: HarmonyScanCheck <dll>..."); return 2; }
        int worst = 0;
        foreach (string path in args)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var pe = new PEReader(fs);
                if (!pe.HasMetadata) { Console.WriteLine($"FILE {path}\nERROR not a .NET assembly"); worst = 2; continue; }
                MetadataReader md = pe.GetMetadataReader();
                bool refs = md.AssemblyReferences.Any(h => md.GetString(md.GetAssemblyReference(h).Name) == "0Harmony");
                Console.WriteLine($"FILE {path}");
                Console.WriteLine("REFS0HARMONY " + (refs ? "yes" : "no"));
                int types = 0;
                foreach (TypeDefinitionHandle th in md.TypeDefinitions)
                {
                    TypeDefinition td = md.GetTypeDefinition(th);
                    string name = FullName(md, td);
                    types++;
                    foreach (CustomAttributeHandle ch in td.GetCustomAttributes())
                    {
                        CustomAttribute ca = md.GetCustomAttribute(ch);
                        string owner = CtorAssembly(md, ca.Constructor, out string attr);
                        if (owner == "0Harmony") { Console.WriteLine($"VIOLATION {name}: attribute [{attr}] from 0Harmony"); worst = Math.Max(worst, 1); }
                    }
                    if (!td.BaseType.IsNil && TypeAssembly(md, td.BaseType, out string bt) == "0Harmony")
                    { Console.WriteLine($"VIOLATION {name}: derives from {bt} (0Harmony)"); worst = Math.Max(worst, 1); }
                    foreach (InterfaceImplementationHandle ih in td.GetInterfaceImplementations())
                        if (TypeAssembly(md, md.GetInterfaceImplementation(ih).Interface, out string it) == "0Harmony")
                        { Console.WriteLine($"VIOLATION {name}: implements {it} (0Harmony)"); worst = Math.Max(worst, 1); }
                }
                Console.WriteLine($"TYPES {types}");
            }
            catch (Exception e) { Console.WriteLine($"FILE {path}\nERROR {e.GetType().Name}: {e.Message}"); worst = 2; }
        }
        return worst;
    }

    static string FullName(MetadataReader md, TypeDefinition td)
    {
        string n = md.GetString(td.Name), ns = md.GetString(td.Namespace);
        TypeDefinitionHandle decl = td.GetDeclaringType();
        if (!decl.IsNil) return FullName(md, md.GetTypeDefinition(decl)) + "+" + n;
        return ns.Length == 0 ? n : ns + "." + n;
    }

    static string CtorAssembly(MetadataReader md, EntityHandle ctor, out string type)
    {
        type = "?";
        if (ctor.Kind == HandleKind.MemberReference)
            return TypeAssembly(md, md.GetMemberReference((MemberReferenceHandle)ctor).Parent, out type);
        if (ctor.Kind == HandleKind.MethodDefinition)
        {
            TypeDefinition td = md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType());
            type = md.GetString(td.Name);
            return "(this assembly)";
        }
        return "?";
    }

    // The assembly a type handle lives in: "(this assembly)" for a definition, the referenced assembly's name otherwise.
    static string TypeAssembly(MetadataReader md, EntityHandle h, out string type)
    {
        type = "?";
        switch (h.Kind)
        {
            case HandleKind.TypeDefinition:
                type = md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name);
                return "(this assembly)";
            case HandleKind.TypeReference:
                TypeReference tr = md.GetTypeReference((TypeReferenceHandle)h);
                type = md.GetString(tr.Name);
                EntityHandle scope = tr.ResolutionScope;
                while (scope.Kind == HandleKind.TypeReference)   // nested type: walk out to the outermost reference
                    scope = md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
                if (scope.Kind == HandleKind.AssemblyReference)
                    return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                return "(this assembly)";
            case HandleKind.TypeSpecification:   // a generic instantiation: check its generic type definition
                BlobReader br = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
                if (br.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance)
                {
                    br.ReadSignatureTypeCode();
                    return TypeAssembly(md, br.ReadTypeHandle(), out type);
                }
                return "?";
        }
        return "?";
    }
}
