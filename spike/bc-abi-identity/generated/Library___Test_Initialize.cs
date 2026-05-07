namespace Microsoft.Dynamics.Nav.BusinessApplication
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Dynamics.Nav.Common.Language;
    using Microsoft.Dynamics.Nav.EventSubscription;
    using Microsoft.Dynamics.Nav.Runtime;
    using Microsoft.Dynamics.Nav.Runtime.Extensions;
    using Microsoft.Dynamics.Nav.Runtime.Report;
    using Microsoft.Dynamics.Nav.Types;
    using Microsoft.Dynamics.Nav.Types.Exceptions;
    using Microsoft.Dynamics.Nav.Types.Metadata;

    [NavCodeunitOptions(0, 0, CodeunitSubType.Normal, false)]
    public sealed class Codeunit132250 : NavCodeunit
    {
        public Codeunit132250(ITreeObject parent) : base(parent, 132250)
        {
        }

        public override string ObjectName => "Library - Test Initialize";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 1227409270:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "OnTestInitialize");
                    OnTestInitialize((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 1468874462:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "OnBeforeTestSuiteInitialize");
                    OnBeforeTestSuiteInitialize((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -185043279:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "OnAfterTestSuiteInitialize");
                    OnAfterTestSuiteInitialize((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 132250);
                    break;
            }

            return default;
        }

        public static Codeunit132250 __Construct(ITreeObject parent)
        {
            return new Codeunit132250(parent);
        }

        [NavEvent(NavEventType.Integration, true, false), NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3562946904 - Method 1055458630")]
        public void OnAfterTestSuiteInitialize(int callerCodeunitID)
        {
            if (OnAfterTestSuiteInitialize_Scope.\u03b3eventScope == null && !this.Session.IsEventSessionRecorderEnabled)
                return;
            using (OnAfterTestSuiteInitialize_Scope \u03b2scope = new OnAfterTestSuiteInitialize_Scope(this, callerCodeunitID))
                \u03b2scope.RunEvent();
        }

        [NavName("OnAfterTestSuiteInitialize")]
        [SignatureSpan(5911034641842216L)]
        [SourceSpans(6473954530623496L)]
        private sealed class OnAfterTestSuiteInitialize_Scope : NavEventMethodScope<Codeunit132250>
        {
            public static uint \u03b1scopeId;
            public static NavEventScope \u03b3eventScope;
            [NavName("CallerCodeunitID")]
            public int callerCodeunitID;
            protected override uint RawScopeId { get => OnAfterTestSuiteInitialize_Scope.\u03b1scopeId; set => OnAfterTestSuiteInitialize_Scope.\u03b1scopeId = value; }
            public override NavEventScope EventScope { get => OnAfterTestSuiteInitialize_Scope.\u03b3eventScope; set => OnAfterTestSuiteInitialize_Scope.\u03b3eventScope = value; }
            public override int MethodId { get => -185043279; }

            internal OnAfterTestSuiteInitialize_Scope(Codeunit132250 \u03b2parent, int callerCodeunitID) : base(\u03b2parent)
            {
                this.callerCodeunitID = callerCodeunitID;
            }
        }

        [NavEvent(NavEventType.Integration, false, false), NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3562946904 - Method 3403397708")]
        public void OnBeforeTestSuiteInitialize(int callerCodeunitID)
        {
            if (OnBeforeTestSuiteInitialize_Scope.\u03b3eventScope == null && !this.Session.IsEventSessionRecorderEnabled)
                return;
            using (OnBeforeTestSuiteInitialize_Scope \u03b2scope = new OnBeforeTestSuiteInitialize_Scope(this, callerCodeunitID))
                \u03b2scope.RunEvent();
        }

        [NavName("OnBeforeTestSuiteInitialize")]
        [SignatureSpan(4503659757961257L)]
        [SourceSpans(5066579646742536L)]
        private sealed class OnBeforeTestSuiteInitialize_Scope : NavEventMethodScope<Codeunit132250>
        {
            public static uint \u03b1scopeId;
            public static NavEventScope \u03b3eventScope;
            [NavName("CallerCodeunitID")]
            public int callerCodeunitID;
            protected override uint RawScopeId { get => OnBeforeTestSuiteInitialize_Scope.\u03b1scopeId; set => OnBeforeTestSuiteInitialize_Scope.\u03b1scopeId = value; }
            public override NavEventScope EventScope { get => OnBeforeTestSuiteInitialize_Scope.\u03b3eventScope; set => OnBeforeTestSuiteInitialize_Scope.\u03b3eventScope = value; }
            public override int MethodId { get => 1468874462; }

            internal OnBeforeTestSuiteInitialize_Scope(Codeunit132250 \u03b2parent, int callerCodeunitID) : base(\u03b2parent)
            {
                this.callerCodeunitID = callerCodeunitID;
            }
        }

        protected override void OnRun([NavByReferenceAttribute][NavObjectId(ObjectId = 0)] INavRecordHandle \u03b5rec)
        {
            using (OnRun_Scope \u03b2scope = new OnRun_Scope(this, \u03b5rec))
                \u03b2scope.Run();
        }

        [NavName("OnRun")]
        [SignatureSpan(1688901400264721L)]
        [SourceSpans(2251829878980616L)]
        private sealed class OnRun_Scope : NavTriggerMethodScope<Codeunit132250>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => OnRun_Scope.\u03b1scopeId; set => OnRun_Scope.\u03b1scopeId = value; }

            internal OnRun_Scope(Codeunit132250 \u03b2parent, [NavByReferenceAttribute][NavObjectId(ObjectId = 0)] INavRecordHandle \u03b5rec) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavEvent(NavEventType.Integration, false, false), NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3562946904 - Method 1087174482")]
        public void OnTestInitialize(int callerCodeunitID)
        {
            if (OnTestInitialize_Scope.\u03b3eventScope == null && !this.Session.IsEventSessionRecorderEnabled)
                return;
            using (OnTestInitialize_Scope \u03b2scope = new OnTestInitialize_Scope(this, callerCodeunitID))
                \u03b2scope.RunEvent();
        }

        [NavName("OnTestInitialize")]
        [SignatureSpan(3096284874080286L)]
        [SourceSpans(3659204762861576L)]
        private sealed class OnTestInitialize_Scope : NavEventMethodScope<Codeunit132250>
        {
            public static uint \u03b1scopeId;
            public static NavEventScope \u03b3eventScope;
            [NavName("CallerCodeunitID")]
            public int callerCodeunitID;
            protected override uint RawScopeId { get => OnTestInitialize_Scope.\u03b1scopeId; set => OnTestInitialize_Scope.\u03b1scopeId = value; }
            public override NavEventScope EventScope { get => OnTestInitialize_Scope.\u03b3eventScope; set => OnTestInitialize_Scope.\u03b3eventScope = value; }
            public override int MethodId { get => 1227409270; }

            internal OnTestInitialize_Scope(Codeunit132250 \u03b2parent, int callerCodeunitID) : base(\u03b2parent)
            {
                this.callerCodeunitID = callerCodeunitID;
            }
        }
    }
}
