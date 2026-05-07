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
    public sealed class Codeunit131100 : NavCodeunit
    {
        public Codeunit131100(ITreeObject parent) : base(parent, 131100)
        {
        }

        public override string ObjectName => "AL Runner Config";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 602738078:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "SetCompanyName");
                    SetCompanyName(ALCompiler.ObjectToExactNavValue<NavText>(args[0]));
                    break;
                case 366076283:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GetCompanyName");
                    return GetCompanyName();
                    break;
                case -484387391:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "SetCompanyDisplayName");
                    SetCompanyDisplayName(ALCompiler.ObjectToExactNavValue<NavText>(args[0]));
                    break;
                case -1189373726:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GetCompanyDisplayName");
                    return GetCompanyDisplayName();
                    break;
                case 1650406863:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "SetCompanyUrlName");
                    SetCompanyUrlName(ALCompiler.ObjectToExactNavValue<NavText>(args[0]));
                    break;
                case 533024986:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GetCompanyUrlName");
                    return GetCompanyUrlName();
                    break;
                case 586707953:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "SetCompanyId");
                    SetCompanyId((System.Guid)ALCompiler.ObjectToGuid(args[0]));
                    break;
                case 1294872756:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GetCompanyId");
                    return GetCompanyId();
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 131100);
                    break;
            }

            return default;
        }

        public static Codeunit131100 __Construct(ITreeObject parent)
        {
            return new Codeunit131100(parent);
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 370575948")]
        public NavText GetCompanyDisplayName()
        {
            using (GetCompanyDisplayName_Scope__1189373726 \u03b2scope = new GetCompanyDisplayName_Scope__1189373726(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetCompanyDisplayName")]
        [SignatureSpan(8444309432827939L)]
        [SourceSpans(9007229321609224L)]
        private sealed class GetCompanyDisplayName_Scope__1189373726 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            protected override uint RawScopeId { get => GetCompanyDisplayName_Scope__1189373726.\u03b1scopeId; set => GetCompanyDisplayName_Scope__1189373726.\u03b1scopeId = value; }

            internal GetCompanyDisplayName_Scope__1189373726(Codeunit131100 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 371938177")]
        public System.Guid GetCompanyId()
        {
            using (GetCompanyId_Scope_1294872756 \u03b2scope = new GetCompanyId_Scope_1294872756(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetCompanyId")]
        [SignatureSpan(16888558736113690L)]
        [SourceSpans(17451478624894984L)]
        private sealed class GetCompanyId_Scope_1294872756 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public System.Guid \u03b3retVal = default(System.Guid);
            protected override uint RawScopeId { get => GetCompanyId_Scope_1294872756.\u03b1scopeId; set => GetCompanyId_Scope_1294872756.\u03b1scopeId = value; }

            internal GetCompanyId_Scope_1294872756(Codeunit131100 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 698170077")]
        public NavText GetCompanyName()
        {
            using (GetCompanyName_Scope_366076283 \u03b2scope = new GetCompanyName_Scope_366076283(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetCompanyName")]
        [SignatureSpan(4222184781185052L)]
        [SourceSpans(4785104669966344L)]
        private sealed class GetCompanyName_Scope_366076283 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            protected override uint RawScopeId { get => GetCompanyName_Scope_366076283.\u03b1scopeId; set => GetCompanyName_Scope_366076283.\u03b1scopeId = value; }

            internal GetCompanyName_Scope_366076283(Codeunit131100 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 2465017424")]
        public NavText GetCompanyUrlName()
        {
            using (GetCompanyUrlName_Scope_533024986 \u03b2scope = new GetCompanyUrlName_Scope_533024986(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetCompanyUrlName")]
        [SignatureSpan(12666434084470815L)]
        [SourceSpans(13229353973252104L)]
        private sealed class GetCompanyUrlName_Scope_533024986 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            protected override uint RawScopeId { get => GetCompanyUrlName_Scope_533024986.\u03b1scopeId; set => GetCompanyUrlName_Scope_533024986.\u03b1scopeId = value; }

            internal GetCompanyUrlName_Scope_533024986(Codeunit131100 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 3868828038")]
        public void SetCompanyDisplayName(NavText name)
        {
            using (SetCompanyDisplayName_Scope__484387391 \u03b2scope = new SetCompanyDisplayName_Scope__484387391(this, name))
                \u03b2scope.Run();
        }

        [NavName("SetCompanyDisplayName")]
        [SignatureSpan(6473984595394595L)]
        [SourceSpans(7036904484175880L)]
        private sealed class SetCompanyDisplayName_Scope__484387391 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [NavName("Name")]
            public NavText name;
            protected override uint RawScopeId { get => SetCompanyDisplayName_Scope__484387391.\u03b1scopeId; set => SetCompanyDisplayName_Scope__484387391.\u03b1scopeId = value; }

            internal SetCompanyDisplayName_Scope__484387391(Codeunit131100 \u03b2parent, NavText name) : base(\u03b2parent)
            {
                this.name = name.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 3908602704")]
        public void SetCompanyId(System.Guid id)
        {
            using (SetCompanyId_Scope_586707953 \u03b2scope = new SetCompanyId_Scope_586707953(this, id))
                \u03b2scope.Run();
        }

        [NavName("SetCompanyId")]
        [SignatureSpan(14918233898680346L)]
        [SourceSpans(15481153787461640L)]
        private sealed class SetCompanyId_Scope_586707953 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [NavName("Id")]
            public System.Guid id;
            protected override uint RawScopeId { get => SetCompanyId_Scope_586707953.\u03b1scopeId; set => SetCompanyId_Scope_586707953.\u03b1scopeId = value; }

            internal SetCompanyId_Scope_586707953(Codeunit131100 \u03b2parent, System.Guid id) : base(\u03b2parent)
            {
                this.id = id;
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 3561660433")]
        public void SetCompanyName(NavText name)
        {
            using (SetCompanyName_Scope_602738078 \u03b2scope = new SetCompanyName_Scope_602738078(this, name))
                \u03b2scope.Run();
        }

        [NavName("SetCompanyName")]
        [SignatureSpan(2251859943751708L)]
        [SourceSpans(2814779832533000L)]
        private sealed class SetCompanyName_Scope_602738078 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [NavName("Name")]
            public NavText name;
            protected override uint RawScopeId { get => SetCompanyName_Scope_602738078.\u03b1scopeId; set => SetCompanyName_Scope_602738078.\u03b1scopeId = value; }

            internal SetCompanyName_Scope_602738078(Codeunit131100 \u03b2parent, NavText name) : base(\u03b2parent)
            {
                this.name = name.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2868525657 - Method 2153777679")]
        public void SetCompanyUrlName(NavText name)
        {
            using (SetCompanyUrlName_Scope_1650406863 \u03b2scope = new SetCompanyUrlName_Scope_1650406863(this, name))
                \u03b2scope.Run();
        }

        [NavName("SetCompanyUrlName")]
        [SignatureSpan(10696109247037471L)]
        [SourceSpans(11259029135818760L)]
        private sealed class SetCompanyUrlName_Scope_1650406863 : NavMethodScope<Codeunit131100>
        {
            public static uint \u03b1scopeId;
            [NavName("Name")]
            public NavText name;
            protected override uint RawScopeId { get => SetCompanyUrlName_Scope_1650406863.\u03b1scopeId; set => SetCompanyUrlName_Scope_1650406863.\u03b1scopeId = value; }

            internal SetCompanyUrlName_Scope_1650406863(Codeunit131100 \u03b2parent, NavText name) : base(\u03b2parent)
            {
                this.name = name.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }
    }
}
