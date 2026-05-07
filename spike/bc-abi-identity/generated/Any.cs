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
    public sealed class Codeunit130500 : NavCodeunit
    {
        [NavName("Seed")]
        private int seed = default(int);
        [NavName("SeedSet")]
        private bool seedSet = default(bool);
        protected override void OnClear()
        {
            this.seed = default(int);
            this.seedSet = default(bool);
        }

        public Codeunit130500(ITreeObject parent) : base(parent, 130500)
        {
        }

        public override string ObjectName => "Any";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 103103916:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "Boolean");
                    return Boolean();
                    break;
                case 852555984:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "IntegerInRange");
                    return IntegerInRange((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -1979833688:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "IntegerInRange_1979833688");
                    return IntegerInRange_1979833688((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case -1637945577:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "DecimalInRange");
                    return DecimalInRange((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case -335090290:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "DecimalInRange_335090290");
                    return DecimalInRange_335090290((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]), (int)ALCompiler.ObjectToInt32(args[2]));
                    break;
                case 1436032192:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "DecimalInRange_1436032192");
                    return DecimalInRange_1436032192((Decimal18)ALCompiler.ObjectToDecimal(args[0]), (Decimal18)ALCompiler.ObjectToDecimal(args[1]), (int)ALCompiler.ObjectToInt32(args[2]));
                    break;
                case -380215559:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "DateInRange");
                    return DateInRange((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -1525099069:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "DateInRange_1525099069");
                    return DateInRange_1525099069(ALCompiler.ObjectToExactNavValue<NavDate>(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 603023490:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "DateInRange_603023490");
                    return DateInRange_603023490(ALCompiler.ObjectToExactNavValue<NavDate>(args[0]), (int)ALCompiler.ObjectToInt32(args[1]), (int)ALCompiler.ObjectToInt32(args[2]));
                    break;
                case 366575985:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "AlphabeticText");
                    return AlphabeticText((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -857636156:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "AlphanumericText");
                    return AlphanumericText((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 1366092468:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "UnicodeText");
                    return UnicodeText((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -1124834794:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "Email");
                    return Email();
                    break;
                case 1539154589:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "Email_1539154589");
                    return Email_1539154589((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case -885965549:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GuidValue");
                    return GuidValue();
                    break;
                case -1071630414:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "SetSeed");
                    SetSeed((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -1885673659:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "GetSeed");
                    return GetSeed();
                    break;
                case 1911422895:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "SetDefaultSeed");
                    SetDefaultSeed();
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 130500);
                    break;
            }

            return default;
        }

        public static Codeunit130500 __Construct(ITreeObject parent)
        {
            return new Codeunit130500(parent);
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 2873311388")]
        public NavText AlphabeticText(int length)
        {
            using (AlphabeticText_Scope_366575985 \u03b2scope = new AlphabeticText_Scope_366575985(this, length))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("AlphabeticText")]
        [SignatureSpan(21110683387756572L)]
        [SourceSpans(23362457432162332L, 23643932408938523L, 23925407385911308L, 24206899542360129L, 24488374519136291L, 24769832316043275L, 25051307292819480L, 25332777974628360L)]
        private sealed class AlphabeticText_Scope_366575985 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("Length")]
            public int length;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            [NavName("ASCIICodeFrom")]
            public int aSCIICodeFrom = default(int);
            [NavName("ASCIICodeTo")]
            public int aSCIICodeTo = default(int);
            [NavName("Number")]
            public int number = default(int);
            [NavName("i")]
            public int i = default(int);
            [NavName("TextValue")]
            public NavText textValue = NavText.Default(0);
            protected override uint RawScopeId { get => AlphabeticText_Scope_366575985.\u03b1scopeId; set => AlphabeticText_Scope_366575985.\u03b1scopeId = value; }

            internal AlphabeticText_Scope_366575985(Codeunit130500 \u03b2parent, int length) : base(\u03b2parent)
            {
                this.length = length;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.aSCIICodeFrom = 97;
                StmtHit(1);
                this.aSCIICodeTo = 122;
                this.i = 1;
                StmtHit(2);
                int @tmp0 = this.length;
                for (; this.i <= @tmp0;)
                {
                    {
                        CStmtHit(3);
                        this.number = base.Parent.IntegerInRange_1979833688(this.aSCIICodeFrom, this.aSCIICodeTo);
                        StmtHit(4);
                        this.textValue = new NavText(ALSystemString.SetChar(this.textValue, this.i - 1, ALCompiler.ToChar(this.number)));
                    }

                    l_85_8:
                        if (this.i >= @tmp0)
                            break;
                    this.i = this.i + 1;
                }

                StmtHit(5);
                StmtHit(6);
                this.\u03b3retVal = this.textValue;
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 3423230762")]
        public NavText AlphanumericText(int length)
        {
            using (AlphanumericText_Scope__857636156 \u03b2scope = new AlphanumericText_Scope__857636156(this, length))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("AlphanumericText")]
        [SignatureSpan(25895757992951838L)]
        [SourceSpans(27021657900056614L, 27303124286898250L, 27584582083805226L, 27866052765614088L)]
        private sealed class AlphanumericText_Scope__857636156 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("Length")]
            public int length;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            [NavName("GuidTxt")]
            public NavText guidTxt = NavText.Default(0);
            protected override uint RawScopeId { get => AlphanumericText_Scope__857636156.\u03b1scopeId; set => AlphanumericText_Scope__857636156.\u03b1scopeId = value; }

            internal AlphanumericText_Scope__857636156(Codeunit130500 \u03b2parent, int length) : base(\u03b2parent)
            {
                this.length = length;
            }

            protected override void OnRun()
            {
                while (CStmtHit(0) & (ALSystemString.ALStrLen(this.guidTxt) < this.length))
                {
                    StmtHit(1);
                    this.guidTxt = new NavText(this.guidTxt + (ALSystemString.ALLowercase(ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(base.Parent.GuidValue())), "=", "{}-"))));
                }

                StmtHit(2);
                this.\u03b3retVal = new NavText(ALSystemString.ALCopyStr(this.guidTxt, 1, this.length));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 4232386234")]
        public bool Boolean()
        {
            using (Boolean_Scope_103103916 \u03b2scope = new Boolean_Scope_103103916(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("Boolean")]
        [SignatureSpan(2533334920527893L)]
        [SourceSpans(3096259104276514L, 3377729786085384L)]
        private sealed class Boolean_Scope_103103916 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public bool \u03b3retVal = default(bool);
            protected override uint RawScopeId { get => Boolean_Scope_103103916.\u03b1scopeId; set => Boolean_Scope_103103916.\u03b1scopeId = value; }

            internal Boolean_Scope_103103916(Codeunit130500 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.GetNextValue(2) == 2;
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 1519525099")]
        public NavDate DateInRange_603023490(NavDate startingDate, int minNumberOfDays, int maxNumberOfDays)
        {
            using (DateInRange_Scope_603023490 \u03b2scope = new DateInRange_Scope_603023490(this, startingDate, minNumberOfDays, maxNumberOfDays))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DateInRange")]
        [SignatureSpan(19140358550323225L)]
        [SourceSpans(19703295618973741L, 19984774890717215L, 20266232687624301L, 20547703369433096L)]
        private sealed class DateInRange_Scope_603023490 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("StartingDate")]
            public NavDate startingDate;
            [NavName("MinNumberOfDays")]
            public int minNumberOfDays;
            [NavName("MaxNumberOfDays")]
            public int maxNumberOfDays;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => DateInRange_Scope_603023490.\u03b1scopeId; set => DateInRange_Scope_603023490.\u03b1scopeId = value; }

            internal DateInRange_Scope_603023490(Codeunit130500 \u03b2parent, NavDate startingDate, int minNumberOfDays, int maxNumberOfDays) : base(\u03b2parent)
            {
                this.startingDate = startingDate;
                this.minNumberOfDays = minNumberOfDays;
                this.maxNumberOfDays = maxNumberOfDays;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.minNumberOfDays >= this.maxNumberOfDays))
                {
                    StmtHit(1);
                    this.\u03b3retVal = this.startingDate;
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = ALSystemDate.ALCalcDate(this.Session, ALSystemString.ALStrSubstNo("<+%1D>", ALCompiler.ToNavValue(base.Parent.IntegerInRange_1979833688(this.minNumberOfDays, this.maxNumberOfDays))), this.startingDate);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 1436856403")]
        public NavDate DateInRange_1525099069(NavDate startingDate, int maxNumberOfDays)
        {
            using (DateInRange_Scope__1525099069 \u03b2scope = new DateInRange_Scope__1525099069(this, startingDate, maxNumberOfDays))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DateInRange")]
        [SignatureSpan(17732983666442265L)]
        [SourceSpans(18295907850190908L, 18577378531999752L)]
        private sealed class DateInRange_Scope__1525099069 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("StartingDate")]
            public NavDate startingDate;
            [NavName("MaxNumberOfDays")]
            public int maxNumberOfDays;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => DateInRange_Scope__1525099069.\u03b1scopeId; set => DateInRange_Scope__1525099069.\u03b1scopeId = value; }

            internal DateInRange_Scope__1525099069(Codeunit130500 \u03b2parent, NavDate startingDate, int maxNumberOfDays) : base(\u03b2parent)
            {
                this.startingDate = startingDate;
                this.maxNumberOfDays = maxNumberOfDays;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.DateInRange_603023490(this.startingDate, 0, this.maxNumberOfDays);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 3487573483")]
        public NavDate DateInRange(int maxNumberOfDays)
        {
            using (DateInRange_Scope__380215559 \u03b2scope = new DateInRange_Scope__380215559(this, maxNumberOfDays))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DateInRange")]
        [SignatureSpan(16325608782561305L)]
        [SourceSpans(16888532966309946L, 17170003648118792L)]
        private sealed class DateInRange_Scope__380215559 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxNumberOfDays")]
            public int maxNumberOfDays;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => DateInRange_Scope__380215559.\u03b1scopeId; set => DateInRange_Scope__380215559.\u03b1scopeId = value; }

            internal DateInRange_Scope__380215559(Codeunit130500 \u03b2parent, int maxNumberOfDays) : base(\u03b2parent)
            {
                this.maxNumberOfDays = maxNumberOfDays;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.DateInRange_603023490(ALSystemDate.ALWorkDate(this.Session), 0, this.maxNumberOfDays);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 1390316861")]
        public Decimal18 DecimalInRange_1436032192(Decimal18 minValue, Decimal18 maxValue, int decimalPlaces)
        {
            using (DecimalInRange_Scope_1436032192 \u03b2scope = new DecimalInRange_Scope_1436032192(this, minValue, maxValue, decimalPlaces))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DecimalInRange")]
        [SignatureSpan(12947909061247004L)]
        [SourceSpans(14636733152100392L, 14918208128876589L, 15199683105652781L, 15481158082428973L, 15762628764237832L)]
        private sealed class DecimalInRange_Scope_1436032192 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MinValue")]
            public Decimal18 minValue;
            [NavName("MaxValue")]
            public Decimal18 maxValue;
            [NavName("DecimalPlaces")]
            public int decimalPlaces;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            [NavName("Min")]
            public int min = default(int);
            [NavName("Max")]
            public int max = default(int);
            [NavName("Pow")]
            public int pow = default(int);
            protected override uint RawScopeId { get => DecimalInRange_Scope_1436032192.\u03b1scopeId; set => DecimalInRange_Scope_1436032192.\u03b1scopeId = value; }

            internal DecimalInRange_Scope_1436032192(Codeunit130500 \u03b2parent, Decimal18 minValue, Decimal18 maxValue, int decimalPlaces) : base(\u03b2parent)
            {
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.decimalPlaces = decimalPlaces;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.pow = ALCompiler.ToInt32(ALSystemNumeric.ALPower(10, this.decimalPlaces));
                StmtHit(1);
                this.min = ALCompiler.ToInt32(ALSystemNumeric.ALRound(this.minValue * this.pow, 1, ">"));
                StmtHit(2);
                this.max = ALCompiler.ToInt32(ALSystemNumeric.ALRound(this.maxValue * this.pow, 1, "<"));
                StmtHit(3);
                this.\u03b3retVal = base.Parent.IntegerInRange_1979833688(this.min, this.max) / ((Decimal18)this.pow);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 2670508621")]
        public Decimal18 DecimalInRange(int maxValue, int decimalPlaces)
        {
            using (DecimalInRange_Scope__1637945577 \u03b2scope = new DecimalInRange_Scope__1637945577(this, maxValue, decimalPlaces))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DecimalInRange")]
        [SignatureSpan(7318409525723164L)]
        [SourceSpans(8725758639800360L, 9007233616576574L, 9288721478254626L, 9570200749998116L, 10133163588452397L, 10414642860195900L, 10696083477233704L, 10977554159042568L)]
        private sealed class DecimalInRange_Scope__1637945577 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxValue")]
            public int maxValue;
            [NavName("DecimalPlaces")]
            public int decimalPlaces;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            [NavName("PseudoRandomInteger")]
            public int pseudoRandomInteger = default(int);
            [NavName("Pow")]
            public int pow = default(int);
            protected override uint RawScopeId { get => DecimalInRange_Scope__1637945577.\u03b1scopeId; set => DecimalInRange_Scope__1637945577.\u03b1scopeId = value; }

            internal DecimalInRange_Scope__1637945577(Codeunit130500 \u03b2parent, int maxValue, int decimalPlaces) : base(\u03b2parent)
            {
                this.maxValue = maxValue;
                this.decimalPlaces = decimalPlaces;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.pow = ALCompiler.ToInt32(ALSystemNumeric.ALPower(10, this.decimalPlaces));
                StmtHit(1);
                this.pseudoRandomInteger = base.Parent.IntegerInRange(this.maxValue * this.pow);
                if (CStmtHit(2) & (this.pseudoRandomInteger == 0))
                {
                    StmtHit(3);
                    this.pseudoRandomInteger = 1;
                }
                else if (CStmtHit(4) & (this.pseudoRandomInteger % 10 == 0))
                {
                    StmtHit(5);
                    this.pseudoRandomInteger = this.pseudoRandomInteger - (base.Parent.IntegerInRange_1979833688(1, 9));
                }

                StmtHit(6);
                this.\u03b3retVal = this.pseudoRandomInteger / ((Decimal18)this.pow);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 955642438")]
        public Decimal18 DecimalInRange_335090290(int minValue, int maxValue, int decimalPlaces)
        {
            using (DecimalInRange_Scope__335090290 \u03b2scope = new DecimalInRange_Scope__335090290(this, minValue, maxValue, decimalPlaces))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("DecimalInRange")]
        [SignatureSpan(11540534177366044L)]
        [SourceSpans(12103458361114700L, 12384929042923528L)]
        private sealed class DecimalInRange_Scope__335090290 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MinValue")]
            public int minValue;
            [NavName("MaxValue")]
            public int maxValue;
            [NavName("DecimalPlaces")]
            public int decimalPlaces;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => DecimalInRange_Scope__335090290.\u03b1scopeId; set => DecimalInRange_Scope__335090290.\u03b1scopeId = value; }

            internal DecimalInRange_Scope__335090290(Codeunit130500 \u03b2parent, int minValue, int maxValue, int decimalPlaces) : base(\u03b2parent)
            {
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.decimalPlaces = decimalPlaces;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = this.minValue + base.Parent.DecimalInRange(this.maxValue - this.minValue, this.decimalPlaces);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 3869341414")]
        public NavText Email_1539154589(int localPartLength, int domainLength)
        {
            using (Email_Scope_1539154589 \u03b2scope = new Email_Scope_1539154589(this, localPartLength, domainLength))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("Email")]
        [SignatureSpan(32369682458804243L)]
        [SourceSpans(32932606642552943L, 33214077324361736L)]
        private sealed class Email_Scope_1539154589 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("LocalPartLength")]
            public int localPartLength;
            [NavName("DomainLength")]
            public int domainLength;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            protected override uint RawScopeId { get => Email_Scope_1539154589.\u03b1scopeId; set => Email_Scope_1539154589.\u03b1scopeId = value; }

            internal Email_Scope_1539154589(Codeunit130500 \u03b2parent, int localPartLength, int domainLength) : base(\u03b2parent)
            {
                this.localPartLength = localPartLength;
                this.domainLength = domainLength;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = new NavText(base.Parent.AlphanumericText(this.localPartLength) + "@" + base.Parent.AlphabeticText(this.domainLength) + "." + base.Parent.AlphabeticText(3));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 1406229554")]
        public NavText Email()
        {
            using (Email_Scope__1124834794 \u03b2scope = new Email_Scope__1124834794(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("Email")]
        [SignatureSpan(30962307574923283L)]
        [SourceSpans(31525231758671900L, 31806702440480776L)]
        private sealed class Email_Scope__1124834794 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            protected override uint RawScopeId { get => Email_Scope__1124834794.\u03b1scopeId; set => Email_Scope__1124834794.\u03b1scopeId = value; }

            internal Email_Scope__1124834794(Codeunit130500 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.Email_1539154589(20, 20);
                return;
            }
        }

        private int GetNextValue(int maxValue)
        {
            using (GetNextValue_Scope__1014773973 \u03b2scope = new GetNextValue_Scope__1014773973(this, maxValue))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetNextValue")]
        [SignatureSpan(40251007578341408L)]
        [SourceSpans(40813918877188118L, 41095398148931607L, 41376855945838623L, 41658326627647496L)]
        private sealed class GetNextValue_Scope__1014773973 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxValue")]
            public int maxValue;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => GetNextValue_Scope__1014773973.\u03b1scopeId; set => GetNextValue_Scope__1014773973.\u03b1scopeId = value; }

            internal GetNextValue_Scope__1014773973(Codeunit130500 \u03b2parent, int maxValue) : base(\u03b2parent)
            {
                this.maxValue = maxValue;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (!base.Parent.seedSet))
                {
                    StmtHit(1);
                    base.Parent.SetSeed(1);
                }

                StmtHit(2);
                this.\u03b3retVal = ALSystemNumeric.ALRandom(this.maxValue);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 713815787")]
        public int GetSeed()
        {
            using (GetSeed_Scope__1885673659 \u03b2scope = new GetSeed_Scope__1885673659(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetSeed")]
        [SignatureSpan(37154757063999509L)]
        [SourceSpans(37717681247748115L, 37999151929557000L)]
        private sealed class GetSeed_Scope__1885673659 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => GetSeed_Scope__1885673659.\u03b1scopeId; set => GetSeed_Scope__1885673659.\u03b1scopeId = value; }

            internal GetSeed_Scope__1885673659(Codeunit130500 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.seed;
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 3277835318")]
        public System.Guid GuidValue()
        {
            using (GuidValue_Scope__885965549 \u03b2scope = new GuidValue_Scope__885965549(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GuidValue")]
        [SignatureSpan(33777057342685207L)]
        [SourceSpans(34339981526433819L, 34621452208242696L)]
        private sealed class GuidValue_Scope__885965549 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public System.Guid \u03b3retVal = default(System.Guid);
            protected override uint RawScopeId { get => GuidValue_Scope__885965549.\u03b1scopeId; set => GuidValue_Scope__885965549.\u03b1scopeId = value; }

            internal GuidValue_Scope__885965549(Codeunit130500 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = System.Guid.NewGuid();
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 2571831489")]
        public int IntegerInRange(int maxValue)
        {
            using (IntegerInRange_Scope_852555984 \u03b2scope = new IntegerInRange_Scope_852555984(this, maxValue))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("IntegerInRange")]
        [SignatureSpan(3940709804408860L)]
        [SourceSpans(4503646873059351L, 4785126144802836L, 5066583941709861L, 5348054623518728L)]
        private sealed class IntegerInRange_Scope_852555984 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxValue")]
            public int maxValue;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => IntegerInRange_Scope_852555984.\u03b1scopeId; set => IntegerInRange_Scope_852555984.\u03b1scopeId = value; }

            internal IntegerInRange_Scope_852555984(Codeunit130500 \u03b2parent, int maxValue) : base(\u03b2parent)
            {
                this.maxValue = maxValue;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.maxValue < 1))
                {
                    StmtHit(1);
                    this.\u03b3retVal = 1;
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = base.Parent.GetNextValue(this.maxValue);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 2283933095")]
        public int IntegerInRange_1979833688(int minValue, int maxValue)
        {
            using (IntegerInRange_Scope__1979833688 \u03b2scope = new IntegerInRange_Scope__1979833688(this, minValue, maxValue))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("IntegerInRange")]
        [SignatureSpan(5911034641842204L)]
        [SourceSpans(6473958825590851L, 6755429507399688L)]
        private sealed class IntegerInRange_Scope__1979833688 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("MinValue")]
            public int minValue;
            [NavName("MaxValue")]
            public int maxValue;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => IntegerInRange_Scope__1979833688.\u03b1scopeId; set => IntegerInRange_Scope__1979833688.\u03b1scopeId = value; }

            internal IntegerInRange_Scope__1979833688(Codeunit130500 \u03b2parent, int minValue, int maxValue) : base(\u03b2parent)
            {
                this.minValue = minValue;
                this.maxValue = maxValue;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = this.minValue - 1 + base.Parent.GetNextValue(this.maxValue - this.minValue + 1);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 2117752292")]
        public void SetDefaultSeed()
        {
            using (SetDefaultSeed_Scope_1911422895 \u03b2scope = new SetDefaultSeed_Scope_1911422895(this))
                \u03b2scope.Run();
        }

        [NavName("SetDefaultSeed")]
        [SignatureSpan(38562131947880476L)]
        [SourceSpans(39125056131629080L, 39406531108405282L, 39688001790214152L)]
        private sealed class SetDefaultSeed_Scope_1911422895 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => SetDefaultSeed_Scope_1911422895.\u03b1scopeId; set => SetDefaultSeed_Scope_1911422895.\u03b1scopeId = value; }

            internal SetDefaultSeed_Scope_1911422895(Codeunit130500 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                base.Parent.seedSet = true;
                StmtHit(1);
                base.Parent.SetSeed(ALSystemDate.GetALTime(this.Session) - NavTime.Create(1U));
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 1329576490")]
        public void SetSeed(int newSeed)
        {
            using (SetSeed_Scope__1071630414 \u03b2scope = new SetSeed_Scope__1071630414(this, newSeed))
                \u03b2scope.Run();
        }

        [NavName("SetSeed")]
        [SignatureSpan(35184432226566165L)]
        [SourceSpans(35747356410314776L, 36028831387090968L, 36310306363867160L, 36591777045676040L)]
        private sealed class SetSeed_Scope__1071630414 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("NewSeed")]
            public int newSeed;
            protected override uint RawScopeId { get => SetSeed_Scope__1071630414.\u03b1scopeId; set => SetSeed_Scope__1071630414.\u03b1scopeId = value; }

            internal SetSeed_Scope__1071630414(Codeunit130500 \u03b2parent, int newSeed) : base(\u03b2parent)
            {
                this.newSeed = newSeed;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                base.Parent.seed = this.newSeed;
                StmtHit(1);
                base.Parent.seedSet = true;
                StmtHit(2);
                ALSystemNumeric.ALRandomize(base.Parent.seed);
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 2846663002 - Method 1861200453")]
        public NavText UnicodeText(int length)
        {
            using (UnicodeText_Scope_1366092468 \u03b2scope = new UnicodeText_Scope_1366092468(this, length))
            {
                \u03b2scope.Run();
                return \u03b2scope._String;
            }
        }

        [NavName("UnicodeText")]
        [SignatureSpan(28429032783937561L)]
        [SourceSpans(29554906921304116L, 29836399077883956L, 30117856874790933L, 30399327556599816L)]
        private sealed class UnicodeText_Scope_1366092468 : NavMethodScope<Codeunit130500>
        {
            public static uint \u03b1scopeId;
            [NavName("Length")]
            public int length;
            [ReturnValue("String")]
            [NavName("String")]
            public NavText _String = NavText.Default(0);
            [NavName("i")]
            public int i = default(int);
            protected override uint RawScopeId { get => UnicodeText_Scope_1366092468.\u03b1scopeId; set => UnicodeText_Scope_1366092468.\u03b1scopeId = value; }

            internal UnicodeText_Scope_1366092468(Codeunit130500 \u03b2parent, int length) : base(\u03b2parent)
            {
                this.length = length;
            }

            protected override void OnRun()
            {
                this.i = 1;
                StmtHit(0);
                int @tmp0 = this.length;
                for (; this.i <= @tmp0;)
                {
                    {
                        CStmtHit(1);
                        this._String = new NavText(ALSystemString.SetChar(this._String, this.i - 1, ALCompiler.ToChar(base.Parent.IntegerInRange_1979833688(1072, 1103))));
                    }

                    l_105_8:
                        if (this.i >= @tmp0)
                            break;
                    this.i = this.i + 1;
                }

                StmtHit(2);
                this._String = this._String;
                return;
            }
        }
    }
}
