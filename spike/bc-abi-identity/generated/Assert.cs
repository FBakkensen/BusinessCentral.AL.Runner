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
    public sealed class Codeunit130 : NavCodeunit
    {
        public Codeunit130(ITreeObject parent) : base(parent, 130)
        {
        }

        public override string ObjectName => "Assert";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case -1626256597:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "AreEqual");
                    AreEqual(ALCompiler.ObjectToExactNavValue<NavVariant>(args[0]), ALCompiler.ObjectToExactNavValue<NavVariant>(args[1]), ALCompiler.ObjectToExactNavValue<NavText>(args[2]));
                    break;
                case 1077536212:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "AreNotEqual");
                    AreNotEqual(ALCompiler.ObjectToExactNavValue<NavVariant>(args[0]), ALCompiler.ObjectToExactNavValue<NavVariant>(args[1]), ALCompiler.ObjectToExactNavValue<NavText>(args[2]));
                    break;
                case -886670126:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "IsTrue");
                    IsTrue((bool)ALCompiler.ObjectToBoolean(args[0]), ALCompiler.ObjectToExactNavValue<NavText>(args[1]));
                    break;
                case 1379904343:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "IsFalse");
                    IsFalse((bool)ALCompiler.ObjectToBoolean(args[0]), ALCompiler.ObjectToExactNavValue<NavText>(args[1]));
                    break;
                case -414868753:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "ExpectedError");
                    ExpectedError(ALCompiler.ObjectToExactNavValue<NavText>(args[0]));
                    break;
                case -282706523:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "ExpectedErrorCode");
                    ExpectedErrorCode(ALCompiler.ObjectToExactNavValue<NavText>(args[0]));
                    break;
                case -2016571826:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "ExpectedErrorCode_2016571826");
                    ExpectedErrorCode_2016571826(ALCompiler.ObjectToExactNavValue<NavText>(args[0]), ALCompiler.ObjectToExactNavValue<NavText>(args[1]));
                    break;
                case -1038665824:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "ExpectedMessage");
                    ExpectedMessage(ALCompiler.ObjectToExactNavValue<NavText>(args[0]), ALCompiler.ObjectToExactNavValue<NavText>(args[1]));
                    break;
                case -888914761:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "ExpectedTestFieldError");
                    ExpectedTestFieldError(ALCompiler.ObjectToExactNavValue<NavText>(args[0]), ALCompiler.ObjectToExactNavValue<NavText>(args[1]));
                    break;
                case -10753027:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "RecordIsEmpty");
                    RecordIsEmpty(ALCompiler.ObjectToExactNavValue<NavVariant>(args[0]));
                    break;
                case -937498451:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "RecordIsNotEmpty");
                    RecordIsNotEmpty(ALCompiler.ObjectToExactNavValue<NavVariant>(args[0]));
                    break;
                case 1234415231:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "RecordCount");
                    RecordCount(ALCompiler.ObjectToExactNavValue<NavVariant>(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 2061903807:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "TableIsEmpty");
                    TableIsEmpty((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -917769900:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "TableIsNotEmpty");
                    TableIsNotEmpty((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 2004090022:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(4, args, "AreNearlyEqual");
                    AreNearlyEqual((Decimal18)ALCompiler.ObjectToDecimal(args[0]), (Decimal18)ALCompiler.ObjectToDecimal(args[1]), (Decimal18)ALCompiler.ObjectToDecimal(args[2]), ALCompiler.ObjectToExactNavValue<NavText>(args[3]));
                    break;
                case 1867274427:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "Fail");
                    Fail(ALCompiler.ObjectToExactNavValue<NavText>(args[0]));
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 130);
                    break;
            }

            return default;
        }

        public static Codeunit130 __Construct(ITreeObject parent)
        {
            return new Codeunit130(parent);
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 460278664")]
        public void AreEqual(NavVariant expected, NavVariant actual, NavText msg)
        {
            using (AreEqual_Scope__1626256597 \u03b2scope = new AreEqual_Scope__1626256597(this, expected, actual, msg))
                \u03b2scope.Run();
        }

        [NavName("AreEqual")]
        [SignatureSpan(1407435013423126L)]
        [SourceSpans(1970354902204424L)]
        private sealed class AreEqual_Scope__1626256597 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("Expected")]
            public NavVariant expected;
            [NavName("Actual")]
            public NavVariant actual;
            [NavName("Msg")]
            public NavText msg;
            protected override uint RawScopeId { get => AreEqual_Scope__1626256597.\u03b1scopeId; set => AreEqual_Scope__1626256597.\u03b1scopeId = value; }

            internal AreEqual_Scope__1626256597(Codeunit130 \u03b2parent, NavVariant expected, NavVariant actual, NavText msg) : base(\u03b2parent)
            {
                this.expected = expected.ALByValue(this);
                this.actual = actual.ALByValue(this);
                this.msg = msg.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 3550715185")]
        public void AreNearlyEqual(Decimal18 expected, Decimal18 actual, Decimal18 delta, NavText msg)
        {
            using (AreNearlyEqual_Scope_2004090022 \u03b2scope = new AreNearlyEqual_Scope_2004090022(this, expected, actual, delta, msg))
                \u03b2scope.Run();
        }

        [NavName("AreNearlyEqual")]
        [SignatureSpan(17170033712889884L)]
        [SourceSpans(17732953601671176L)]
        private sealed class AreNearlyEqual_Scope_2004090022 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("Expected")]
            public Decimal18 expected;
            [NavName("Actual")]
            public Decimal18 actual;
            [NavName("Delta")]
            public Decimal18 delta;
            [NavName("Msg")]
            public NavText msg;
            protected override uint RawScopeId { get => AreNearlyEqual_Scope_2004090022.\u03b1scopeId; set => AreNearlyEqual_Scope_2004090022.\u03b1scopeId = value; }

            internal AreNearlyEqual_Scope_2004090022(Codeunit130 \u03b2parent, Decimal18 expected, Decimal18 actual, Decimal18 delta, NavText msg) : base(\u03b2parent)
            {
                this.expected = expected;
                this.actual = actual;
                this.delta = delta;
                this.msg = msg.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 4212755428")]
        public void AreNotEqual(NavVariant expected, NavVariant actual, NavText msg)
        {
            using (AreNotEqual_Scope_1077536212 \u03b2scope = new AreNotEqual_Scope_1077536212(this, expected, actual, msg))
                \u03b2scope.Run();
        }

        [NavName("AreNotEqual")]
        [SignatureSpan(2533334920527897L)]
        [SourceSpans(3096254809309192L)]
        private sealed class AreNotEqual_Scope_1077536212 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("Expected")]
            public NavVariant expected;
            [NavName("Actual")]
            public NavVariant actual;
            [NavName("Msg")]
            public NavText msg;
            protected override uint RawScopeId { get => AreNotEqual_Scope_1077536212.\u03b1scopeId; set => AreNotEqual_Scope_1077536212.\u03b1scopeId = value; }

            internal AreNotEqual_Scope_1077536212(Codeunit130 \u03b2parent, NavVariant expected, NavVariant actual, NavText msg) : base(\u03b2parent)
            {
                this.expected = expected.ALByValue(this);
                this.actual = actual.ALByValue(this);
                this.msg = msg.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 668702713")]
        public void ExpectedErrorCode_2016571826(NavText expectedErrorCode, NavText expectedErrorMessage)
        {
            using (ExpectedErrorCode_Scope__2016571826 \u03b2scope = new ExpectedErrorCode_Scope__2016571826(this, expectedErrorCode, expectedErrorMessage))
                \u03b2scope.Run();
        }

        [NavName("ExpectedErrorCode")]
        [SignatureSpan(8162834456051743L)]
        [SourceSpans(8725754344833032L)]
        private sealed class ExpectedErrorCode_Scope__2016571826 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("ExpectedErrorCode")]
            public NavText expectedErrorCode;
            [NavName("ExpectedErrorMessage")]
            public NavText expectedErrorMessage;
            protected override uint RawScopeId { get => ExpectedErrorCode_Scope__2016571826.\u03b1scopeId; set => ExpectedErrorCode_Scope__2016571826.\u03b1scopeId = value; }

            internal ExpectedErrorCode_Scope__2016571826(Codeunit130 \u03b2parent, NavText expectedErrorCode, NavText expectedErrorMessage) : base(\u03b2parent)
            {
                this.expectedErrorCode = expectedErrorCode.ModifyLength(0);
                this.expectedErrorMessage = expectedErrorMessage.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2533927620")]
        public void ExpectedErrorCode(NavText expectedErrorCode)
        {
            using (ExpectedErrorCode_Scope__282706523 \u03b2scope = new ExpectedErrorCode_Scope__282706523(this, expectedErrorCode))
                \u03b2scope.Run();
        }

        [NavName("ExpectedErrorCode")]
        [SignatureSpan(7036934548946975L)]
        [SourceSpans(7599854437728264L)]
        private sealed class ExpectedErrorCode_Scope__282706523 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("ExpectedErrorCode")]
            public NavText expectedErrorCode;
            protected override uint RawScopeId { get => ExpectedErrorCode_Scope__282706523.\u03b1scopeId; set => ExpectedErrorCode_Scope__282706523.\u03b1scopeId = value; }

            internal ExpectedErrorCode_Scope__282706523(Codeunit130 \u03b2parent, NavText expectedErrorCode) : base(\u03b2parent)
            {
                this.expectedErrorCode = expectedErrorCode.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 738789213")]
        public void ExpectedError(NavText expectedErrorMessage)
        {
            using (ExpectedError_Scope__414868753 \u03b2scope = new ExpectedError_Scope__414868753(this, expectedErrorMessage))
                \u03b2scope.Run();
        }

        [NavName("ExpectedError")]
        [SignatureSpan(5911034641842203L)]
        [SourceSpans(6473954530623496L)]
        private sealed class ExpectedError_Scope__414868753 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("ExpectedErrorMessage")]
            public NavText expectedErrorMessage;
            protected override uint RawScopeId { get => ExpectedError_Scope__414868753.\u03b1scopeId; set => ExpectedError_Scope__414868753.\u03b1scopeId = value; }

            internal ExpectedError_Scope__414868753(Codeunit130 \u03b2parent, NavText expectedErrorMessage) : base(\u03b2parent)
            {
                this.expectedErrorMessage = expectedErrorMessage.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2588342616")]
        public void ExpectedMessage(NavText expectedMessage, NavText actualMessage)
        {
            using (ExpectedMessage_Scope__1038665824 \u03b2scope = new ExpectedMessage_Scope__1038665824(this, expectedMessage, actualMessage))
                \u03b2scope.Run();
        }

        [NavName("ExpectedMessage")]
        [SignatureSpan(9288734363156509L)]
        [SourceSpans(9851654251937800L)]
        private sealed class ExpectedMessage_Scope__1038665824 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("ExpectedMessage")]
            public NavText expectedMessage;
            [NavName("ActualMessage")]
            public NavText actualMessage;
            protected override uint RawScopeId { get => ExpectedMessage_Scope__1038665824.\u03b1scopeId; set => ExpectedMessage_Scope__1038665824.\u03b1scopeId = value; }

            internal ExpectedMessage_Scope__1038665824(Codeunit130 \u03b2parent, NavText expectedMessage, NavText actualMessage) : base(\u03b2parent)
            {
                this.expectedMessage = expectedMessage.ModifyLength(0);
                this.actualMessage = actualMessage.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 863390723")]
        public void ExpectedTestFieldError(NavText fieldCaption, NavText fieldValue)
        {
            using (ExpectedTestFieldError_Scope__888914761 \u03b2scope = new ExpectedTestFieldError_Scope__888914761(this, fieldCaption, fieldValue))
                \u03b2scope.Run();
        }

        [NavName("ExpectedTestFieldError")]
        [SignatureSpan(10414634270261284L)]
        [SourceSpans(10977554159042568L)]
        private sealed class ExpectedTestFieldError_Scope__888914761 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("FieldCaption")]
            public NavText fieldCaption;
            [NavName("FieldValue")]
            public NavText fieldValue;
            protected override uint RawScopeId { get => ExpectedTestFieldError_Scope__888914761.\u03b1scopeId; set => ExpectedTestFieldError_Scope__888914761.\u03b1scopeId = value; }

            internal ExpectedTestFieldError_Scope__888914761(Codeunit130 \u03b2parent, NavText fieldCaption, NavText fieldValue) : base(\u03b2parent)
            {
                this.fieldCaption = fieldCaption.ModifyLength(0);
                this.fieldValue = fieldValue.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 819176404")]
        public void Fail(NavText msg)
        {
            using (Fail_Scope_1867274427 \u03b2scope = new Fail_Scope_1867274427(this, msg))
                \u03b2scope.Run();
        }

        [NavName("Fail")]
        [SignatureSpan(18295933619994642L)]
        [SourceSpans(18858853508775944L)]
        private sealed class Fail_Scope_1867274427 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("Msg")]
            public NavText msg;
            protected override uint RawScopeId { get => Fail_Scope_1867274427.\u03b1scopeId; set => Fail_Scope_1867274427.\u03b1scopeId = value; }

            internal Fail_Scope_1867274427(Codeunit130 \u03b2parent, NavText msg) : base(\u03b2parent)
            {
                this.msg = msg.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2842911935")]
        public void IsFalse(bool condition, NavText msg)
        {
            using (IsFalse_Scope_1379904343 \u03b2scope = new IsFalse_Scope_1379904343(this, condition, msg))
                \u03b2scope.Run();
        }

        [NavName("IsFalse")]
        [SignatureSpan(4785134734737429L)]
        [SourceSpans(5348054623518728L)]
        private sealed class IsFalse_Scope_1379904343 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("Condition")]
            public bool condition;
            [NavName("Msg")]
            public NavText msg;
            protected override uint RawScopeId { get => IsFalse_Scope_1379904343.\u03b1scopeId; set => IsFalse_Scope_1379904343.\u03b1scopeId = value; }

            internal IsFalse_Scope_1379904343(Codeunit130 \u03b2parent, bool condition, NavText msg) : base(\u03b2parent)
            {
                this.condition = condition;
                this.msg = msg.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2005199097")]
        public void IsTrue(bool condition, NavText msg)
        {
            using (IsTrue_Scope__886670126 \u03b2scope = new IsTrue_Scope__886670126(this, condition, msg))
                \u03b2scope.Run();
        }

        [NavName("IsTrue")]
        [SignatureSpan(3659234827632660L)]
        [SourceSpans(4222154716413960L)]
        private sealed class IsTrue_Scope__886670126 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("Condition")]
            public bool condition;
            [NavName("Msg")]
            public NavText msg;
            protected override uint RawScopeId { get => IsTrue_Scope__886670126.\u03b1scopeId; set => IsTrue_Scope__886670126.\u03b1scopeId = value; }

            internal IsTrue_Scope__886670126(Codeunit130 \u03b2parent, bool condition, NavText msg) : base(\u03b2parent)
            {
                this.condition = condition;
                this.msg = msg.ModifyLength(0);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2494030224")]
        public void RecordCount(NavVariant recordVariant, int expectedCount)
        {
            using (RecordCount_Scope_1234415231 \u03b2scope = new RecordCount_Scope_1234415231(this, recordVariant, expectedCount))
                \u03b2scope.Run();
        }

        [NavName("RecordCount")]
        [SignatureSpan(13792333991575577L)]
        [SourceSpans(14355253880356872L)]
        private sealed class RecordCount_Scope_1234415231 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("RecordVariant")]
            public NavVariant recordVariant;
            [NavName("ExpectedCount")]
            public int expectedCount;
            protected override uint RawScopeId { get => RecordCount_Scope_1234415231.\u03b1scopeId; set => RecordCount_Scope_1234415231.\u03b1scopeId = value; }

            internal RecordCount_Scope_1234415231(Codeunit130 \u03b2parent, NavVariant recordVariant, int expectedCount) : base(\u03b2parent)
            {
                this.recordVariant = recordVariant.ALByValue(this);
                this.expectedCount = expectedCount;
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 3932116956")]
        public void RecordIsEmpty(NavVariant recordVariant)
        {
            using (RecordIsEmpty_Scope__10753027 \u03b2scope = new RecordIsEmpty_Scope__10753027(this, recordVariant))
                \u03b2scope.Run();
        }

        [NavName("RecordIsEmpty")]
        [SignatureSpan(11540534177366043L)]
        [SourceSpans(12103454066147336L)]
        private sealed class RecordIsEmpty_Scope__10753027 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("RecordVariant")]
            public NavVariant recordVariant;
            protected override uint RawScopeId { get => RecordIsEmpty_Scope__10753027.\u03b1scopeId; set => RecordIsEmpty_Scope__10753027.\u03b1scopeId = value; }

            internal RecordIsEmpty_Scope__10753027(Codeunit130 \u03b2parent, NavVariant recordVariant) : base(\u03b2parent)
            {
                this.recordVariant = recordVariant.ALByValue(this);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 3686940351")]
        public void RecordIsNotEmpty(NavVariant recordVariant)
        {
            using (RecordIsNotEmpty_Scope__937498451 \u03b2scope = new RecordIsNotEmpty_Scope__937498451(this, recordVariant))
                \u03b2scope.Run();
        }

        [NavName("RecordIsNotEmpty")]
        [SignatureSpan(12666434084470814L)]
        [SourceSpans(13229353973252104L)]
        private sealed class RecordIsNotEmpty_Scope__937498451 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("RecordVariant")]
            public NavVariant recordVariant;
            protected override uint RawScopeId { get => RecordIsNotEmpty_Scope__937498451.\u03b1scopeId; set => RecordIsNotEmpty_Scope__937498451.\u03b1scopeId = value; }

            internal RecordIsNotEmpty_Scope__937498451(Codeunit130 \u03b2parent, NavVariant recordVariant) : base(\u03b2parent)
            {
                this.recordVariant = recordVariant.ALByValue(this);
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2365682116")]
        public void TableIsEmpty(int tableId)
        {
            using (TableIsEmpty_Scope_2061903807 \u03b2scope = new TableIsEmpty_Scope_2061903807(this, tableId))
                \u03b2scope.Run();
        }

        [NavName("TableIsEmpty")]
        [SignatureSpan(14918233898680346L)]
        [SourceSpans(15481153787461640L)]
        private sealed class TableIsEmpty_Scope_2061903807 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("TableId")]
            public int tableId;
            protected override uint RawScopeId { get => TableIsEmpty_Scope_2061903807.\u03b1scopeId; set => TableIsEmpty_Scope_2061903807.\u03b1scopeId = value; }

            internal TableIsEmpty_Scope_2061903807(Codeunit130 \u03b2parent, int tableId) : base(\u03b2parent)
            {
                this.tableId = tableId;
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3132006006 - Method 2805144541")]
        public void TableIsNotEmpty(int tableId)
        {
            using (TableIsNotEmpty_Scope__917769900 \u03b2scope = new TableIsNotEmpty_Scope__917769900(this, tableId))
                \u03b2scope.Run();
        }

        [NavName("TableIsNotEmpty")]
        [SignatureSpan(16044133805785117L)]
        [SourceSpans(16607053694566408L)]
        private sealed class TableIsNotEmpty_Scope__917769900 : NavMethodScope<Codeunit130>
        {
            public static uint \u03b1scopeId;
            [NavName("TableId")]
            public int tableId;
            protected override uint RawScopeId { get => TableIsNotEmpty_Scope__917769900.\u03b1scopeId; set => TableIsNotEmpty_Scope__917769900.\u03b1scopeId = value; }

            internal TableIsNotEmpty_Scope__917769900(Codeunit130 \u03b2parent, int tableId) : base(\u03b2parent)
            {
                this.tableId = tableId;
            }

            protected override void OnRun()
            {
            }
        }
    }
}
