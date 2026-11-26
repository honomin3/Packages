// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal static class ScaleDiagnostics
    {
        private static readonly Localizer Messages = new Localizer("en-US", () => new List<(string, Func<string, string>)> {
            ("en-US",key=>key=="ass.title" ? "Armature Scale Sync: {0}" : key=="ass.title:description" ? "{0}" : null)
        });
        private sealed class ScaleError : SimpleError
        {
            private readonly Diagnostic diagnostic;
            public ScaleError(Diagnostic diagnostic)
            {
                this.diagnostic = diagnostic;
            }
            public override Localizer Localizer => Messages;
            public override string TitleKey => "ass.title";
            public override string[] TitleSubst => new[] { diagnostic.Code };
            public override string[] DetailsSubst => new[] { diagnostic.Message };
            public override ErrorSeverity Severity => diagnostic.Severity == Core.Severity.Error ? ErrorSeverity.Error : ErrorSeverity.NonFatal;
        }
        internal static void Report(Diagnostic diagnostic, UnityEngine.Object related)
        {
            var error = new ScaleError(diagnostic);
            if (related)
                error.AddReference(ObjectRegistry.GetReference(related));
            ErrorReport.ReportError(error);
        }
    }
}
