using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace UdonExpressionDriver.Editor.Util
{
    public static class AssemblyStripper
    {
        /// <summary>
        ///     Strip a .dll of all symbols not explicitly whitelisted, but keep anything
        ///     transitively referenced by whitelisted symbols.
        /// </summary>
        /// <param name="inputPath">Input assembly path</param>
        /// <param name="whitelist">
        ///     Strings: type full names (e.g. "MyNs.MyType") or member full names
        ///     (e.g. "MyNs.MyType::MyMethod(System.Int32)") or "MyNs.MyType::MyField")
        /// </param>
        /// <param name="outputPath">Output assembly path</param>
        public static void StripExcept(string inputPath, IEnumerable<string> whitelist, string outputPath)
        {
            var readerParams = new ReaderParameters { ReadSymbols = false, InMemory = true };
            var asm = AssemblyDefinition.ReadAssembly(inputPath, readerParams);
            var module = asm.MainModule;

            // normalization sets
            var keepTypes = new HashSet<string>(StringComparer.Ordinal);
            var keepMembers = new HashSet<string>(StringComparer.Ordinal); // method/field fullnames

            // Seed queue: treat whitelist strings as either type fullnames or member fullnames.
            var workQueue = new Queue<MemberReference>();
            foreach (var w in whitelist)
            {
                // try type first
                var tdef = ResolveType(w);
                if (tdef != null)
                {
                    keepTypes.Add(tdef.FullName);
                    workQueue.Enqueue(tdef);
                    continue;
                }

                var mref = ResolveMember(w);
                if (mref != null)
                {
                    keepMembers.Add(mref.FullName);
                    // ensure type containing this member is kept
                    if (mref.DeclaringType != null) keepTypes.Add(mref.DeclaringType.FullName);
                    workQueue.Enqueue(mref);
                    continue;
                }

                // If nothing resolves, try to match by simple type name (best-effort)
                var alt = module.Types.SelectMany(FlattenAll).FirstOrDefault(x => x.Name == w || x.FullName == w);
                if (alt != null)
                {
                    keepTypes.Add(alt.FullName);
                    workQueue.Enqueue(alt);
                }

                // unknown name — ignore (user can supply exact names)
            }

            // BFS: for each kept member/type, find all referenced type/member defs in same module
            while (workQueue.Count > 0)
            {
                var item = workQueue.Dequeue();
                if (item is TypeDefinition td)
                {
                    // record
                    if (!keepTypes.Add(td.FullName))
                    {
                        /* was already present */
                    }

                    // base type
                    if (td.BaseType != null) TryAddTypeReference(td.BaseType, workQueue, keepTypes);

                    // interfaces
                    foreach (var iface in td.Interfaces) TryAddTypeReference(iface.InterfaceType, workQueue, keepTypes);

                    // fields
                    foreach (var f in td.Fields)
                    {
                        TryAddTypeReference(f.FieldType, workQueue, keepTypes);
                        if (keepMembers.Add(f.FullName)) workQueue.Enqueue(f);
                    }

                    // properties/events: keep their types and accessors
                    foreach (var p in td.Properties)
                    {
                        TryAddTypeReference(p.PropertyType, workQueue, keepTypes);
                        if (p.GetMethod != null) TryAddMemberRef(p.GetMethod, workQueue, keepMembers);
                        if (p.SetMethod != null) TryAddMemberRef(p.SetMethod, workQueue, keepMembers);
                        if (keepMembers.Add(p.FullName)) workQueue.Enqueue(p);
                    }

                    foreach (var e in td.Events)
                    {
                        TryAddTypeReference(e.EventType, workQueue, keepTypes);
                        if (e.AddMethod != null) TryAddMemberRef(e.AddMethod, workQueue, keepMembers);
                        if (e.RemoveMethod != null) TryAddMemberRef(e.RemoveMethod, workQueue, keepMembers);
                        if (keepMembers.Add(e.FullName)) workQueue.Enqueue(e);
                    }

                    // nested types
                    foreach (var nt in td.NestedTypes)
                        if (keepTypes.Add(nt.FullName))
                            workQueue.Enqueue(nt);

                    // methods/signatures
                    foreach (var m in td.Methods)
                    {
                        TryAddMethodSignatureReferences(m, workQueue, keepTypes, keepMembers);
                        if (keepMembers.Add(m.FullName)) workQueue.Enqueue(m);
                    }
                }
                else if (item is MethodDefinition md)
                {
                    // return type + params
                    TryAddTypeReference(md.ReturnType, workQueue, keepTypes);
                    foreach (var p in md.Parameters) TryAddTypeReference(p.ParameterType, workQueue, keepTypes);

                    // custom attrs on method
                    foreach (var ca in md.CustomAttributes) TryAddTypeReference(ca.AttributeType, workQueue, keepTypes);

                    // method body instructions -> references
                    if (md.HasBody)
                    {
                        foreach (var instr in md.Body.Instructions)
                        {
                            var op = instr.Operand;
                            switch (op)
                            {
                                case MethodReference mr: TryAddMemberRef(mr, workQueue, keepMembers); break;
                                case FieldReference fr: TryAddMemberRef(fr, workQueue, keepMembers); break;
                                case TypeReference tr: TryAddTypeReference(tr, workQueue, keepTypes); break;
                            }
                        }

                        // exception handlers' catch types
                        foreach (var eh in md.Body.ExceptionHandlers)
                            if (eh.CatchType != null)
                                TryAddTypeReference(eh.CatchType, workQueue, keepTypes);
                    }
                }
                else if (item is FieldDefinition fd)
                {
                    TryAddTypeReference(fd.FieldType, workQueue, keepTypes);
                    foreach (var ca in fd.CustomAttributes) TryAddTypeReference(ca.AttributeType, workQueue, keepTypes);
                }
                else if (item is PropertyDefinition pd)
                {
                    TryAddTypeReference(pd.PropertyType, workQueue, keepTypes);
                    if (pd.GetMethod != null) TryAddMemberRef(pd.GetMethod, workQueue, keepMembers);
                    if (pd.SetMethod != null) TryAddMemberRef(pd.SetMethod, workQueue, keepMembers);
                }
                else if (item is EventDefinition ed)
                {
                    TryAddTypeReference(ed.EventType, workQueue, keepTypes);
                    if (ed.AddMethod != null) TryAddMemberRef(ed.AddMethod, workQueue, keepMembers);
                    if (ed.RemoveMethod != null) TryAddMemberRef(ed.RemoveMethod, workQueue, keepMembers);
                }
                else if (item is MethodReference mref)
                {
                    // try to resolve to definition (if in same module)
                    var def = TryResolveMethodDefinition(mref);
                    if (def != null)
                    {
                        if (keepMembers.Add(def.FullName)) workQueue.Enqueue(def);
                        if (def.DeclaringType != null && keepTypes.Add(def.DeclaringType.FullName))
                            workQueue.Enqueue(def.DeclaringType);
                    }

                    // add signature types
                    TryAddTypeReference(mref.ReturnType, workQueue, keepTypes);
                    foreach (var p in mref.Parameters) TryAddTypeReference(p.ParameterType, workQueue, keepTypes);
                }
                else if (item is FieldReference fref)
                {
                    var fdef = TryResolveFieldDefinition(fref);
                    if (fdef != null)
                    {
                        if (keepMembers.Add(fdef.FullName)) workQueue.Enqueue(fdef);
                        if (fdef.DeclaringType != null && keepTypes.Add(fdef.DeclaringType.FullName))
                            workQueue.Enqueue(fdef.DeclaringType);
                    }

                    TryAddTypeReference(fref.FieldType, workQueue, keepTypes);
                }
                else
                {
                    // best-effort resolve
                    if (item is MethodReference mr)
                    {
                        var d = TryResolveMethodDefinition(mr);
                        if (d != null)
                            if (keepMembers.Add(d.FullName))
                                workQueue.Enqueue(d);
                    }
                }
            }

            // Now remove anything not in keepTypes / keepMembers
            // Remove members from kept types; remove entire types otherwise.
            var allTypes = module.Types.SelectMany(FlattenAll).ToList();
            foreach (var t in allTypes)
            {
                if (!keepTypes.Contains(t.FullName))
                {
                    // remove top-level types only at parent container
                    if (t.IsNested)
                    {
                        // nested type removal: remove from declaring type
                        var parent = t.DeclaringType;
                        parent.NestedTypes.Remove(t);
                    }
                    else
                    {
                        module.Types.Remove(t);
                    }

                    continue;
                }

                // keep the type, prune members not in keepMembers
                t.Methods.RemoveWhere(m => !keepMembers.Contains(m.FullName) && !IsSpecialKeepMethod(m));
                t.Fields.RemoveWhere(f => !keepMembers.Contains(f.FullName));
                t.Properties.RemoveWhere(p => !keepMembers.Contains(p.FullName));
                t.Events.RemoveWhere(e => !keepMembers.Contains(e.FullName));
                // Note: keep nested types that are in keepTypes (handled above)
            }

            // Final save (no symbols written -> PDB removed)
            var writerParams = new WriterParameters { WriteSymbols = false };
            asm.Write(outputPath, writerParams);
            return;

            // Resolve member full name to a definition if possible
            MemberReference ResolveMember(string memberFullName)
            {
                // Try methods/fields by matching FullName
                foreach (var t in module.Types.SelectMany(Flatten))
                {
                    var md = t.Methods.FirstOrDefault(m => m.FullName == memberFullName);
                    if (md != null) return md;
                    var fd = t.Fields.FirstOrDefault(f => f.FullName == memberFullName);
                    if (fd != null) return fd;
                    var prop = t.Properties.FirstOrDefault(p => p.FullName == memberFullName);
                    if (prop != null) return prop;
                    var ev = t.Events.FirstOrDefault(e => e.FullName == memberFullName);
                    if (ev != null) return ev;
                }

                return null;

                IEnumerable<TypeDefinition> Flatten(TypeDefinition td)
                {
                    yield return td;
                    foreach (var nt in td.NestedTypes)
                    foreach (var z in Flatten(nt))
                        yield return z;
                }
            }

            // Helper to resolve type by full name (search nested types too)
            TypeDefinition ResolveType(string fullName)
            {
                // Module.GetType works for top-level and nested with '/' separators in Cecil fullnames
                var t = module.GetType(fullName);
                return t ??
                       // fallback scan
                       module.Types.SelectMany(Flatten).FirstOrDefault(x => x.FullName == fullName);

                IEnumerable<TypeDefinition> Flatten(TypeDefinition td)
                {
                    yield return td;
                    foreach (var z in td.NestedTypes.SelectMany(Flatten))
                        yield return z;
                }
            }

            // helpers
            static IEnumerable<TypeDefinition> FlattenAll(TypeDefinition td)
            {
                yield return td;
                foreach (var nt in td.NestedTypes)
                foreach (var z in FlattenAll(nt))
                    yield return z;
            }

            // try add type reference if it resolves to a type in this module
            void TryAddTypeReference(TypeReference tr, Queue<MemberReference> q, HashSet<string> keepT)
            {
                if (tr == null) return;
                var resolved = ResolveTypeReference(tr);
                if (resolved != null && keepT.Add(resolved.FullName)) q.Enqueue(resolved);
            }

            // try add a member reference (MethodReference / FieldReference)
            void TryAddMemberRef(MemberReference mr, Queue<MemberReference> q, HashSet<string> keepM)
            {
                if (mr == null) return;
                // try to resolve to definition in same module
                if (mr is MethodReference mref)
                {
                    var def = TryResolveMethodDefinition(mref);
                    if (def != null && keepM.Add(def.FullName)) q.Enqueue(def);
                    // also add declaring type
                    if (def?.DeclaringType != null && keepTypes.Add(def.DeclaringType.FullName))
                        q.Enqueue(def.DeclaringType);
                }
                else if (mr is FieldReference fref)
                {
                    var fdef = TryResolveFieldDefinition(fref);
                    if (fdef != null && keepM.Add(fdef.FullName)) q.Enqueue(fdef);
                    if (fdef?.DeclaringType != null && keepTypes.Add(fdef.DeclaringType.FullName))
                        q.Enqueue(fdef.DeclaringType);
                }
            }

            TypeDefinition ResolveTypeReference(TypeReference tr)
            {
                try
                {
                    var resolved = tr.Resolve();
                    // ensure it's from this module (assembly)
                    if (resolved != null && resolved.Module == module) return resolved;
                }
                catch
                {
                    // ignored
                }

                return null;
            }

            MethodDefinition TryResolveMethodDefinition(MethodReference mr)
            {
                try
                {
                    var resolved = mr.Resolve();
                    if (resolved != null && resolved.Module == module) return resolved;
                }
                catch
                {
                    // ignored
                }

                return null;
            }

            FieldDefinition TryResolveFieldDefinition(FieldReference fr)
            {
                try
                {
                    var resolved = fr.Resolve();
                    if (resolved != null && resolved.Module == module) return resolved;
                }
                catch
                {
                    // ignored
                }

                return null;
            }

            void TryAddMethodSignatureReferences(MethodDefinition methodDef, Queue<MemberReference> q, HashSet<string> keepT,
                HashSet<string> keepM)
            {
                TryAddTypeReference(methodDef.ReturnType, q, keepT);
                foreach (var p in methodDef.Parameters) TryAddTypeReference(p.ParameterType, q, keepT);
                foreach (var ca in methodDef.CustomAttributes) TryAddTypeReference(ca.AttributeType, q, keepT);
                // if method has body, inspect operands
                if (!methodDef.HasBody) return;
                foreach (var instr in methodDef.Body.Instructions)
                {
                    var op = instr.Operand;
                    if (op is MethodReference mref) TryAddMemberRef(mref, q, keepM);
                    else if (op is FieldReference fref) TryAddMemberRef(fref, q, keepM);
                    else if (op is TypeReference tr) TryAddTypeReference(tr, q, keepT);
                }
            }

            bool IsSpecialKeepMethod(MethodDefinition m)
            {
                // keep .ctor static ctor .cctor if they are referenced, but if not referenced they're removable.
                // keep .cctor if it has ModuleInitializer attribute? (best-effort: keep static ctor)
                if (m.IsConstructor && m.IsStatic) return true; // safer to keep static ctors
                return false;
            }
        }
    }

    internal static class CecilExtensions
    {
        public static void RemoveWhere<T>(this ICollection<T> collection, Func<T, bool> predicate)
        {
            var toRemove = collection.Where(predicate).ToList();
            foreach (var item in toRemove)
                collection.Remove(item);
        }
    }
}