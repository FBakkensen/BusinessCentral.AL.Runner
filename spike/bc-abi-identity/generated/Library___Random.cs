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

    [NavCodeunitOptions(NavCodeunitOptions.SingleInstance, 0, CodeunitSubType.Normal, false)]
    public sealed class Codeunit130440 : NavCodeunit
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

        public Codeunit130440(ITreeObject parent) : base(parent, 130440)
        {
        }

        public override string ObjectName => "Library - Random";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 955389730:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "RandDec");
                    return RandDec((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 1190567314:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "RandDecInRange");
                    return RandDecInRange((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]), (int)ALCompiler.ObjectToInt32(args[2]));
                    break;
                case 941168150:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "RandDecInDecimalRange");
                    return RandDecInDecimalRange((Decimal18)ALCompiler.ObjectToDecimal(args[0]), (Decimal18)ALCompiler.ObjectToDecimal(args[1]), (int)ALCompiler.ObjectToInt32(args[2]));
                    break;
                case -1962295948:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "RandInt");
                    return RandInt((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 1003431217:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "RandIntInRange");
                    return RandIntInRange((int)ALCompiler.ObjectToInt32(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 532450847:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "RandDate");
                    return RandDate((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case -339567019:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "RandDateFrom");
                    return RandDateFrom(ALCompiler.ObjectToExactNavValue<NavDate>(args[0]), (int)ALCompiler.ObjectToInt32(args[1]));
                    break;
                case 5050887:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(3, args, "RandDateFromInRange");
                    return RandDateFromInRange(ALCompiler.ObjectToExactNavValue<NavDate>(args[0]), (int)ALCompiler.ObjectToInt32(args[1]), (int)ALCompiler.ObjectToInt32(args[2]));
                    break;
                case -1264507840:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "RandPrecision");
                    return RandPrecision();
                    break;
                case -1733974039:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "RandText");
                    return RandText((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                case 1254858159:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "Init");
                    return Init();
                    break;
                case -1876723345:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(1, args, "SetSeed");
                    return SetSeed((int)ALCompiler.ObjectToInt32(args[0]));
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 130440);
                    break;
            }

            return default;
        }

        public static Codeunit130440 __Construct(ITreeObject parent)
        {
            return new Codeunit130440(parent);
        }

        public override bool IsSingleInstance => true;

        private int GetNextValue(int maxValue)
        {
            using (GetNextValue_Scope__1014773973 \u03b2scope = new GetNextValue_Scope__1014773973(this, maxValue))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("GetNextValue")]
        [SignatureSpan(27866108600188960L)]
        [SourceSpans(28429019899035670L, 28710499170779159L, 28991956967686175L, 29273427649495048L)]
        private sealed class GetNextValue_Scope__1014773973 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("MaxValue")]
            public int maxValue;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => GetNextValue_Scope__1014773973.\u03b1scopeId; set => GetNextValue_Scope__1014773973.\u03b1scopeId = value; }

            internal GetNextValue_Scope__1014773973(Codeunit130440 \u03b2parent, int maxValue) : base(\u03b2parent)
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

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 2908016598")]
        public int Init()
        {
            using (Init_Scope_1254858159 \u03b2scope = new Init_Scope_1254858159(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("Init")]
        [SignatureSpan(24206908132294674L)]
        [SourceSpans(24769832316043302L, 25051302997852168L)]
        private sealed class Init_Scope_1254858159 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => Init_Scope_1254858159.\u03b1scopeId; set => Init_Scope_1254858159.\u03b1scopeId = value; }

            internal Init_Scope_1254858159(Codeunit130440 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.SetSeed(ALSystemDate.GetALTime(this.Session) - NavTime.Create(1U));
                return;
            }
        }

        protected override void OnRun([NavByReferenceAttribute][NavObjectId(ObjectId = 0)] INavRecordHandle \u03b5rec)
        {
            using (OnRun_Scope \u03b2scope = new OnRun_Scope(this, \u03b5rec))
                \u03b2scope.Run();
        }

        [NavName("OnRun")]
        [SignatureSpan(2533326330593297L)]
        [SourceSpans(3096254809309192L)]
        private sealed class OnRun_Scope : NavTriggerMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => OnRun_Scope.\u03b1scopeId; set => OnRun_Scope.\u03b1scopeId = value; }

            internal OnRun_Scope(Codeunit130440 \u03b2parent, [NavByReferenceAttribute][NavObjectId(ObjectId = 0)] INavRecordHandle \u03b5rec) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 1197652849")]
        public NavDate RandDateFromInRange(NavDate fromDate, int fromRange, int toRange)
        {
            using (RandDateFromInRange_Scope_5050887 \u03b2scope = new RandDateFromInRange_Scope_5050887(this, fromDate, fromRange, toRange))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandDateFromInRange")]
        [SignatureSpan(18295933619994657L)]
        [SourceSpans(18858870688645151L, 19140349960388635L, 19421807757295707L, 19703278439104520L)]
        private sealed class RandDateFromInRange_Scope_5050887 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("FromDate")]
            public NavDate fromDate;
            [NavName("FromRange")]
            public int fromRange;
            [NavName("ToRange")]
            public int toRange;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => RandDateFromInRange_Scope_5050887.\u03b1scopeId; set => RandDateFromInRange_Scope_5050887.\u03b1scopeId = value; }

            internal RandDateFromInRange_Scope_5050887(Codeunit130440 \u03b2parent, NavDate fromDate, int fromRange, int toRange) : base(\u03b2parent)
            {
                this.fromDate = fromDate;
                this.fromRange = fromRange;
                this.toRange = toRange;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.fromRange >= this.toRange))
                {
                    StmtHit(1);
                    this.\u03b3retVal = this.fromDate;
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = ALSystemDate.ALCalcDate(this.Session, ALSystemString.ALStrSubstNo("<+%1D>", ALCompiler.ToNavValue(base.Parent.RandIntInRange(this.fromRange, this.toRange))), this.fromDate);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 3881919988")]
        public NavDate RandDateFrom(NavDate fromDate, int range)
        {
            using (RandDateFrom_Scope__339567019 \u03b2scope = new RandDateFrom_Scope__339567019(this, fromDate, range))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandDateFrom")]
        [SignatureSpan(16325608782561306L)]
        [SourceSpans(16888545851211796L, 17170025122955291L, 17451482919862363L, 17732953601671176L)]
        private sealed class RandDateFrom_Scope__339567019 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("FromDate")]
            public NavDate fromDate;
            [NavName("Range")]
            public int range;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => RandDateFrom_Scope__339567019.\u03b1scopeId; set => RandDateFrom_Scope__339567019.\u03b1scopeId = value; }

            internal RandDateFrom_Scope__339567019(Codeunit130440 \u03b2parent, NavDate fromDate, int range) : base(\u03b2parent)
            {
                this.fromDate = fromDate;
                this.range = range;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.range == 0))
                {
                    StmtHit(1);
                    this.\u03b3retVal = this.fromDate;
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = ALSystemDate.ALCalcDate(this.Session, ALSystemString.ALStrSubstNo("<%1D>", ALCompiler.ToNavValue(this.range / ((Decimal18)ALSystemNumeric.ALAbs(this.range)) * base.Parent.RandInt(this.range))), this.fromDate);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 2893366922")]
        public NavDate RandDate(int delta)
        {
            using (RandDate_Scope_532450847 \u03b2scope = new RandDate_Scope_532450847(this, delta))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandDate")]
        [SignatureSpan(14355283945127958L)]
        [SourceSpans(14918221013778452L, 15199700285521949L, 15481158082429026L, 15762628764237832L)]
        private sealed class RandDate_Scope_532450847 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Delta")]
            public int delta;
            [ReturnValue]
            public NavDate \u03b3retVal = NavDate.Default;
            protected override uint RawScopeId { get => RandDate_Scope_532450847.\u03b1scopeId; set => RandDate_Scope_532450847.\u03b1scopeId = value; }

            internal RandDate_Scope_532450847(Codeunit130440 \u03b2parent, int delta) : base(\u03b2parent)
            {
                this.delta = delta;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.delta == 0))
                {
                    StmtHit(1);
                    this.\u03b3retVal = ALSystemDate.ALWorkDate(this.Session);
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = ALSystemDate.ALCalcDate(this.Session, ALSystemString.ALStrSubstNo("<%1D>", ALCompiler.ToNavValue(this.delta / ((Decimal18)ALSystemNumeric.ALAbs(this.delta)) * base.Parent.RandInt(ALCompiler.ToInt32(ALSystemNumeric.ALAbs(this.delta))))), ALSystemDate.ALWorkDate(this.Session));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 1067805345")]
        public Decimal18 RandDecInDecimalRange(Decimal18 min, Decimal18 max, int precision)
        {
            using (RandDecInDecimalRange_Scope_941168150 \u03b2scope = new RandDecInDecimalRange_Scope_941168150(this, min, max, precision))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandDecInDecimalRange")]
        [SignatureSpan(7599884502499363L)]
        [SourceSpans(9288708593352740L, 9570183570128937L, 9851658546905129L, 10133133523681327L, 10414604205490184L)]
        private sealed class RandDecInDecimalRange_Scope_941168150 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Min")]
            public Decimal18 min;
            [NavName("Max")]
            public Decimal18 max;
            [NavName("Precision")]
            public int precision;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            [NavName("Min2")]
            public int min2 = default(int);
            [NavName("Max2")]
            public int max2 = default(int);
            [NavName("Pow")]
            public int pow = default(int);
            protected override uint RawScopeId { get => RandDecInDecimalRange_Scope_941168150.\u03b1scopeId; set => RandDecInDecimalRange_Scope_941168150.\u03b1scopeId = value; }

            internal RandDecInDecimalRange_Scope_941168150(Codeunit130440 \u03b2parent, Decimal18 min, Decimal18 max, int precision) : base(\u03b2parent)
            {
                this.min = min;
                this.max = max;
                this.precision = precision;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.pow = ALCompiler.ToInt32(ALSystemNumeric.ALPower(10, this.precision));
                StmtHit(1);
                this.min2 = ALCompiler.ToInt32(ALSystemNumeric.ALRound(this.min * this.pow, 1, ">"));
                StmtHit(2);
                this.max2 = ALCompiler.ToInt32(ALSystemNumeric.ALRound(this.max * this.pow, 1, "<"));
                StmtHit(3);
                this.\u03b3retVal = base.Parent.RandIntInRange(this.min2, this.max2) / ((Decimal18)this.pow);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 1287495613")]
        public Decimal18 RandDecInRange(int min, int max, int decimals)
        {
            using (RandDecInRange_Scope_1190567314 \u03b2scope = new RandDecInRange_Scope_1190567314(this, min, max, decimals))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandDecInRange")]
        [SignatureSpan(6192509618618396L)]
        [SourceSpans(6755433802367025L, 7036904484175880L)]
        private sealed class RandDecInRange_Scope_1190567314 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Min")]
            public int min;
            [NavName("Max")]
            public int max;
            [NavName("Decimals")]
            public int decimals;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => RandDecInRange_Scope_1190567314.\u03b1scopeId; set => RandDecInRange_Scope_1190567314.\u03b1scopeId = value; }

            internal RandDecInRange_Scope_1190567314(Codeunit130440 \u03b2parent, int min, int max, int decimals) : base(\u03b2parent)
            {
                this.min = min;
                this.max = max;
                this.decimals = decimals;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = this.min + base.Parent.RandDec(this.max - this.min, this.decimals);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 3935872185")]
        public Decimal18 RandDec(int range, int decimals)
        {
            using (RandDec_Scope_955389730 \u03b2scope = new RandDec_Scope_955389730(this, range, decimals))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandDec")]
        [SignatureSpan(4785134734737429L)]
        [SourceSpans(5348058918486089L, 5629529600294920L)]
        private sealed class RandDec_Scope_955389730 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Range")]
            public int range;
            [NavName("Decimals")]
            public int decimals;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => RandDec_Scope_955389730.\u03b1scopeId; set => RandDec_Scope_955389730.\u03b1scopeId = value; }

            internal RandDec_Scope_955389730(Codeunit130440 \u03b2parent, int range, int decimals) : base(\u03b2parent)
            {
                this.range = range;
                this.decimals = decimals;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = base.Parent.RandInt(ALCompiler.ToInt32(this.range * ALSystemNumeric.ALPower(10, this.decimals))) / ((Decimal18)ALSystemNumeric.ALPower(10, this.decimals));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 2209934284")]
        public int RandIntInRange(int min, int max)
        {
            using (RandIntInRange_Scope_1003431217 \u03b2scope = new RandIntInRange_Scope_1003431217(this, min, max))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandIntInRange")]
        [SignatureSpan(12947909061247004L)]
        [SourceSpans(13510833244995631L, 13792303926804488L)]
        private sealed class RandIntInRange_Scope_1003431217 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Min")]
            public int min;
            [NavName("Max")]
            public int max;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => RandIntInRange_Scope_1003431217.\u03b1scopeId; set => RandIntInRange_Scope_1003431217.\u03b1scopeId = value; }

            internal RandIntInRange_Scope_1003431217(Codeunit130440 \u03b2parent, int min, int max) : base(\u03b2parent)
            {
                this.min = min;
                this.max = max;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = this.min - 1 + base.Parent.RandInt(this.max - this.min + 1);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 1680803110")]
        public int RandInt(int range)
        {
            using (RandInt_Scope__1962295948 \u03b2scope = new RandInt_Scope__1962295948(this, range))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandInt")]
        [SignatureSpan(10977584223813653L)]
        [SourceSpans(11540521292464148L, 11822000564207636L, 12103458361114658L, 12384929042923528L)]
        private sealed class RandInt_Scope__1962295948 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Range")]
            public int range;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => RandInt_Scope__1962295948.\u03b1scopeId; set => RandInt_Scope__1962295948.\u03b1scopeId = value; }

            internal RandInt_Scope__1962295948(Codeunit130440 \u03b2parent, int range) : base(\u03b2parent)
            {
                this.range = range;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.range < 1))
                {
                    StmtHit(1);
                    this.\u03b3retVal = 1;
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = base.Parent.GetNextValue(this.range);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 3958801414")]
        public Decimal18 RandPrecision()
        {
            using (RandPrecision_Scope__1264507840 \u03b2scope = new RandPrecision_Scope__1264507840(this))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandPrecision")]
        [SignatureSpan(20266258457427995L)]
        [SourceSpans(20829182641176616L, 21110653322985480L)]
        private sealed class RandPrecision_Scope__1264507840 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => RandPrecision_Scope__1264507840.\u03b1scopeId; set => RandPrecision_Scope__1264507840.\u03b1scopeId = value; }

            internal RandPrecision_Scope__1264507840(Codeunit130440 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = 1 / ((Decimal18)ALSystemNumeric.ALPower(10, base.Parent.RandInt(5)));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 793966493")]
        public NavText RandText(int length)
        {
            using (RandText_Scope__1733974039 \u03b2scope = new RandText_Scope__1733974039(this, length))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RandText")]
        [SignatureSpan(21673633341308950L)]
        [SourceSpans(22799533248413734L, 23080999635255371L, 23362457432162346L, 23643928113971208L)]
        private sealed class RandText_Scope__1733974039 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Length")]
            public int length;
            [ReturnValue]
            public NavText \u03b3retVal = NavText.Default(0);
            [NavName("GuidTxt")]
            public NavText guidTxt = NavText.Default(0);
            protected override uint RawScopeId { get => RandText_Scope__1733974039.\u03b1scopeId; set => RandText_Scope__1733974039.\u03b1scopeId = value; }

            internal RandText_Scope__1733974039(Codeunit130440 \u03b2parent, int length) : base(\u03b2parent)
            {
                this.length = length;
            }

            protected override void OnRun()
            {
                while (CStmtHit(0) & (ALSystemString.ALStrLen(this.guidTxt) < this.length))
                {
                    StmtHit(1);
                    this.guidTxt = new NavText(this.guidTxt + (ALSystemString.ALLowercase(ALSystemString.ALDelChr(NavFormatEvaluateHelper.Format(this.Session, ALCompiler.ToNavValue(System.Guid.NewGuid())), "=", "{}-"))));
                }

                StmtHit(2);
                this.\u03b3retVal = new NavText(ALSystemString.ALCopyStr(this.guidTxt, 1, this.length));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 1118977379 - Method 2687642291")]
        public int SetSeed(int val)
        {
            using (SetSeed_Scope__1876723345 \u03b2scope = new SetSeed_Scope__1876723345(this, val))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("SetSeed")]
        [SignatureSpan(25614283016175637L)]
        [SourceSpans(26177207199924244L, 26458682176700440L, 26740157153476632L, 27021632130252819L, 27303102812061704L)]
        private sealed class SetSeed_Scope__1876723345 : NavMethodScope<Codeunit130440>
        {
            public static uint \u03b1scopeId;
            [NavName("Val")]
            public int val;
            [ReturnValue]
            public int \u03b3retVal = default(int);
            protected override uint RawScopeId { get => SetSeed_Scope__1876723345.\u03b1scopeId; set => SetSeed_Scope__1876723345.\u03b1scopeId = value; }

            internal SetSeed_Scope__1876723345(Codeunit130440 \u03b2parent, int val) : base(\u03b2parent)
            {
                this.val = val;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                base.Parent.seed = this.val;
                StmtHit(1);
                base.Parent.seedSet = true;
                StmtHit(2);
                ALSystemNumeric.ALRandomize(base.Parent.seed);
                StmtHit(3);
                this.\u03b3retVal = base.Parent.seed;
                return;
            }
        }
    }
}
