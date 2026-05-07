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
    public sealed class Codeunit131003 : NavCodeunit
    {
        public Codeunit131003(ITreeObject parent) : base(parent, 131003)
        {
        }

        public override string ObjectName => "Library - Utility";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 1004149295:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GenerateGUID");
                    return GenerateGUID();
                    break;
                case -1303748072:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "GenerateRandomCode");
                    return GenerateRandomCode((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 766157091:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "GenerateRandomCode20");
                    return GenerateRandomCode20((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 2049874294:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "GenerateRandomText");
                    return GenerateRandomText((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 1114548148:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "GenerateRandomXMLText");
                    return GenerateRandomXMLText((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 1314780206:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "GenerateRandomAlphabeticText");
                    return GenerateRandomAlphabeticText((int)ALCompiler.ObjectToInt32(args[0]), ALCompiler.ObjectToInt32(args[1]));
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 131003);
                    break;
            }

            return default;
        }

        public static Codeunit131003 __Construct(ITreeObject parent)
        {
            return new Codeunit131003(parent);
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3079332134 - Method 2893312297")]
        public NavText GenerateGUID()
        {
            using (GenerateGUID_Scope_1004149295 \u03b2scope = new GenerateGUID_Scope_1004149295(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GenerateGUID")]
        [SignatureSpan(2533334920527898L)]
        [SourceSpans(3096259104276534L, 3377729786085384L)]
        private sealed class GenerateGUID_Scope_1004149295 : NavMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(50);
            protected override uint RawScopeId { get => GenerateGUID_Scope_1004149295.\u03b1scopeId; set => GenerateGUID_Scope_1004149295.\u03b1scopeId = value; }

            internal GenerateGUID_Scope_1004149295(Codeunit131003 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = new NavText(50, ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(System.Guid.NewGuid())), "=", "{}"));
                return;
            }
        }

        private static NCLOptionMetadata GenerateRandomAlphabeticText_Scope_1314780206_textCase_metadata = NCLOptionMetadata.Create("");
        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3079332134 - Method 2531374800")]
        public NavText GenerateRandomAlphabeticText(int maxLength, int textCase)
        {
            using (GenerateRandomAlphabeticText_Scope_1314780206 \u03b2scope = new GenerateRandomAlphabeticText_Scope_1314780206(this, maxLength, textCase))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GenerateRandomAlphabeticText")]
        [SignatureSpan(13510859014799402L)]
        [SourceSpans(15481158082428952L, 15762633059270704L, 16044125215850544L, 16325595897659415L, 16607075169402916L, 16888532966309909L, 17170003648118792L)]
        private sealed class GenerateRandomAlphabeticText_Scope_1314780206 : NavMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxLength")]
            public int maxLength;
            [NavName("TextCase")]
            public NavOption textCase;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            [NavName("i")]
            public int i = default(int);
            [NavName("ASCIIFrom")]
            public int aSCIIFrom = default(int);
            [NavName("Result")]
            public NavText result = NavText.Default(0);
            protected override uint RawScopeId { get => GenerateRandomAlphabeticText_Scope_1314780206.\u03b1scopeId; set => GenerateRandomAlphabeticText_Scope_1314780206.\u03b1scopeId = value; }

            internal GenerateRandomAlphabeticText_Scope_1314780206(Codeunit131003 \u03b2parent, int maxLength, int textCase) : base(\u03b2parent)
            {
                this.maxLength = maxLength;
                this.textCase = NavOption.Create(GenerateRandomAlphabeticText_Scope_1314780206_textCase_metadata, textCase);
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.aSCIIFrom = 97;
                this.i = 1;
                StmtHit(1);
                int @tmp0 = this.maxLength;
                for (; this.i <= @tmp0;)
                {
                    {
                        CStmtHit(2);
                        this.result = new NavText(ALSystemString.SetChar(this.result, this.i - 1, ALCompiler.ToChar(this.aSCIIFrom + ALSystemNumeric.ALRandom(25))));
                    }

                    l_56_8:
                        if (this.i >= @tmp0)
                            break;
                    this.i = this.i + 1;
                }

                if (CStmtHit(3) & (this.textCase == 1))
                {
                    StmtHit(4);
                    this.\u03b3retVal = new NavText(ALSystemString.ALUppercase(this.result));
                    return;
                }

                StmtHit(5);
                this.\u03b3retVal = this.result;
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3079332134 - Method 1029109640")]
        public NavCode GenerateRandomCode20(int fieldNo, int tableNo)
        {
            using (GenerateRandomCode20_Scope_766157091 \u03b2scope = new GenerateRandomCode20_Scope_766157091(this, fieldNo, tableNo))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GenerateRandomCode20")]
        [SignatureSpan(6192509618618402L)]
        [SourceSpans(7318383755919420L, 7599858732695601L, 7881329414504456L)]
        private sealed class GenerateRandomCode20_Scope_766157091 : NavMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            [NavName("FieldNo")]
            public int fieldNo;
            [NavName("TableNo")]
            public int tableNo;
            [ReturnValue]
            public NavCode \u03b3retVal = NavCode.Default(20);
            [NavName("GuidTxt")]
            public NavText guidTxt = NavText.Default(0);
            protected override uint RawScopeId { get => GenerateRandomCode20_Scope_766157091.\u03b1scopeId; set => GenerateRandomCode20_Scope_766157091.\u03b1scopeId = value; }

            internal GenerateRandomCode20_Scope_766157091(Codeunit131003 \u03b2parent, int fieldNo, int tableNo) : base(\u03b2parent)
            {
                this.fieldNo = fieldNo;
                this.tableNo = tableNo;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.guidTxt = new NavText(ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(System.Guid.NewGuid())), "=", "{}-"));
                StmtHit(1);
                this.\u03b3retVal = new NavCode(20, ALSystemString.ALUppercase(ALSystemString.ALCopyStr(this.guidTxt, 1, 20)));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3079332134 - Method 2052175702")]
        public NavCode GenerateRandomCode(int fieldNo, int tableNo)
        {
            using (GenerateRandomCode_Scope__1303748072 \u03b2scope = new GenerateRandomCode_Scope__1303748072(this, fieldNo, tableNo))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GenerateRandomCode")]
        [SignatureSpan(3940709804408864L)]
        [SourceSpans(5066583941709884L, 5348058918486065L, 5629529600294920L)]
        private sealed class GenerateRandomCode_Scope__1303748072 : NavMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            [NavName("FieldNo")]
            public int fieldNo;
            [NavName("TableNo")]
            public int tableNo;
            [ReturnValue]
            public NavCode \u03b3retVal = NavCode.Default(10);
            [NavName("GuidTxt")]
            public NavText guidTxt = NavText.Default(0);
            protected override uint RawScopeId { get => GenerateRandomCode_Scope__1303748072.\u03b1scopeId; set => GenerateRandomCode_Scope__1303748072.\u03b1scopeId = value; }

            internal GenerateRandomCode_Scope__1303748072(Codeunit131003 \u03b2parent, int fieldNo, int tableNo) : base(\u03b2parent)
            {
                this.fieldNo = fieldNo;
                this.tableNo = tableNo;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.guidTxt = new NavText(ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(System.Guid.NewGuid())), "=", "{}-"));
                StmtHit(1);
                this.\u03b3retVal = new NavCode(10, ALSystemString.ALUppercase(ALSystemString.ALCopyStr(this.guidTxt, 1, 10)));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3079332134 - Method 3713103421")]
        public NavText GenerateRandomText(int maxLength)
        {
            using (GenerateRandomText_Scope_2049874294 \u03b2scope = new GenerateRandomText_Scope_2049874294(this, maxLength))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GenerateRandomText")]
        [SignatureSpan(8444309432827936L)]
        [SourceSpans(9570209339932713L, 9851675726774347L, 10133133523681325L, 10414604205490184L)]
        private sealed class GenerateRandomText_Scope_2049874294 : NavMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxLength")]
            public int maxLength;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            [NavName("GuidTxt")]
            public NavText guidTxt = NavText.Default(0);
            protected override uint RawScopeId { get => GenerateRandomText_Scope_2049874294.\u03b1scopeId; set => GenerateRandomText_Scope_2049874294.\u03b1scopeId = value; }

            internal GenerateRandomText_Scope_2049874294(Codeunit131003 \u03b2parent, int maxLength) : base(\u03b2parent)
            {
                this.maxLength = maxLength;
            }

            protected override void OnRun()
            {
                while (CStmtHit(0) & (ALSystemString.ALStrLen(this.guidTxt) < this.maxLength))
                {
                    StmtHit(1);
                    this.guidTxt = new NavText(this.guidTxt + (ALSystemString.ALLowercase(ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(System.Guid.NewGuid())), "=", "{}-"))));
                }

                StmtHit(2);
                this.\u03b3retVal = new NavText(ALSystemString.ALCopyStr(this.guidTxt, 1, this.maxLength));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 3079332134 - Method 823555251")]
        public NavText GenerateRandomXMLText(int maxLength)
        {
            using (GenerateRandomXMLText_Scope_1114548148 \u03b2scope = new GenerateRandomXMLText_Scope_1114548148(this, maxLength))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GenerateRandomXMLText")]
        [SignatureSpan(10977584223813667L)]
        [SourceSpans(12103484130918441L, 12384950517760075L, 12666408314667053L, 12947878996475912L)]
        private sealed class GenerateRandomXMLText_Scope_1114548148 : NavMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxLength")]
            public int maxLength;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            [NavName("GuidTxt")]
            public NavText guidTxt = NavText.Default(0);
            protected override uint RawScopeId { get => GenerateRandomXMLText_Scope_1114548148.\u03b1scopeId; set => GenerateRandomXMLText_Scope_1114548148.\u03b1scopeId = value; }

            internal GenerateRandomXMLText_Scope_1114548148(Codeunit131003 \u03b2parent, int maxLength) : base(\u03b2parent)
            {
                this.maxLength = maxLength;
            }

            protected override void OnRun()
            {
                while (CStmtHit(0) & (ALSystemString.ALStrLen(this.guidTxt) < this.maxLength))
                {
                    StmtHit(1);
                    this.guidTxt = new NavText(this.guidTxt + (ALSystemString.ALLowercase(ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(System.Guid.NewGuid())), "=", "{}-"))));
                }

                StmtHit(2);
                this.\u03b3retVal = new NavText(ALSystemString.ALCopyStr(this.guidTxt, 1, this.maxLength));
                return;
            }
        }

        protected override void OnRun([NavByReferenceAttribute][NavObjectId(ObjectId = 0)] INavRecordHandle \u03b5rec)
        {
            using (OnRun_Scope \u03b2scope = new OnRun_Scope(this, \u03b5rec))
                \u03b2scope.Run();
        }

        [NavName("OnRun")]
        [SignatureSpan(1407426423488529L)]
        [SourceSpans(1970354902204424L)]
        private sealed class OnRun_Scope : NavTriggerMethodScope<Codeunit131003>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => OnRun_Scope.\u03b1scopeId; set => OnRun_Scope.\u03b1scopeId = value; }

            internal OnRun_Scope(Codeunit131003 \u03b2parent, [NavByReferenceAttribute][NavObjectId(ObjectId = 0)] INavRecordHandle \u03b5rec) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }
    }
}
