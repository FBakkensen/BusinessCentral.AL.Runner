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
    public sealed class Codeunit50100 : NavCodeunit
    {
        public Codeunit50100(ITreeObject parent) : base(parent, 50100)
        {
        }

        public override string ObjectName => "Discount Calculator";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 1673903542:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "ApplyDiscount");
                    return ApplyDiscount((Decimal18)ALCompiler.ObjectToDecimal(args[0]), (Decimal18)ALCompiler.ObjectToDecimal(args[1]));
                    break;
                case 564529948:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "CalculateVAT");
                    return CalculateVAT((Decimal18)ALCompiler.ObjectToDecimal(args[0]), (Decimal18)ALCompiler.ObjectToDecimal(args[1]));
                    break;
                case -1227115268:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(2, args, "RoundToNearest");
                    return RoundToNearest((Decimal18)ALCompiler.ObjectToDecimal(args[0]), (Decimal18)ALCompiler.ObjectToDecimal(args[1]));
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 50100);
                    break;
            }

            return default;
        }

        public static Codeunit50100 __Construct(ITreeObject parent)
        {
            return new Codeunit50100(parent);
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 4198214571 - Method 3079365950")]
        public Decimal18 ApplyDiscount(Decimal18 originalPrice, Decimal18 discountPercent)
        {
            using (ApplyDiscount_Scope_1673903542 \u03b2scope = new ApplyDiscount_Scope_1673903542(this, originalPrice, discountPercent))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("ApplyDiscount")]
        [SignatureSpan(563010083094555L)]
        [SourceSpans(1125947151745054L, 1407426423488574L, 1688897105297440L, 1970376377040957L, 2251834173947962L, 2533304855756808L)]
        private sealed class ApplyDiscount_Scope_1673903542 : NavMethodScope<Codeunit50100>
        {
            public static uint \u03b1scopeId;
            [NavName("OriginalPrice")]
            public Decimal18 originalPrice;
            [NavName("DiscountPercent")]
            public Decimal18 discountPercent;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => ApplyDiscount_Scope_1673903542.\u03b1scopeId; set => ApplyDiscount_Scope_1673903542.\u03b1scopeId = value; }

            internal ApplyDiscount_Scope_1673903542(Codeunit50100 \u03b2parent, Decimal18 originalPrice, Decimal18 discountPercent) : base(\u03b2parent)
            {
                this.originalPrice = originalPrice;
                this.discountPercent = discountPercent;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.discountPercent < 0))
                {
                    StmtHit(1);
                    NavDialog.ALError(this.Session, System.Guid.Parse("8da61efd-0002-0003-0507-0b0d1113171d"), "Discount percentage must not be negative");
                }

                if (CStmtHit(2) & (this.discountPercent > 100))
                {
                    StmtHit(3);
                    NavDialog.ALError(this.Session, System.Guid.Parse("8da61efd-0002-0003-0507-0b0d1113171d"), "Discount percentage must not exceed 100");
                }

                StmtHit(4);
                this.\u03b3retVal = this.originalPrice * (1 - this.discountPercent / ((Decimal18)100));
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 4198214571 - Method 481671152")]
        public Decimal18 CalculateVAT(Decimal18 netAmount, Decimal18 vATPercent)
        {
            using (CalculateVAT_Scope_564529948 \u03b2scope = new CalculateVAT_Scope_564529948(this, netAmount, vATPercent))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("CalculateVAT")]
        [SignatureSpan(3096284874080282L)]
        [SourceSpans(3659209057828907L, 3940679739637768L)]
        private sealed class CalculateVAT_Scope_564529948 : NavMethodScope<Codeunit50100>
        {
            public static uint \u03b1scopeId;
            [NavName("NetAmount")]
            public Decimal18 netAmount;
            [NavName("VATPercent")]
            public Decimal18 vATPercent;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => CalculateVAT_Scope_564529948.\u03b1scopeId; set => CalculateVAT_Scope_564529948.\u03b1scopeId = value; }

            internal CalculateVAT_Scope_564529948(Codeunit50100 \u03b2parent, Decimal18 netAmount, Decimal18 vATPercent) : base(\u03b2parent)
            {
                this.netAmount = netAmount;
                this.vATPercent = vATPercent;
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.\u03b3retVal = this.netAmount * this.vATPercent / ((Decimal18)100);
                return;
            }
        }

        [NavFunctionVisibility(FunctionVisibility.External), NavCaption(TranslationKey = "Codeunit 4198214571 - Method 1759089062")]
        public Decimal18 RoundToNearest(Decimal18 value, Decimal18 precision)
        {
            using (RoundToNearest_Scope__1227115268 \u03b2scope = new RoundToNearest_Scope__1227115268(this, value, precision))
            {
                \u03b2scope.Run();
                return \u03b2scope.\u03b3retVal;
            }
        }

        [NavName("RoundToNearest")]
        [SignatureSpan(4503659757961244L)]
        [SourceSpans(5066596826611736L, 5348076098355224L, 5629533895262262L, 5911004577071112L)]
        private sealed class RoundToNearest_Scope__1227115268 : NavMethodScope<Codeunit50100>
        {
            public static uint \u03b1scopeId;
            [NavName("Value")]
            public Decimal18 value;
            [NavName("Precision")]
            public Decimal18 precision;
            [ReturnValue]
            public Decimal18 \u03b3retVal = Decimal18.Zero;
            protected override uint RawScopeId { get => RoundToNearest_Scope__1227115268.\u03b1scopeId; set => RoundToNearest_Scope__1227115268.\u03b1scopeId = value; }

            internal RoundToNearest_Scope__1227115268(Codeunit50100 \u03b2parent, Decimal18 value, Decimal18 precision) : base(\u03b2parent)
            {
                this.value = value;
                this.precision = precision;
            }

            protected override void OnRun()
            {
                if (CStmtHit(0) & (this.precision == 0))
                {
                    StmtHit(1);
                    this.\u03b3retVal = this.value;
                    return;
                }

                StmtHit(2);
                this.\u03b3retVal = ALSystemNumeric.ALRound(this.value / ((Decimal18)this.precision), 1) * this.precision;
                return;
            }
        }
    }
}
