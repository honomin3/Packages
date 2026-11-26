// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Honomin.ArmatureScaleSync.Core
{
    public enum Severity
    {
        Warning, Error
    }
    public enum Route
    {
        Independent, BoneProxy, Native, Merged, ProjectedMerge, ProjectedProxy
    }
    public sealed class Diagnostic
    {
        public string Code
        {
            get;
        }
        public Severity Severity
        {
            get;
        }
        public string Target
        {
            get;
        }
        public string Message
        {
            get;
        }
        public Diagnostic(string code, Severity severity, string target, string message)
        {
            Code = code;
            Severity = severity;
            Target = target;
            Message = message;
        }
        public override string ToString() => Code + ": " + Message;
    }
    public sealed class Bone
    {
        public string Id
        {
            get;
        }
        public string Parent
        {
            get;
        }
        public Bone(string id, string parent = null)
        {
            Id = id;
            Parent = parent;
        }
    }
    public sealed class Declaration
    {
        public string Id
        {
            get;
        }
        public string Bone
        {
            get;
        }
        public Scale3 Multiplier
        {
            get;
        }
        public bool Advanced
        {
            get;
        }
        public Declaration(string id, string bone, Scale3 multiplier, bool advanced = false)
        {
            Id = id;
            Bone = bone;
            Multiplier = multiplier;
            Advanced = advanced;
        }
    }
    public sealed class Binding
    {
        public string Source
        {
            get;
        }
        public string Target
        {
            get;
        }
        public Route Route
        {
            get;
        }
        public Binding(string source, string target, Route route)
        {
            Source = source;
            Target = target;
            Route = route;
        }
    }
    public sealed class ScaleWrite
    {
        public string Target
        {
            get;
        }
        public Scale3 Multiplier
        {
            get;
        }
        public IReadOnlyList<string> Origins
        {
            get;
        }
        public ScaleWrite(string target, Scale3 multiplier, IEnumerable<string> origins)
        {
            Target = target;
            Multiplier = multiplier;
            Origins = new ReadOnlyCollection<string>(origins.OrderBy(o => o, System.StringComparer.Ordinal).ToArray());
        }
    }
    public sealed class ScalePlan
    {
        public IReadOnlyList<ScaleWrite> Writes
        {
            get;
        }
        public IReadOnlyList<Diagnostic> Diagnostics
        {
            get;
        }
        public ScalePlan(IEnumerable<ScaleWrite> writes, IEnumerable<Diagnostic> diagnostics)
        {
            Writes = new ReadOnlyCollection<ScaleWrite>(writes.ToArray());
            Diagnostics = new ReadOnlyCollection<Diagnostic>(diagnostics.ToArray());
        }
    }
}
