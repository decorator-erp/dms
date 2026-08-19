using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Gates;

/// <summary>
/// The tenth and last <see cref="ConditionType"/> to get a handler, closing a gap open since Phase 2:
/// until now <c>ConditionType.Budget</c> was rejected at gate creation with
/// <c>UnsupportedConditionType</c>, because the module that owns budgets did not exist.
///
/// <para><b>It validates nothing, and that is the ported behaviour rather than a stub.</b> The backend's
/// own <c>BudgetHandler.ValidateAsync</c> returns success unconditionally. Create-time validation asks
/// "can this gate be created against this owner" — and a Budget gate can always be created: a task or
/// stage with no budget at all is precisely the case the gate exists to block, so refusing to create it
/// would invert the feature. Whether the budget is <i>satisfied</i> is a different question, decided
/// continuously by <c>FinanceGateVerdicts</c> and delivered as an integration event.</para>
///
/// <para>That split is why <c>GateConditionContext</c> was NOT extended for Finance. The context carries
/// what a validator needs, and this validator needs nothing; adding budget fields to it would have been
/// a Projects.Contracts change made for a consumer that never reads them.</para>
///
/// <para><b>Registered by <c>FinanceModule</c>'s Scrutor scan over <c>Finance.Application</c></b>, the
/// same define-in-one-module / implement-in-many shape Documents and CRM already use. Finance is the
/// fourth module to contribute a gate type and the one that completes the set —
/// <c>GateConditionCoverageTests</c> now asserts every <c>ConditionType</c> has a handler.</para>
/// </summary>
internal sealed class BudgetHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.Budget;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());
}
