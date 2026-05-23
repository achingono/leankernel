using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IResponseQualityGate
{
    QualityGateResult Evaluate(QualityEvaluationContext context);
}
