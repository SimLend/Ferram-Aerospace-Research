using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FerramAerospaceResearch.Interfaces;

namespace FerramAerospaceResearch.Reflection
{
    public class NodeReflection : ValueReflection
    {
        private const BindingFlags PublicFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
        private const BindingFlags PrivateFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags AllFlags = PublicFlags | PrivateFlags;

        private static Dictionary<Type, List<Type>> children;

        public static Dictionary<Type, List<Type>> Children
        {
            get
            {
                if (children != null)
                    return children;

                children = new Dictionary<Type, List<Type>>();

                foreach (Pair<ConfigNodeAttribute, Type> pair in ReflectionUtils.FindAttribute<ConfigNodeAttribute>())
                {
                    // FARLogger.TraceFormat("{0} parent: {1}", pair.Second.Name, pair.First.Parent?.Name);
                    if (pair.First.Parent is null)
                        continue;

                    if (!children.TryGetValue(pair.First.Parent, out List<Type> list))
                    {
                        list = new List<Type>();
                        children.Add(pair.First.Parent, list);
                    }

                    FARLogger.Assert(pair.Second.IsStatic() || ReflectionUtils.FindInstance(pair.Second) != null,
                                     "Can only attach static types and singletons");
                    list.Add(pair.Second);
                }

                return children;
            }
        }

        /// <summary>
        /// All the created <see cref="NodeReflection"/> for each type
        /// </summary>
        private static readonly Dictionary<Type, NodeReflection> nodes = new Dictionary<Type, NodeReflection>();

        /// <summary>
        /// Reflected lists in this config node
        /// </summary>
        public readonly List<ListValueReflection> ListValues = new List<ListValueReflection>();

        /// <summary>
        /// Reflected subnodes in this config node
        /// </summary>
        public readonly List<NodeReflection> Nodes = new List<NodeReflection>();

        /// <summary>
        /// Reflected values in this node
        /// </summary>
        public readonly List<ValueReflection> Values = new List<ValueReflection>();

        private NodeReflection(Type type, string name)
        {
            ValueType = type;
            Name = name;
        }

        public NodeReflection(NodeReflection other, string name = null, MemberInfo mi = null) : base(other, mi)
        {
            Name = name;
            Id = other.Id;
            AllowMultiple = other.AllowMultiple;

            // copy reflected values
            Values.Capacity = other.Values.Count;
            foreach (ValueReflection reflection in other.Values)
                Values.Add(reflection);

            // copy reflected list values
            ListValues.Capacity = other.ListValues.Count;
            foreach (ListValueReflection list in other.ListValues)
                ListValues.Add(list);

            // copy reflected nodes
            Nodes.Capacity = other.Nodes.Count;
            foreach (NodeReflection node in other.Nodes)
                Nodes.Add(node);
        }

        /// <summary>
        /// Id of this config node
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Whether this node can be declared multiple times at the top level scope
        /// </summary>
        public bool AllowMultiple { get; private set; }

        /// <summary>
        /// Whether this node is root
        /// </summary>
        public bool IsRootNode { get; private set; }

        /// <summary>
        /// Whether this node should be saved
        /// </summary>
        public bool ShouldSave { get; private set; }

        private object FindOrMakeInstance()
        {
            // handle static classes
            if (ValueType.IsStatic())
                return null;
            object instance = ReflectionUtils.FindInstance(ValueType);
            if (instance != null)
                return instance;
            FARLogger.TraceFormat("Instance of {0} was not provided and could not find one. Creating one", ValueType);
            instance = ReflectionUtils.CreateInstance(ValueType);

            FARLogger.Assert(instance != null, "Could not create instance");

            return instance;
        }

        /// <inheritdoc/>
        protected override void OnSetup(Type type, ConfigValueAttribute attribute)
        {
            ConfigNodeAttribute node = type.GetCustomAttribute<ConfigNodeAttribute>();
            Id = node?.Id ?? type.Name;

            // there should only be 1 root node for one type
            if (node != null)
            {
                IsRootNode = node.IsRoot;
                ShouldSave = node.ShouldSave;
                if (node.IsRoot)
                    AllowMultiple = node.AllowMultiple;
            }

            // if this is a member of some type
            if (attribute != null)
            {
                FARLogger.AssertFormat(!string.IsNullOrEmpty(attribute.Name),
                                       "Nested nodes required ConfigValue.Name to be set");
                Name = attribute.Name;
            }

            // get all public fields
            var fields = new HashSet<FieldInfo>(type.GetFields(PublicFlags));

            // and add all the other fields that declare ConfigValueAttribute
            fields.UnionWith(type.GetFieldsWithAttribute<ConfigValueAttribute>(flags: AllFlags)
                                 .Select(pair => pair.Second));

            // only get properties that declare ConfigValueAttribute
            var properties = new List<PropertyInfo>(type.GetPropertiesWithAttribute<ConfigValueAttribute>(AllFlags)
                                                        .Select(pair => pair.Second));

            FARLogger.TraceFormat("Found {0} fields and {1} properties in {2}",
                                  fields.Count.ToString(),
                                  properties.Count.ToString(),
                                  type);

            foreach (FieldInfo fi in fields)
                SetupType(fi, fi.FieldType);

            foreach (PropertyInfo pi in properties)
                SetupType(pi, pi.PropertyType);

            if (!Children.TryGetValue(type, out List<Type> list))
                return;
            foreach (Type subnode in list)
                SetupType(null, subnode);
        }

        private void SetupType(MemberInfo mi, Type memberType)
        {
            // if ignored, nothing to do
            if (mi?.GetCustomAttribute<ConfigValueIgnoreAttribute>() != null)
                return;

            // check if the type is a node and contains ConfigValueAttribute
            ConfigNodeAttribute node = memberType.GetCustomAttribute<ConfigNodeAttribute>();
            ConfigValueAttribute value = mi?.GetCustomAttribute<ConfigValueAttribute>();

            // try to get the list value type
            Type listValueType = ReflectionUtils.ListType(ReflectionUtils.ConfigValueType(memberType) ?? memberType);

            if (listValueType != null)
            {
                // is a list
                var reflection = ListValueReflection.Create(mi, value, listValueType);

                ListValues.Add(reflection);
                FARLogger.TraceFormat("Added list value '{1} -> <{0}, {2}>'",
                                      reflection.Name ?? "{null}",
                                      reflection.NodeId ?? "{null}",
                                      reflection.ValueType);
            }
            else if (node == null)
            {
                // not a node or a list -> simple value
                ValueReflection reflection = Create(mi, value);
                Values.Add(reflection);
                FARLogger.TraceFormat("Added value '{0} -> {1}'", reflection.Name, reflection.ValueType);
            }
            else
            {
                // ConfigValue name
                string name = value?.Name;

                // get clone or create new reflection for the type
                NodeReflection nodeReflection = GetReflection(memberType, true, name, mi);
                Nodes.Add(nodeReflection);
                FARLogger.TraceFormat("Added node '{1} -> <{0}, {2}>'",
                                      name ?? "{null}",
                                      nodeReflection.Id,
                                      nodeReflection.ValueType);
            }
        }

        public static NodeReflection GetReflection(
            Type type,
            bool clone = false,
            string cloneName = null,
            MemberInfo mi = null
        )
        {
            // get or create NodeReflection and cache it
            if (nodes.TryGetValue(type, out NodeReflection original))
                return !clone ? original : new NodeReflection(original, cloneName, mi);
            original = Factory(new NodeReflection(type, cloneName), mi);
            nodes.Add(type, original);

            return !clone ? original : new NodeReflection(original, cloneName, mi);
        }

        private int Apply(ref object owner, NodeVisitor visitor)
        {
            int count = 0;
            FARLogger.TraceFormat("Applying visitor to config node {0}[{1}]", Id, Name ?? "{null}");
            foreach (ValueReflection value in Values)
            {
                FARLogger.TraceFormat("Visiting value {0}[{1}].{2}", Id, Name, value.Name);
                try
                {
                    visitor.VisitValue(owner, value);
                }
                catch (Exception e)
                {
                    FARLogger.ExceptionFormat(e, "Exception loading value {0} in {1}", value.Name, value.DeclaringType);
                    count++;
                }
            }

            foreach (ListValueReflection reflection in ListValues)
            {
                if (reflection.IsNodeValue)
                {
                    NodeReflection nodeReflection = GetReflection(reflection.ValueType);

                    FARLogger.TraceFormat("Visiting list nodes {0}[{1}].{2}[{3}]",
                                          Id,
                                          Name ?? "{null}",
                                          reflection.NodeId,
                                          reflection.Name ?? "{null}");
                    try
                    {
                        visitor.VisitNodeList(owner, reflection, nodeReflection);
                    }
                    catch (Exception e)
                    {
                        FARLogger.ExceptionFormat(e,
                                                  "Exception loading node ({2}) list {0} in {1}",
                                                  reflection.Name,
                                                  reflection.DeclaringType,
                                                  reflection.NodeId);
                        count++;
                    }
                }
                else
                {
                    FARLogger.TraceFormat("Visiting list values {0}[{1}].{2}", Id, Name ?? "{null}", reflection.Name);
                    try
                    {
                        visitor.VisitValueList(owner, reflection);
                    }
                    catch (Exception e)
                    {
                        FARLogger.ExceptionFormat(e,
                                                  "Exception loading value list {0} in {1}",
                                                  reflection.Name,
                                                  reflection.DeclaringType);
                        count++;
                    }
                }
            }

            foreach (NodeReflection node in Nodes)
            {
                FARLogger.TraceFormat("Visiting subnode {0}[{1}].{2}[{3}]",
                                      Id,
                                      Name ?? "{null}",
                                      node.Id,
                                      node.Name ?? "{null}");
                try
                {
                    visitor.VisitNode(owner, node);
                }
                catch (Exception e)
                {
                    FARLogger.ExceptionFormat(e,
                                              "Exception loading node {2}[{0}] in {1}",
                                              node.Name,
                                              node.DeclaringType,
                                              node.Id);
                    count++;
                }
            }

            return count;
        }

        private const BindingFlags SpecialMethodFlags = BindingFlags.Public | BindingFlags.Static;

        private struct TypeMethodPair
        {
            public Type type;
            public string name;

            public override int GetHashCode()
            {
                // https://stackoverflow.com/a/1646913/13262469
                // or use Microsoft.Bcl.HashCode NuGet package
                int hash = 17;
                hash = hash * 31 + type.GetHashCode();
                hash = hash * 31 + name.GetHashCode();
                return hash;
            }
        }

        private static readonly Dictionary<TypeMethodPair, MethodInfo> specialMethods = new();

        private static MethodInfo FindSpecialMethod(Type type, string name)
        {
            TypeMethodPair pair = new()
            {
                type = type,
                name = name
            };
            if (specialMethods.TryGetValue(pair, out MethodInfo info))
                return info;

            info = type.GetMethod(name, SpecialMethodFlags);
            info = info?.GetParameters().Length == 0 ? info : null;

            specialMethods.Add(pair, info);
            return info;
        }

        public int Save(INodeSaver saver, object instance)
        {
            var save = new NodeReader { Saver = saver };
            var node = instance as FerramAerospaceResearch.Interfaces.IConfigNode;

            if (node is null)
                FindSpecialMethod(ValueType, "BeforeSaved")?.Invoke(null, null);
            else
                node.BeforeSaved();

            int count = Apply(ref instance, save);

            if (node is null)
                FindSpecialMethod(ValueType, "AfterSaved")?.Invoke(null, null);
            else
                node.AfterSaved();
            return count;
        }

        public int Load(INodeLoader loader, ref object instance)
        {
            var load = new NodeLoader { Loader = loader };
            var node = instance as FerramAerospaceResearch.Interfaces.IConfigNode;

            if (node is null)
                FindSpecialMethod(ValueType, "BeforeLoaded")?.Invoke(null, null);
            else
                node.BeforeLoaded();

            int count = Apply(ref instance, load);

            if (node is null)
                FindSpecialMethod(ValueType, "AfterLoaded")?.Invoke(null, null);
            else
                node.AfterLoaded();
            return count;
        }

        public object Load(INodeLoader loader, out int errors)
        {
            object instance = FindOrMakeInstance();
            errors = Load(loader, ref instance);
            return instance;
        }

        private abstract class NodeVisitor
        {
            public abstract void VisitValue(object owner, ValueReflection reflection);
            public abstract void VisitValueList(object owner, ListValueReflection reflection);

            public abstract void VisitNodeList(
                object owner,
                ListValueReflection reflection,
                NodeReflection nodeReflection
            );

            public abstract void VisitNode(object owner, NodeReflection reflection);

            protected static void CopyList(IList from, IList to, Type valueType)
            {
                if (from is null || to is null)
                    return;

                object def = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;

                int count = from.Count;
                if (to.IsFixedSize)
                {
                    count = Math.Min(count, to.Count);
                    for (int i = 0; i < count; i++)
                        to[i] = from[i] ?? def;
                }
                else
                {
                    to.Clear();
                    for (int i = 0; i < count; i++)
                        to.Add(from[i] ?? def);
                }
            }
        }

        private sealed class NodeLoader : NodeVisitor
        {
            public INodeLoader Loader { get; set; }

            /// <inheritdoc />
            public override void VisitValue(object owner, ValueReflection reflection)
            {
                if (Loader.OnValue(reflection, out object value))
                    reflection.SetMember(owner, value, true);
            }

            /// <inheritdoc />
            public override void VisitValueList(object owner, ListValueReflection reflection)
            {
                if (Loader.OnValueList(reflection, out IList list))
                    CopyList(list, reflection.GetMember(owner) as IList, reflection.ValueType);
            }

            /// <inheritdoc />
            public override void VisitNodeList(
                object owner,
                ListValueReflection reflection,
                NodeReflection nodeReflection
            )
            {
                if (Loader.OnNodeList(reflection, nodeReflection, out IList list))
                    CopyList(list, reflection.GetMember(owner) as IList, reflection.ValueType);
            }

            /// <inheritdoc />
            public override void VisitNode(object owner, NodeReflection reflection)
            {
                if (Loader.OnNode(reflection.GetMember(owner), reflection, out object value) && value != null)
                    reflection.SetMember(owner, value, true);
            }
        }

        private sealed class NodeReader : NodeVisitor
        {
            public INodeSaver Saver { get; set; }

            /// <inheritdoc />
            public override void VisitValue(object owner, ValueReflection reflection)
            {
                if (Saver.OnValue(reflection.GetMember(owner), reflection, out object newValue))
                    reflection.SetMember(owner, newValue, true);
            }

            /// <inheritdoc />
            public override void VisitValueList(object owner, ListValueReflection reflection)
            {
                if (!(reflection.GetMember(owner) is IList member))
                    return;

                for (int i = 0; i < member.Count; i++)
                {
                    if (Saver.OnListValue(i, member[i], reflection, out object newValue))
                        member[i] = newValue;
                }
            }

            /// <inheritdoc />
            public override void VisitNodeList(
                object owner,
                ListValueReflection reflection,
                NodeReflection nodeReflection
            )
            {
                if (!(reflection.GetMember(owner) is IList member))
                    return;

                for (int i = 0; i < member.Count; i++)
                {
                    if (Saver.OnListNode(i, member[i], reflection, nodeReflection, out object newValue))
                        member[i] = newValue;
                }
            }

            /// <inheritdoc />
            public override void VisitNode(object owner, NodeReflection reflection)
            {
                if (Saver.OnNode(reflection.GetMember(owner), reflection, out object newValue))
                    reflection.SetMember(owner, newValue, true);
            }
        }
    }
}
