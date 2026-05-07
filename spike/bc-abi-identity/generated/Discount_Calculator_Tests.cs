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

    [NavCodeunitOptions(0, 0, CodeunitSubType.Test, false)]
    public sealed class Codeunit50900 : NavTestCodeunit
    {
        [NavName("DiscountCalc")]
        private NavCodeunitHandle discountCalc;
        [NavName("Assert")]
        private NavCodeunitHandle assert;
        protected override void OnClear()
        {
            this.discountCalc.Clear();
            this.assert.Clear();
        }

        public Codeunit50900(ITreeObject parent) : base(parent, 50900)
        {
            this.InitializeComponent();
        }

        void InitializeComponent()
        {
            this.discountCalc = new NavCodeunitHandle(this, 50100);
            this.assert = new NavCodeunitHandle(this, 130);
        }

        public override string ObjectName => "Discount Calculator Tests";
        public override bool IsCompiledForOnPremise => true;

        protected override object OnInvoke(int memberId, object[] args)
        {
            switch (memberId)
            {
                case 1029305361:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "TestApplyDiscount_10Percent");
                    TestApplyDiscount_10Percent();
                    break;
                case -259991698:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "TestApplyDiscount_ZeroPercent");
                    TestApplyDiscount_ZeroPercent();
                    break;
                case 1548957234:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "TestApplyDiscount_100Percent");
                    TestApplyDiscount_100Percent();
                    break;
                case 643850202:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "TestCalculateVAT");
                    TestCalculateVAT();
                    break;
                case -1277124873:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "TestApplyDiscount_NegativePercent");
                    TestApplyDiscount_NegativePercent();
                    break;
                case -1279881514:
                    SpikeShims.RuntimeShim.ThrowIfWrongArgumentCount(0, args, "TestApplyDiscount_Over100Percent");
                    TestApplyDiscount_Over100Percent();
                    break;
                default:
                    NavRuntimeHelpers.CompilationError(Lang.WrongReference, memberId, 50900);
                    break;
            }

            return default;
        }

        public static Codeunit50900 __Construct(ITreeObject parent)
        {
            return new Codeunit50900(parent);
        }

        [NavTest("TestApplyDiscount_100Percent", TestMethodNo = 794, TestPermissions = NavTestPermissions.Restrictive), NavFunctionVisibility(FunctionVisibility.External)]
        public void TestApplyDiscount_100Percent()
        {
            using (TestApplyDiscount_100Percent_Scope_1548957234 \u03b2scope = new TestApplyDiscount_100Percent_Scope_1548957234(this))
                \u03b2scope.Run();
        }

        [NavName("TestApplyDiscount_100Percent")]
        [SignatureSpan(8725784409604138L)]
        [SourceSpans(9851658546905143L, 10133133523681351L, 10414604205490184L)]
        private sealed class TestApplyDiscount_100Percent_Scope_1548957234 : NavMethodScope<Codeunit50900>
        {
            public static uint \u03b1scopeId;
            [NavName("Result")]
            public Decimal18 result = Decimal18.Zero;
            protected override uint RawScopeId { get => TestApplyDiscount_100Percent_Scope_1548957234.\u03b1scopeId; set => TestApplyDiscount_100Percent_Scope_1548957234.\u03b1scopeId = value; }

            internal TestApplyDiscount_100Percent_Scope_1548957234(Codeunit50900 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.result = ((Decimal18)ALCompiler.ObjectToDecimal(base.Parent.discountCalc.Target.Invoke(1673903542, new object[] { 250, 100 })));
                StmtHit(1);
                base.Parent.assert.Target.Invoke(-1626256597, new object[] { ALCompiler.ToVariant(this, 0), ALCompiler.ToVariant(this, this.result), new NavText("100% discount should return zero") });
            }
        }

        [NavTest("TestApplyDiscount_10Percent", TestMethodNo = 166, TestPermissions = NavTestPermissions.Restrictive), NavFunctionVisibility(FunctionVisibility.External)]
        public void TestApplyDiscount_10Percent()
        {
            using (TestApplyDiscount_10Percent_Scope_1029305361 \u03b2scope = new TestApplyDiscount_10Percent_Scope_1029305361(this))
                \u03b2scope.Run();
        }

        [NavName("TestApplyDiscount_10Percent")]
        [SignatureSpan(2533334920527913L)]
        [SourceSpans(4222159011381302L, 5066583941709903L, 5348054623518728L)]
        private sealed class TestApplyDiscount_10Percent_Scope_1029305361 : NavMethodScope<Codeunit50900>
        {
            public static uint \u03b1scopeId;
            [NavName("Result")]
            public Decimal18 result = Decimal18.Zero;
            protected override uint RawScopeId { get => TestApplyDiscount_10Percent_Scope_1029305361.\u03b1scopeId; set => TestApplyDiscount_10Percent_Scope_1029305361.\u03b1scopeId = value; }

            internal TestApplyDiscount_10Percent_Scope_1029305361(Codeunit50900 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.result = ((Decimal18)ALCompiler.ObjectToDecimal(base.Parent.discountCalc.Target.Invoke(1673903542, new object[] { 200, 10 })));
                StmtHit(1);
                base.Parent.assert.Target.Invoke(-1626256597, new object[] { ALCompiler.ToVariant(this, 180), ALCompiler.ToVariant(this, this.result), new NavText("Expected 10% discount on 200 to be 180") });
            }
        }

        [NavTest("TestApplyDiscount_NegativePercent", TestMethodNo = 1253, TestPermissions = NavTestPermissions.Restrictive), NavFunctionVisibility(FunctionVisibility.External)]
        public void TestApplyDiscount_NegativePercent()
        {
            using (TestApplyDiscount_NegativePercent_Scope__1277124873 \u03b2scope = new TestApplyDiscount_NegativePercent_Scope__1277124873(this))
                \u03b2scope.Run();
        }

        [NavName("TestApplyDiscount_NegativePercent")]
        [SignatureSpan(13792333991575599L)]
        [SourceSpans(14636784691707961L, 15481158082429001L, 15762628764237832L)]
        private sealed class TestApplyDiscount_NegativePercent_Scope__1277124873 : NavMethodScope<Codeunit50900>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => TestApplyDiscount_NegativePercent_Scope__1277124873.\u03b1scopeId; set => TestApplyDiscount_NegativePercent_Scope__1277124873.\u03b1scopeId = value; }

            internal TestApplyDiscount_NegativePercent_Scope__1277124873(Codeunit50900 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                this.AssertError(() =>
                {
                    StmtHit(0);
                    base.Parent.discountCalc.Target.Invoke(1673903542, new object[] { 200, -10 });
                });
                StmtHit(1);
                base.Parent.assert.Target.Invoke(-414868753, new object[] { new NavText("Discount percentage must not be negative") });
            }
        }

        [NavTest("TestApplyDiscount_Over100Percent", TestMethodNo = 1582, TestPermissions = NavTestPermissions.Restrictive), NavFunctionVisibility(FunctionVisibility.External)]
        public void TestApplyDiscount_Over100Percent()
        {
            using (TestApplyDiscount_Over100Percent_Scope__1279881514 \u03b2scope = new TestApplyDiscount_Over100Percent_Scope__1279881514(this))
                \u03b2scope.Run();
        }

        [NavName("TestApplyDiscount_Over100Percent")]
        [SignatureSpan(16607083759337518L)]
        [SourceSpans(17451534459469881L, 18295907850190920L, 18577378531999752L)]
        private sealed class TestApplyDiscount_Over100Percent_Scope__1279881514 : NavMethodScope<Codeunit50900>
        {
            public static uint \u03b1scopeId;
            protected override uint RawScopeId { get => TestApplyDiscount_Over100Percent_Scope__1279881514.\u03b1scopeId; set => TestApplyDiscount_Over100Percent_Scope__1279881514.\u03b1scopeId = value; }

            internal TestApplyDiscount_Over100Percent_Scope__1279881514(Codeunit50900 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                this.AssertError(() =>
                {
                    StmtHit(0);
                    base.Parent.discountCalc.Target.Invoke(1673903542, new object[] { 200, 150 });
                });
                StmtHit(1);
                base.Parent.assert.Target.Invoke(-414868753, new object[] { new NavText("Discount percentage must not exceed 100") });
            }
        }

        [NavTest("TestApplyDiscount_ZeroPercent", TestMethodNo = 546, TestPermissions = NavTestPermissions.Restrictive), NavFunctionVisibility(FunctionVisibility.External)]
        public void TestApplyDiscount_ZeroPercent()
        {
            using (TestApplyDiscount_ZeroPercent_Scope__259991698 \u03b2scope = new TestApplyDiscount_ZeroPercent_Scope__259991698(this))
                \u03b2scope.Run();
        }

        [NavName("TestApplyDiscount_ZeroPercent")]
        [SignatureSpan(6192509618618411L)]
        [SourceSpans(7318383755919413L, 7599858732695635L, 7881329414504456L)]
        private sealed class TestApplyDiscount_ZeroPercent_Scope__259991698 : NavMethodScope<Codeunit50900>
        {
            public static uint \u03b1scopeId;
            [NavName("Result")]
            public Decimal18 result = Decimal18.Zero;
            protected override uint RawScopeId { get => TestApplyDiscount_ZeroPercent_Scope__259991698.\u03b1scopeId; set => TestApplyDiscount_ZeroPercent_Scope__259991698.\u03b1scopeId = value; }

            internal TestApplyDiscount_ZeroPercent_Scope__259991698(Codeunit50900 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.result = ((Decimal18)ALCompiler.ObjectToDecimal(base.Parent.discountCalc.Target.Invoke(1673903542, new object[] { 100, 0 })));
                StmtHit(1);
                base.Parent.assert.Target.Invoke(-1626256597, new object[] { ALCompiler.ToVariant(this, 100), ALCompiler.ToVariant(this, this.result), new NavText("Zero discount should return original price") });
            }
        }

        [NavTest("TestCalculateVAT", TestMethodNo = 1031, TestPermissions = NavTestPermissions.Restrictive), NavFunctionVisibility(FunctionVisibility.External)]
        public void TestCalculateVAT()
        {
            using (TestCalculateVAT_Scope_643850202 \u03b2scope = new TestCalculateVAT_Scope_643850202(this))
                \u03b2scope.Run();
        }

        [NavName("TestCalculateVAT")]
        [SignatureSpan(11259059200589854L)]
        [SourceSpans(12384933337890869L, 12666408314667078L, 12947878996475912L)]
        private sealed class TestCalculateVAT_Scope_643850202 : NavMethodScope<Codeunit50900>
        {
            public static uint \u03b1scopeId;
            [NavName("Result")]
            public Decimal18 result = Decimal18.Zero;
            protected override uint RawScopeId { get => TestCalculateVAT_Scope_643850202.\u03b1scopeId; set => TestCalculateVAT_Scope_643850202.\u03b1scopeId = value; }

            internal TestCalculateVAT_Scope_643850202(Codeunit50900 \u03b2parent) : base(\u03b2parent)
            {
            }

            protected override void OnRun()
            {
                StmtHit(0);
                this.result = ((Decimal18)ALCompiler.ObjectToDecimal(base.Parent.discountCalc.Target.Invoke(564529948, new object[] { 100, 19 })));
                StmtHit(1);
                base.Parent.assert.Target.Invoke(-1626256597, new object[] { ALCompiler.ToVariant(this, 19), ALCompiler.ToVariant(this, this.result), new NavText("VAT on 100 at 19% should be 19") });
            }
        }
    }
}
