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
    public sealed class Codeunit131004 : NavCodeunit
    {
        public Codeunit131004(ITreeObject parent) : base(parent, 131004)
        {
        }

        public override string ObjectName => "Library - Variable Storage";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 1947004069:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "Enqueue");
                    Enqueue(ALCompiler.ObjectToExactNavValue<NavVariant>(args[0]));
                    break;
                case -42280326:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "DequeueText");
                    return DequeueText();
                    break;
                case 43444490:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "DequeueInteger");
                    return DequeueInteger();
                    break;
                case 830533232:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "DequeueDecimal");
                    return DequeueDecimal();
                    break;
                case -366652263:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "DequeueBoolean");
                    return DequeueBoolean();
                    break;
                case 333372006:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "DequeueDate");
                    return DequeueDate();
                    break;
                case -1000230526:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "DequeueVariant");
                    return DequeueVariant((NavScope)args[0]);
                    break;
                case 251739948:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "AssertEmpty");
                    AssertEmpty();
                    break;
                case 1166893731:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "Clear");
                    Clear();
                    break;
                case -1220804299:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "IsEmpty");
                    return IsEmpty();
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 131004);
                    break;
            }

            return default;
        }

        public static Codeunit131004 __Construct(ITreeObject parent)
        {
            return new Codeunit131004(parent);
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 777978906")]
        public void AssertEmpty()
        {
            using (AssertEmpty_Scope_251739948 \u03b2scope = new AssertEmpty_Scope_251739948(this))
                \u03b2scope.Run();
        }

        [NavName("AssertEmpty")]
        [SignatureSpan(9007259386380313L)]
        [SourceSpans(9570179275161608L)]
        private sealed class AssertEmpty_Scope_251739948 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => AssertEmpty_Scope_251739948.\u03b1scopeId; set => AssertEmpty_Scope_251739948.\u03b1scopeId = value; }

            internal AssertEmpty_Scope_251739948(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 3208987521")]
        public void Clear()
        {
            using (Clear_Scope_1166893731 \u03b2scope = new Clear_Scope_1166893731(this))
                \u03b2scope.Run();
        }

        [NavName("Clear")]
        [SignatureSpan(10133159293485075L)]
        [SourceSpans(10696079182266376L)]
        private sealed class Clear_Scope_1166893731 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => Clear_Scope_1166893731.\u03b1scopeId; set => Clear_Scope_1166893731.\u03b1scopeId = value; }

            internal Clear_Scope_1166893731(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 2540143396")]
        public bool DequeueBoolean()
        {
            using (DequeueBoolean_Scope__366652263 \u03b2scope = new DequeueBoolean_Scope__366652263(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DequeueBoolean")]
        [SignatureSpan(5629559665066012L)]
        [SourceSpans(6192479553847304L)]
        private sealed class DequeueBoolean_Scope__366652263 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public bool \u03b3retVal = default(bool);
            protected override uint RawScopeId { get => DequeueBoolean_Scope__366652263.\u03b1scopeId; set => DequeueBoolean_Scope__366652263.\u03b1scopeId = value; }

            internal DequeueBoolean_Scope__366652263(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 202792837")]
        public NavDate DequeueDate()
        {
            using (DequeueDate_Scope_333372006 \u03b2scope = new DequeueDate_Scope_333372006(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DequeueDate")]
        [SignatureSpan(6755459572170777L)]
        [SourceSpans(7318379460952072L)]
        private sealed class DequeueDate_Scope_333372006 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => DequeueDate_Scope_333372006.\u03b1scopeId; set => DequeueDate_Scope_333372006.\u03b1scopeId = value; }

            internal DequeueDate_Scope_333372006(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 1889302759")]
        public Decimal18 DequeueDecimal()
        {
            using (DequeueDecimal_Scope_830533232 \u03b2scope = new DequeueDecimal_Scope_830533232(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DequeueDecimal")]
        [SignatureSpan(4503659757961244L)]
        [SourceSpans(5066579646742536L)]
        private sealed class DequeueDecimal_Scope_830533232 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => DequeueDecimal_Scope_830533232.\u03b1scopeId; set => DequeueDecimal_Scope_830533232.\u03b1scopeId = value; }

            internal DequeueDecimal_Scope_830533232(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 3066121602")]
        public int DequeueInteger()
        {
            using (DequeueInteger_Scope_43444490 \u03b2scope = new DequeueInteger_Scope_43444490(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DequeueInteger")]
        [SignatureSpan(3377759850856476L)]
        [SourceSpans(3940679739637768L)]
        private sealed class DequeueInteger_Scope_43444490 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => DequeueInteger_Scope_43444490.\u03b1scopeId; set => DequeueInteger_Scope_43444490.\u03b1scopeId = value; }

            internal DequeueInteger_Scope_43444490(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 1828809674")]
        public NavText DequeueText()
        {
            using (DequeueText_Scope__42280326 \u03b2scope = new DequeueText_Scope__42280326(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DequeueText")]
        [SignatureSpan(2251859943751705L)]
        [SourceSpans(2814779832533000L)]
        private sealed class DequeueText_Scope__42280326 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            protected override uint RawScopeId { get => DequeueText_Scope__42280326.\u03b1scopeId; set => DequeueText_Scope__42280326.\u03b1scopeId = value; }

            internal DequeueText_Scope__42280326(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 3188931400")]
        public NavVariant DequeueVariant(NavScope \u03b3ReturnValueParent)
        {
            using (DequeueVariant_Scope__1000230526 \u03b2scope = new DequeueVariant_Scope__1000230526(this, \u03b3ReturnValueParent))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DequeueVariant")]
        [SignatureSpan(7881359479275548L)]
        [SourceSpans(8444279368056840L)]
        private sealed class DequeueVariant_Scope__1000230526 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavVariant \u03b3retVal;
            protected override uint RawScopeId { get => DequeueVariant_Scope__1000230526.\u03b1scopeId; set => DequeueVariant_Scope__1000230526.\u03b1scopeId = value; }

            internal DequeueVariant_Scope__1000230526(Codeunit131004 \u03b2parent, NavScope \u03b3ReturnValueParent) : base(\u03b2parent)
            {
                this.\u03b3retVal = NavVariant.Default(\u03b3ReturnValueParent);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 1191337068")]
        public void Enqueue(NavVariant value)
        {
            using (Enqueue_Scope_1947004069 \u03b2scope = new Enqueue_Scope_1947004069(this, value))
                \u03b2scope.Run();
        }

        [NavName("Enqueue")]
        [SignatureSpan(1125960036646933L)]
        [SourceSpans(1688879925428232L)]
        private sealed class Enqueue_Scope_1947004069 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [NavName("Value")]
            public NavVariant value;
            protected override uint RawScopeId { get => Enqueue_Scope_1947004069.\u03b1scopeId; set => Enqueue_Scope_1947004069.\u03b1scopeId = value; }

            internal Enqueue_Scope_1947004069(Codeunit131004 \u03b2parent, NavVariant value) : base(\u03b2parent)
            {
                this.value = value.ALByValue(this);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1656420359 - Method 3359376494")]
        public bool IsEmpty()
        {
            using (IsEmpty_Scope__1220804299 \u03b2scope = new IsEmpty_Scope__1220804299(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("IsEmpty")]
        [SignatureSpan(11259059200589845L)]
        [SourceSpans(11821979089371144L)]
        private sealed class IsEmpty_Scope__1220804299 : NavMethodScope<Codeunit131004>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public bool \u03b3retVal = default(bool);
            protected override uint RawScopeId { get => IsEmpty_Scope__1220804299.\u03b1scopeId; set => IsEmpty_Scope__1220804299.\u03b1scopeId = value; }

            internal IsEmpty_Scope__1220804299(Codeunit131004 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }
    }
}
