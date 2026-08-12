using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace UdonExpressionDriver.Bootstrapper
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

            var graph = new KeepGraph(module);
            graph.Seed(whitelist);
            graph.Walk();
            graph.Prune();

            // Final save (no symbols written -> PDB removed)
            var writerParams = new WriterParameters { WriteSymbols = false };
            asm.Write(outputPath, writerParams);
        }

        /// <summary>
        /// Computes the keep set by walking the module's reference graph from the whitelist,
        /// then strips everything that isn't kept.
        /// </summary>
        private class KeepGraph
        {
            private readonly ModuleDefinition _module;
            private readonly HashSet<string> _keepTypes = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _keepMembers = new HashSet<string>(StringComparer.Ordinal);
            private readonly Queue<MemberReference> _workQueue = new Queue<MemberReference>();

            public KeepGraph(ModuleDefinition module)
            {
                _module = module;
            }

            // Seed the queue from the whitelist: each entry is tried as a type full name,
            // then a member full name, then as a simple type name (best effort).
            public void Seed(IEnumerable<string> whitelist)
            {
                foreach (var w in whitelist)
                {
                    var tdef = ResolveType(w);
                    if (tdef != null)
                    {
                        KeepType(tdef);
                        continue;
                    }

                    var mref = ResolveMember(w);
                    if (mref != null)
                    {
                        _keepMembers.Add(mref.FullName);
                        // Keep the declaring type but only the referenced member (its other
                        // members stay unreachable unless the graph pulls them in).
                        if (mref.DeclaringType != null)
                            _keepTypes.Add(mref.DeclaringType.FullName);
                        _workQueue.Enqueue(mref);
                        continue;
                    }

                    var alt = AllTypes().FirstOrDefault(x => x.Name == w || x.FullName == w);
                    if (alt != null) KeepType(alt);
                }
            }

            // BFS over every kept type/member: each one pulls in the types and members it
            // references, transitively, until the whole reachable graph is recorded.
            public void Walk()
            {
                while (_workQueue.Count > 0)
                {
                    var item = _workQueue.Dequeue();

                    switch (item)
                    {
                        case TypeDefinition td: WalkType(td); break;
                        case MethodDefinition md: WalkMethod(md); break;
                        case FieldDefinition fd: WalkField(fd); break;
                        case PropertyDefinition pd: WalkProperty(pd); break;
                        case EventDefinition ed: WalkEvent(ed); break;
                        case MethodReference mref: WalkMethodReference(mref); break;
                        case FieldReference fref: WalkFieldReference(fref); break;
                    }
                }
            }

            // Removes types not in the keep set, and prunes the kept types' members.
            public void Prune()
            {
                var allTypes = AllTypes().ToList();
                foreach (var t in allTypes)
                {
                    if (!_keepTypes.Contains(t.FullName))
                    {
                        if (t.IsNested)
                            t.DeclaringType.NestedTypes.Remove(t);
                        else
                            _module.Types.Remove(t);
                        continue;
                    }

                    t.Methods.RemoveWhere(m => !_keepMembers.Contains(m.FullName) && !IsSpecialKeepMethod(m));
                    t.Fields.RemoveWhere(f => !_keepMembers.Contains(f.FullName));
                    t.Properties.RemoveWhere(p => !_keepMembers.Contains(p.FullName));
                    t.Events.RemoveWhere(e => !_keepMembers.Contains(e.FullName));
                }
            }

            private void KeepType(TypeDefinition td)
            {
                if (_keepTypes.Add(td.FullName))
                    _workQueue.Enqueue(td);
            }

            private void WalkType(TypeDefinition td)
            {
                if (td.BaseType != null) AddTypeReference(td.BaseType);
                foreach (var iface in td.Interfaces) AddTypeReference(iface.InterfaceType);

                foreach (var f in td.Fields)
                {
                    AddTypeReference(f.FieldType);
                    if (_keepMembers.Add(f.FullName)) _workQueue.Enqueue(f);
                }

                foreach (var p in td.Properties)
                {
                    AddTypeReference(p.PropertyType);
                    AddMemberReference(p.GetMethod);
                    AddMemberReference(p.SetMethod);
                    if (_keepMembers.Add(p.FullName)) _workQueue.Enqueue(p);
                }

                foreach (var e in td.Events)
                {
                    AddTypeReference(e.EventType);
                    AddMemberReference(e.AddMethod);
                    AddMemberReference(e.RemoveMethod);
                    if (_keepMembers.Add(e.FullName)) _workQueue.Enqueue(e);
                }

                foreach (var nt in td.NestedTypes)
                    KeepType(nt);

                foreach (var m in td.Methods)
                {
                    AddMethodSignature(m);
                    if (_keepMembers.Add(m.FullName)) _workQueue.Enqueue(m);
                }
            }

            private void WalkMethod(MethodDefinition md)
            {
                AddTypeReference(md.ReturnType);
                foreach (var p in md.Parameters) AddTypeReference(p.ParameterType);
                foreach (var ca in md.CustomAttributes) AddTypeReference(ca.AttributeType);
                ScanMethodBody(md);
            }

            // Records every type/member referenced by a method's body (operands + catch types).
            private void ScanMethodBody(MethodDefinition md)
            {
                if (!md.HasBody) return;

                foreach (var instr in md.Body.Instructions)
                {
                    switch (instr.Operand)
                    {
                        case MethodReference mr: AddMemberReference(mr); break;
                        case FieldReference fr: AddMemberReference(fr); break;
                        case TypeReference tr: AddTypeReference(tr); break;
                    }
                }

                foreach (var eh in md.Body.ExceptionHandlers)
                    if (eh.CatchType != null)
                        AddTypeReference(eh.CatchType);
            }

            private void WalkField(FieldDefinition fd)
            {
                AddTypeReference(fd.FieldType);
                foreach (var ca in fd.CustomAttributes) AddTypeReference(ca.AttributeType);
            }

            private void WalkProperty(PropertyDefinition pd)
            {
                AddTypeReference(pd.PropertyType);
                AddMemberReference(pd.GetMethod);
                AddMemberReference(pd.SetMethod);
            }

            private void WalkEvent(EventDefinition ed)
            {
                AddTypeReference(ed.EventType);
                AddMemberReference(ed.AddMethod);
                AddMemberReference(ed.RemoveMethod);
            }

            private void WalkMethodReference(MethodReference mref)
            {
                var def = ResolveMethodDefinition(mref);
                if (def != null)
                {
                    if (_keepMembers.Add(def.FullName)) _workQueue.Enqueue(def);
                    if (def.DeclaringType != null && _keepTypes.Add(def.DeclaringType.FullName))
                        _workQueue.Enqueue(def.DeclaringType);
                }

                AddTypeReference(mref.ReturnType);
                foreach (var p in mref.Parameters) AddTypeReference(p.ParameterType);
            }

            private void WalkFieldReference(FieldReference fref)
            {
                var def = ResolveFieldDefinition(fref);
                if (def != null)
                {
                    if (_keepMembers.Add(def.FullName)) _workQueue.Enqueue(def);
                    if (def.DeclaringType != null && _keepTypes.Add(def.DeclaringType.FullName))
                        _workQueue.Enqueue(def.DeclaringType);
                }

                AddTypeReference(fref.FieldType);
            }

            private void AddTypeReference(TypeReference tr)
            {
                if (tr == null) return;
                var resolved = ResolveTypeReference(tr);
                if (resolved != null && _keepTypes.Add(resolved.FullName))
                    _workQueue.Enqueue(resolved);
            }

            private void AddMemberReference(MemberReference mr)
            {
                if (mr == null) return;

                if (mr is MethodReference mref)
                {
                    var def = ResolveMethodDefinition(mref);
                    if (def != null && _keepMembers.Add(def.FullName)) _workQueue.Enqueue(def);
                    if (def?.DeclaringType != null && _keepTypes.Add(def.DeclaringType.FullName))
                        _workQueue.Enqueue(def.DeclaringType);
                }
                else if (mr is FieldReference fref)
                {
                    var def = ResolveFieldDefinition(fref);
                    if (def != null && _keepMembers.Add(def.FullName)) _workQueue.Enqueue(def);
                    if (def?.DeclaringType != null && _keepTypes.Add(def.DeclaringType.FullName))
                        _workQueue.Enqueue(def.DeclaringType);
                }
            }

            private void AddMethodSignature(MethodDefinition methodDef)
            {
                AddTypeReference(methodDef.ReturnType);
                foreach (var p in methodDef.Parameters) AddTypeReference(p.ParameterType);
                foreach (var ca in methodDef.CustomAttributes) AddTypeReference(ca.AttributeType);
                ScanMethodBody(methodDef);
            }

            // Try type full name first, then a scan across all (nested) types in the module.
            private TypeDefinition ResolveType(string fullName)
            {
                var t = _module.GetType(fullName);
                return t ?? AllTypes().FirstOrDefault(x => x.FullName == fullName);
            }

            // Member full name to a definition if possible.
            private MemberReference ResolveMember(string memberFullName)
            {
                foreach (var t in AllTypes())
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
            }

            private TypeDefinition ResolveTypeReference(TypeReference tr)
            {
                try
                {
                    var resolved = tr.Resolve();
                    // Only follow references that live in the assembly we're stripping.
                    if (resolved != null && resolved.Module == _module) return resolved;
                }
                catch
                {
                    // ignored; unresolved refs (e.g. cross-assembly) are simply not followed
                }

                return null;
            }

            private MethodDefinition ResolveMethodDefinition(MethodReference mr)
            {
                try
                {
                    var resolved = mr.Resolve();
                    if (resolved != null && resolved.Module == _module) return resolved;
                }
                catch
                {
                    // ignored
                }

                return null;
            }

            private FieldDefinition ResolveFieldDefinition(FieldReference fr)
            {
                try
                {
                    var resolved = fr.Resolve();
                    if (resolved != null && resolved.Module == _module) return resolved;
                }
                catch
                {
                    // ignored
                }

                return null;
            }

            private IEnumerable<TypeDefinition> AllTypes()
            {
                return _module.Types.SelectMany(FlattenNested);
            }

            private static IEnumerable<TypeDefinition> FlattenNested(TypeDefinition td)
            {
                yield return td;
                foreach (var nested in td.NestedTypes)
                    foreach (var z in FlattenNested(nested))
                        yield return z;
            }

            // Keep static constructors even when nothing references them; .cctor blocks
            // can have side effects a stripped type may rely on.
            private static bool IsSpecialKeepMethod(MethodDefinition m)
            {
                return m.IsConstructor && m.IsStatic;
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
